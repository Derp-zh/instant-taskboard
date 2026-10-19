using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Taskboard.Core;
using Forms = System.Windows.Forms;

namespace Taskboard.Desktop;

internal sealed partial class BoardController : IDisposable
{
    public BoardEngine Engine { get; private set; }
    public AtomicStore Store { get; private set; }
    public bool Expanded { get; private set; }
    public bool SuppressCollapse { get; set; }
    public bool Isolated { get; }
    public bool Smoke { get; }
    public readonly List<ColumnSurface> Columns = new();
    internal ColumnSurface[] VisibleColumns => Columns.Where(x => x.Definition.Visible && !x.Definition.Deleted).ToArray();
    public readonly List<EditorSurface> Editors = new();
    public BallSurface Ball { get; }
    internal CardDragSession? CardDrag { get; set; }
    private readonly DraftStackSurface draftStack;
    private readonly Dictionary<Guid, TaskEditorWindow> taskWindows = new();
    internal Guid? ActiveDraft => draftOrder.Count > 0 ? draftOrder[0] : null;
    private readonly List<SurfaceWindow> auxiliary = new();
    private readonly List<SurfaceWindow> hiddenGroup = new();
    private readonly List<IntPtr> hiddenDialogs = new();
    private readonly List<Guid> draftOrder = new();
    private SurfaceWindow? previouslyActive;
    private bool layingOutDrafts;
    private bool restoringGroup;
    internal bool IsRestoringWorkspace => restoringGroup;
    private int draftPage;
    private Guid? lastClosedDraft;
    internal Rect[] LastDraftBounds { get; private set; } = [];
    private SettingsSurface? settings;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    internal CodexObserver Codex { get; private set; } = new();
    internal NotificationListeningService Listening { get; }
    internal string CodexStatus => Codex.Status;
    private void UpdateCodexWatches() => Codex.Watch(Engine.State.Tasks.SelectMany(task => task.Stages.Where(stage => stage.Codex is not null)
        .Select(stage => new CodexWatch(task.Id, stage.Id, stage.Codex!.BindingId, stage.Codex.ThreadId, stage.Codex.Generation))).ToArray());
    public async Task RestartCodex()
    {
        await Codex.DisposeAsync(); Codex = new CodexObserver(); AttachCodex(); UpdateCodexWatches();
        Codex.Start(Engine.State.Preferences.ConnectionConfigs.GetValueOrDefault("codex.executable",CodexObserver.FindExecutable()),Engine.State.Preferences.ConnectionConfigs.GetValueOrDefault("codex.endpoint",""));
    }
    private void AttachCodex()
    {
        var observer = Codex;
        observer.Changed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (exiting || Codex != observer) return;
            if (!observer.Authoritative)
            {
                var changed = Engine.State.Tasks.Where(x => x.Stages.Any(stage => stage.Codex is { Phase: not CodexPhase.Unknown })).ToArray();
                foreach (var binding in changed.SelectMany(x => x.Stages).Select(x => x.Codex).OfType<CodexBinding>()) binding.Phase = CodexPhase.Unknown;
                if (changed.Length > 0) Engine.Notify(changed.Select(x => x.Id).ToArray());
            }
            settings?.RefreshCodexStatus();
        }));
        observer.Event += (watch,message,sequence,epoch,authoritative) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (exiting || Codex != observer || observer.Epoch != epoch) return;
            var task = Engine.State.Tasks.FirstOrDefault(x => x.Id == watch.TaskId);
            var binding = task?.Stages.FirstOrDefault(x => x.Id == watch.StageId)?.Codex;
            if (task is null || binding?.ThreadId != watch.ThreadId || binding.BindingId != watch.BindingId) return;
            var before = task.Lane;
            if (CodexState.Apply(task,watch.StageId,message,sequence,watch.Generation,epoch,authoritative))
            { if (before != task.Lane) { Engine.State.Tasks.Remove(task); Engine.State.Tasks.Add(task); } task.UpdatedUtc = DateTime.UtcNow; Engine.Notify([task.Id]); }
        }));
    }
    private readonly DispatcherTimer saveTimer;
    private readonly DispatcherTimer reminderTimer;
    private readonly SemaphoreSlim saveGate = new(1);
    private readonly ForegroundCallback foregroundCallback;
    private readonly IntPtr foregroundHook;
    private long dirtyVersion;
    private long savedVersion = -1;
    private bool exiting;
    private int explicitSave;
    private string? lastError;
    private Guid[] notified = [];
    private TaskCard? highlighted;
    private readonly List<Mutex> directoryLocks = new();
    private readonly string pathSelectionFile;
    private bool dataTransition;
    private void SetDataTransition(bool active)
    {
        dataTransition = active; foreach (var column in Columns) column.Root.IsEnabled = !active;
        foreach (var editor in Editors) editor.Root.IsEnabled = !active;
        draftStack.Root.IsEnabled = !active; if (settings is not null) settings.Root.IsEnabled = !active;
    }
    public BoardController(string dataDirectory, bool isolated, bool smoke)
    {
        Isolated = isolated; Smoke = smoke; Store = new AtomicStore(dataDirectory, isolated ? Path.Combine(dataDirectory, "configuration") : DataPaths.ConfigurationDirectory());
        pathSelectionFile = isolated ? Path.Combine(Path.GetFullPath(dataDirectory), "launch-settings.json") : DataPaths.SelectionFile;
        Engine = new BoardEngine(Store.Load()); AttachEngine();
        Listening = new NotificationListeningService(Application.Current.Dispatcher, () => Engine.State.Preferences.Listening, isolated,
            isolated ? Path.Combine(dataDirectory, "notification-sound-restore.json") :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InstantTaskboard", "notification-sound-restore.json"));
        Listening.Changed += () => settings?.RefreshListeningStatus();
        SurfaceWindow.SetTransparency(Engine.State.Preferences.GlassTransparency, Engine.State.Preferences.FloatingTransparency);
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        saveTimer.Tick += async (_, _) => { saveTimer.Stop(); await Flush(); };
        foreach (var definition in Engine.State.Preferences.Columns.Where(x => !x.Deleted)) Columns.Add(new ColumnSurface(this, definition) { SuppressActivation = Smoke });
        Ball = new BallSurface(this) { SuppressActivation = Smoke };
        Native.SetBall(Ball);
        draftStack = new DraftStackSurface(this) { SuppressActivation = Smoke };
        RestorePositions();
        Ui.WindowCreated = window => { RegisterAuxiliary(window); Clamp(window); };
        trayIcon = Brand.TrayIcon(); tray = new Forms.NotifyIcon { Icon = trayIcon, Visible = !Smoke, Text = Brand.Name };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
        tray.BalloonTipClicked += (_, _) => { SetExpanded(true); ShowNotificationTasks(); };
        tray.ContextMenuStrip = BuildTrayMenu();
        reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        reminderTimer.Tick += async (_, _) => { await CheckReminders(); await CheckAutomaticArchives(); }; reminderTimer.Start();
        foregroundCallback = (_, _, handle, _, _, _, _) =>
        {
            if (handle == IntPtr.Zero || exiting) return;
            GetWindowThreadProcessId(handle, out var process);
            if (process == Environment.ProcessId) { Application.Current.Dispatcher.BeginInvoke(new Action(Native.RaiseBall)); return; }
            // Losing focus is also reported when a floating window closes.
            // Only the mouse observer may collapse the workspace on an actual press.
        };
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 0);
        if (!Smoke) outsideClicks = new OutsideClickObserver(this);
    }
    public async Task Start()
    {
        Ball.RefreshData();
        foreach(var column in VisibleColumns) column.PrimeWindow();
        if (Engine.State.Preferences.BallVisible) Ball.Show();
        if (!Isolated)
        {
            var machine = StartupLinks.MachineIdentity();
            var markers = Engine.State.Preferences.MachineMarkers;
            if (!markers.TryGetValue("shortcut:" + machine, out var oldShortcut) || !string.Equals(oldShortcut, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    StartupLinks.Shortcut();
                    markers["shortcut:" + machine] = Environment.ProcessPath ?? AppContext.BaseDirectory;
                    Engine.Touch(); await Flush();
                }
                catch (Exception e) { Report("首次桌面快捷方式创建失败：" + e.Message); }
            }
            try
            {
                StartupLinks.SetAutoStart(Engine.State.Preferences.AutoStart);
                markers["startup:" + machine] = Environment.ProcessPath ?? AppContext.BaseDirectory;
                Engine.Touch(); await Flush();
            }
            catch (Exception e) { Report("开机启动入口更新失败：" + e.Message); }
        }
        if (Store.RecoveryMessage is not null) Report(Store.RecoveryMessage);
        if (Engine.State.Sessions.Count > 0) Ball.ToolTip = $"已恢复{Engine.State.Sessions.Count}份草稿或未提交编辑，展开后继续。";
        await CheckReminders();
        await CheckAutomaticArchives(true);
        foreach (var binding in Engine.State.Tasks.SelectMany(x => x.Stages).Select(x => x.Codex).OfType<CodexBinding>()) { binding.Phase = CodexPhase.Unknown; binding.SourceEpoch = ""; }
        if (!Isolated) await RestartCodex();
        await Listening.Apply();
        RefreshHotkey();
        foreach (var column in VisibleColumns) column.WarmAnimation();
    }
    private void AttachEngine() { Engine.Changed += OnChanged; Engine.Dirty += OnDirty; }
    private void DetachEngine() { retainedStages.Clear(); Engine.Changed -= OnChanged; Engine.Dirty -= OnDirty; }
    private void OnDirty() { dirtyVersion++; if (saveTimer is not null) { saveTimer.Stop(); saveTimer.Start(); } }
    private void OnChanged(IReadOnlyCollection<Guid> ids)
    {
        UpdateCodexWatches();
        if (Columns.Any(x => x.Definition.Deleted) || Engine.State.Preferences.Columns.Any(x => !x.Deleted && !Columns.Any(y => y.Definition.Id == x.Id))) SynchronizeColumns();
        foreach (var column in Columns) { column.Refresh(); column.RefreshCards(ids); }
        Ball?.RefreshData();
        foreach (var editor in Editors.Where(x => x.Session.TaskId is Guid id && !Engine.State.Tasks.Any(t => t.Id == id)).ToArray())
        { RemoveEditor(editor); editor.Destroy(); }
        RefreshDraftStack();
    }
    public async Task<bool> Flush()
    {
        await saveGate.WaitAsync();
        try
        {
            if (savedVersion == dirtyVersion && File.Exists(Store.MainPath)) return true;
            var version = dirtyVersion;
            var snapshot = Copies.Of(Engine.State);
            await Store.SaveAsync(snapshot); savedVersion = version; lastError = null;
            RefreshDraftStack();
            if (dirtyVersion != savedVersion) { saveTimer.Stop(); saveTimer.Start(); }
            return true;
        }
        catch (Exception e) { Report("保存失败，内容仍留在当前窗口，请检查磁盘权限或空间后重试：" + e.Message); return false; }
        finally { saveGate.Release(); }
    }
    public async void Persist(Func<Task>? success = null)
    { if (await Flush() && success is not null) await success(); }
    public async Task<bool> SubmitSession(EditSession session)
    {
        await humanTaskGate.WaitAsync();
        try { await CleanupRetainedStages(); return await SubmitSessionLocked(session); }
        finally { humanTaskGate.Release(); }
    }
    private async Task<bool> SubmitSessionLocked(EditSession session)
    {
        await saveGate.WaitAsync(); explicitSave++;
        try
        {
            // Persist a validated proposed submission before altering official in-memory tasks.
            var frozen = Copies.Of(session);
            var candidate = Copies.Of(Engine.State);
            var proposal = new BoardEngine(candidate);
            var proposedSession = candidate.Sessions.FirstOrDefault(x => x.Id == session.Id) ?? frozen;
            proposedSession.Working = Copies.Of(frozen.Working);
            proposedSession.StatusSelected = frozen.StatusSelected;
            proposal.Submit(proposedSession);
            var captured = dirtyVersion;
            await Store.SaveAsync(candidate);
            session.Working = frozen.Working;
            Engine.Submit(session);
            // Other editor typing can occur during disk IO; preserve it with the next snapshot.
            savedVersion = captured; lastError = null; saveTimer.Stop(); saveTimer.Start();
            return true;
        }
        catch (Exception e) { Report("任务未能保存，编辑窗口会保留：" + e.Message); return false; }
        finally { explicitSave--; saveGate.Release(); }
    }
    public void Toggle() => SetExpanded(!Expanded);
    internal double LastWorkspaceRevealMs,LastBallRevealMs,LastEditorRestoreMs;
    public void SetExpanded(bool expanded)
    {
        if (exiting || Expanded == expanded) return;
        Expanded = expanded;
        restoringGroup = expanded && hiddenGroup.Count > 0;
        workspaceGeneration++;
        outsideTarget = null;
        if (expanded) foreach (var column in Columns) column.RestartCardMarquees();
        if (!expanded)
        {
            hiddenDialogs.Clear(); hiddenDialogs.AddRange(Native.Dialogs()); foreach (var dialog in hiddenDialogs) Native.ShowWindow(dialog, 0);
            hiddenGroup.Clear(); hiddenGroup.AddRange(taskWindows.Values.Cast<SurfaceWindow>().Append(draftStack).Concat(auxiliary).Where(x => x.ShouldRestore).OrderBy(x => x.ActivationOrder));
            previouslyActive = hiddenGroup.LastOrDefault(x => x.IsActive);
            foreach (var column in Columns) column.PrepareHide();
            foreach (var editor in Editors) editor.PrepareHide();
            foreach (var window in auxiliary.ToArray()) window.Hide(); draftStack.Hide(); Ball.HidePopups();
        }
        var feedbackClock=Stopwatch.StartNew();
        Native.BeginWorkspaceChange();
        try {
        foreach (var column in Columns) { if (column.Definition.Visible) column.Reveal(expanded, true); else column.Hide(); }
        LastWorkspaceRevealMs=feedbackClock.Elapsed.TotalMilliseconds; feedbackClock.Restart();
        Ball.SetExpanded(expanded);
        LastBallRevealMs=feedbackClock.Elapsed.TotalMilliseconds; feedbackClock.Restart();
        if (expanded)
        {
            foreach (var session in Engine.State.Sessions.Where(x => x.Visible && !x.Inline).ToArray()) EnsureEditor(session);
            foreach (var window in taskWindows.Values) window.Show();
            LayoutDrafts();
            foreach (var window in hiddenGroup.Where(x => auxiliary.Contains(x) || taskWindows.Values.Contains(x)).ToArray()) { window.Show(); Native.Raise(window); }
            previouslyActive?.Activate(); foreach (var dialog in hiddenDialogs.Where(Native.IsWindow)) Native.ShowWindow(dialog, 4); hiddenDialogs.Clear(); Native.RaiseBall();
        }
        else { foreach (var window in taskWindows.Values) window.Hide(); Persist(); }
        LastEditorRestoreMs=feedbackClock.Elapsed.TotalMilliseconds;
        } finally { restoringGroup = false; Native.EndWorkspaceChange(); }
        if (expanded) _ = CleanupRetainedStages();
    }
    public EditorSurface NewTask(Lane? lane = null, Guid? columnId = null)
    {
        _ = CleanupRetainedStages();
        SetExpanded(true);
        // An untouched editor is an input surface, not another staged card.
        layingOutDrafts = true;
        try
        {
            foreach (var empty in Editors.Where(x => x.Session.TaskId is null && !x.Session.Working.HasContent).ToArray())
            { Engine.State.Sessions.Remove(empty.Session); RemoveEditor(empty); empty.Destroy(); }
        }
        finally { layingOutDrafts = false; }
        var session = Engine.NewSession(lane ?? Lane.Refining); session.StatusSelected = lane is not null;
        session.Working.ColumnId = columnId; session.Original.ColumnId = columnId;
        var editor = EnsureEditor(session); PromoteDraft(editor); draftStack.Activate(); Native.RaiseBall(); return editor;
    }
    public EditorSurface OpenNewEntry(Lane lane, Guid? columnId = null)
    {
        var saved = Engine.State.Sessions.Where(x => x.TaskId is null && !x.Inline && x.Working.HasContent).ToArray();
        SetExpanded(true);
        foreach (var session in saved) { session.Visible = true; EnsureEditor(session); }
        return NewTask(lane, columnId);
    }
    internal EditorSurface OpenDraft(EditSession session)
    {
        _ = CleanupRetainedStages();
        session.Inline = false; session.Visible = true;
        Engine.StageSession(session); SetExpanded(true);
        foreach (var saved in Engine.State.Sessions.Where(x => x.TaskId is null && !x.Inline && x.Working.HasContent).ToArray())
        { saved.Visible = true; EnsureEditor(saved); }
        var editor = EnsureEditor(session); PromoteDraft(editor); draftStack.Activate(); Native.RaiseBall(); return editor;
    }
    internal void ToggleTaskEditor(Guid id)
    {
        var existing = Editors.FirstOrDefault(x => x.Session.TaskId == id);
        if (existing?.Host is { } opening && !opening.CanInterruptTaskOpening) return;
        _ = CleanupRetainedStages();
        if (existing?.Host?.ShouldRestore == true) { existing.Suspend(); Persist(); }
        else EditTask(id);
    }
    public EditorSurface EditTask(Guid id)
    {
        _ = CleanupRetainedStages();
        SetExpanded(true); var session = Engine.Edit(id); var editor = EnsureEditor(session);
        editor.Activate(); Native.RaiseBall(); return editor;
    }
    internal EditorSurface EnsureEditor(EditSession session)
    {
        var existing = Editors.FirstOrDefault(x => x.Session.Id == session.Id); if (existing is not null) return existing;
        var editor = new EditorSurface(this, session); Editors.Add(editor);
        if (session.TaskId is null)
        {
            draftOrder.Insert(0, session.Id);
            return editor;
        }
        var window = new TaskEditorWindow(editor); taskWindows.Add(session.Id, window);
        window.Closed += (_, _) => { hiddenGroup.Remove(window); if (previouslyActive == window) previouslyActive = null; };
        var key = "edit-" + session.Id;
        if (Engine.State.Preferences.Positions.TryGetValue(key, out var position)) { window.Width = position.Width; window.Height = position.Height; }
        PositionFloating(window);
        window.BeforeOpen += () => { if (!restoringGroup) PositionFloating(window); };
        Clamp(window); return editor;
    }
    public void RemoveEditor(EditorSurface editor)
    {
        Editors.Remove(editor); draftOrder.Remove(editor.Session.Id);
        if (taskWindows.Remove(editor.Session.Id, out var window)) { hiddenGroup.Remove(window); if (previouslyActive == window) previouslyActive = null; window.Destroy(); }
        else if (Editors.Any(x => x.Session.TaskId is null)) draftStack.Detach(editor);
        Engine.State.Preferences.Positions.Remove("edit-" + editor.Session.Id); Engine.Touch();
        LayoutDrafts();
    }
    private void PromoteDraft(EditorSurface editor)
    { draftOrder.Remove(editor.Session.Id); draftOrder.Insert(0, editor.Session.Id); draftPage = 0; LayoutDrafts(); }
    public void ShowEditor(EditorSurface editor, bool activate, long? pressedAt = null)
    {
        if (!Expanded) SetExpanded(true);
        if (editor.Session.TaskId is null)
        { if (activate && (ActiveDraft != editor.Session.Id || draftStack.IsContentSwapHandoff)) draftStack.Select(editor, () => PromoteDraft(editor), pressedAt); else if (!draftStack.ShouldRestore) LayoutDrafts(); if (activate) draftStack.Activate(); }
        else if (taskWindows.TryGetValue(editor.Session.Id, out var window)) { window.Show(); if (activate) window.Activate(); }
        Native.RaiseBall();
    }
    public void EditorMoved(EditorSurface editor)
    {
        if (editor.Session.TaskId is null) { SavePosition("draft-stack", draftStack); }
        else if (editor.Host is not null) SavePosition("edit-" + editor.Session.Id, editor.Host);
    }
    public void ChangeDraftPage(int direction) { draftPage += direction; LayoutDrafts(); }
    public void RefreshDraftLayout() => LayoutDrafts();
    private void LayoutDrafts()
    {
        if (layingOutDrafts) return;
        layingOutDrafts = true;
        try
        {
            var drafts = draftOrder.Select(id => Editors.FirstOrDefault(x => x.Session.Id == id && x.Session.TaskId is null)).OfType<EditorSurface>().ToArray();
            if (drafts.Length == 0) { draftStack.ClearClosed(); LastDraftBounds = []; return; }
            if (!Expanded) { draftStack.Hide(); LastDraftBounds = []; return; }
            var area = WorkArea(Ball);
            // Reserve one fifth to the right even on narrow displays for the new-window outline.
            var width = Math.Min(430, Math.Max(80, (area.Width - 24) / 1.4));
            var height = Math.Min(650, Math.Max(80, area.Height - 24)); var step = width / 5;
            var left = area.Left + (area.Width - width) / 2; var top = area.Top + (area.Height - height) / 2;
            var opening = !draftStack.ShouldRestore && !restoringGroup;
            if (!opening && Engine.State.Preferences.Positions.TryGetValue("draft-stack", out var placement))
            { left = Math.Clamp(placement.X, area.Left, area.Right - width * 1.2); top = Math.Clamp(placement.Y, area.Top, area.Bottom - height); }
            var slots = Math.Max(1, (int)Math.Floor((area.Right - 12 - left - width) / step));
            var olderPerPage = Math.Max(0, slots - 1);
            var totalPages = olderPerPage > 0 ? Math.Max(1, (int)Math.Ceiling((drafts.Length - 1.0) / olderPerPage)) : Math.Max(1, drafts.Length);
            draftPage = Math.Clamp(draftPage, 0, totalPages - 1);
            var visible = olderPerPage > 0 ? new[] { drafts[0] }.Concat(drafts.Skip(1 + draftPage * olderPerPage).Take(olderPerPage)).ToArray() : new[] { drafts[draftPage] };
            LastDraftBounds = visible.Select((_, i) => new Rect(left + i * step, top, width, height)).Append(new Rect(left + visible.Length * step, top, width, height)).ToArray();
            if (opening)
            {
                var hostWidth = LastDraftBounds[^1].Right - left - DraftCardGeometry.EntryShift(width);
                var shift = area.Left + (area.Width - hostWidth) / 2 - left;
                LastDraftBounds = LastDraftBounds.Select(rect => new Rect(rect.Left + shift, rect.Top, rect.Width, rect.Height)).ToArray();
                Engine.State.Preferences.Positions["draft-stack"] = new WindowPosition { X = left + shift, Y = top, Width = hostWidth, Height = height };
            }
            draftStack.Layout(visible, LastDraftBounds, draftPage, totalPages);
            Native.RaiseBall();
        }
        finally { layingOutDrafts = false; }
    }
    private void RefreshDraftStack() { if (draftStack is not null && !Editors.Any(x => x.Session.TaskId is null)) draftStack.ClearClosed(); }
    public void RegisterAuxiliary(SurfaceWindow window)
    { auxiliary.Add(window); window.BeforeOpen += () => { if (!restoringGroup && window.FloatingAnimations) PositionFloating(window); }; window.Closed += (_, _) => { auxiliary.Remove(window); hiddenGroup.Remove(window); if (previouslyActive == window) previouslyActive = null; }; }
    private void PositionFloating(SurfaceWindow window)
    {
        var area = WorkArea(Ball);
        window.Width = Math.Min(window.Width, area.Width); window.Height = Math.Min(window.Height, area.Height);
        var previous = window is TaskEditorWindow ? taskWindows.Values.Where(x => x != window && x.ShouldRestore).OrderByDescending(x => x.ActivationOrder).FirstOrDefault() : null;
        window.Left = previous is not null ? previous.Left + 32 : area.Left + (area.Width - window.Width) / 2;
        window.Top = previous is not null ? previous.Top + 32 : area.Top + (area.Height - window.Height) / 2;
        Clamp(window);
    }
    public void MoveTask(Guid id, Lane lane, Guid? before, Guid? columnId = null) { _ = RunHumanTask(() => { if (Engine.State.Tasks.Any(x => x.Id == id)) Engine.Move(id, lane, before, columnId); Persist(); }); }
    public void DeleteTask(Guid id) { _ = RunHumanTask(() => { if (Engine.State.Tasks.Any(x => x.Id == id)) Engine.Delete(id); Persist(); }, id); }
    internal void DeleteDraft(EditorSurface editor)
    { _ = CleanupRetainedStages(); Engine.State.Sessions.Remove(editor.Session); Engine.Touch(); RemoveEditor(editor); editor.Destroy(); Persist(); }
    internal int DraftNumber(Guid id) => Engine.State.Sessions.Where(x => x.TaskId is null && !x.Inline)
        .Concat(Editors.Where(x => x.Session.TaskId is null).Select(x => x.Session)).DistinctBy(x => x.Id)
        .OrderBy(x => x.CreationTime).ThenBy(x => x.Id).Select((x, i) => (x.Id, Number: i + 1)).FirstOrDefault(x => x.Id == id).Number;
    public void Undo() { _ = RunHumanTask(() => { Engine.Undo(); Persist(); }); }
    public void Redo() { _ = RunHumanTask(() => { Engine.Redo(); Persist(); }); }
    public void ArchiveTask(Guid id) { _ = RunHumanTask(() => { if (Engine.State.Tasks.Any(x => x.Id == id)) Engine.Archive(id); settings?.RefreshArchive(); Persist(); }, id); }
    public void UnarchiveTask(Guid id) { _ = RunHumanTask(() => { if (Engine.State.Archives.Any(x => x.Item.Id == id)) Engine.Unarchive(id); ColumnsChanged(false); settings?.RefreshArchive(); Persist(); }); }
    public void Locate(Guid id)
    {
        var item = Engine.State.Tasks.FirstOrDefault(x => x.Id == id); if (item is null) return;
        if (item.TimerUnread) { item.TimerUnread = false; Engine.Notify([id]); }
        var definition = BoardColumns.For(Engine.State.Preferences,item); if (!definition.Visible) { definition.Visible = true; ColumnsChanged(false); }
        SetExpanded(true); Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        { highlighted?.StopFlash(); highlighted = Columns.First(x => x.Definition.Contains(item)).Locate(id); highlighted?.Flash(); }), DispatcherPriority.Loaded);
    }
    public void Settings()
    {
        if (!Expanded) SetExpanded(true);
        if (settings is null) { settings = new SettingsSurface(this); RegisterAuxiliary(settings); var area = WorkArea(Ball); settings.Left = area.Left + (area.Width - settings.Width) / 2; settings.Top = area.Top + (area.Height - settings.Height) / 2; Clamp(settings); }
        settings.Show(); settings.Activate(); Native.RaiseBall();
    }
    internal void ToggleSettings()
    {
        if (settings?.ShouldRestore == true) settings.Hide();
        else Settings();
    }
    public void OptionsChanged()
    {
        Engine.Touch(); foreach (var column in Columns) column.Repaint();
        foreach (var editor in Editors) editor.RefreshOptionLabels();
    }
    public void DeleteOption(Guid id, bool ai)
    {
        Engine.DeleteOption(id, ai);
        foreach (var editor in Editors) { editor.Session.Working.AiIds.Remove(id); editor.Session.Original.AiIds.Remove(id);
            if (editor.Session.Working.PriorityId == id) editor.Session.Working.PriorityId = null;
            if (editor.Session.Original.PriorityId == id) editor.Session.Original.PriorityId = null;
        }
        OptionsChanged();
    }
    public void ColorMenu(FieldOption option, FrameworkElement target)
    {
        var menu = new ContextMenu();
        foreach (var color in new[] { "#77CBB0", "#82AFF7", "#C1A0EB", "#F28F98", "#E9C582", "#93A6BE", "#FFA773", "#E9EAF0" })
        {
            var item = new MenuItem { Header = "● " + color, Foreground = Ui.Brush(color) };
            item.Click += (_, _) => { option.Color = color; OptionsChanged(); }; menu.Items.Add(item);
        }
        menu.PlacementTarget = target; menu.IsOpen = true;
    }
    public void ApplySettings() { foreach (var column in Columns) column.ApplySettings(); RefreshHotkey(); Ball.RefreshEdgeAutoHide(); }
    public void SavePosition(string key, SurfaceWindow window) { Engine.State.Preferences.Positions[key] = window.Position(); Engine.Touch(); }
    public void SaveBall(BallSurface window)
    {
        Engine.State.Preferences.Positions["ball"] = new WindowPosition { X = window.VisibleBallLeft, Y = window.Top, Width = 58, Height = 58 }; Engine.Touch();
    }
    public Rect WorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var screen = Forms.Screen.FromHandle(handle);
        var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice;
        var scaleX = transform?.M11 ?? 96.0 / Math.Max(96, Native.GetDpiForWindow(handle));
        var scaleY = transform?.M22 ?? scaleX;
        var r = screen.WorkingArea;
        return new Rect(r.X * scaleX, r.Y * scaleY, r.Width * scaleX, r.Height * scaleY);
    }
    public void Clamp(SurfaceWindow window)
    {
        if (Smoke) { window.SuppressActivation = true; window.Left = -10000; window.Top = -10000; window.ShowActivated = false; return; }
        var area = WorkArea(window);
        window.Width = Math.Min(window.Width, area.Width); window.Height = Math.Min(window.Height, area.Height);
        window.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - window.Width));
        window.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - window.Height));
    }
    private void SynchronizeColumns()
    {
        foreach (var column in Columns.ToArray())
        { var definition = Engine.State.Preferences.Columns.FirstOrDefault(x => x.Id == column.Definition.Id && !x.Deleted); if (definition is null) { column.PrepareHide(); column.Destroy(); Columns.Remove(column); } else column.Rebind(definition); }
        foreach (var definition in Engine.State.Preferences.Columns.Where(x => !x.Deleted && !Columns.Any(y => y.Definition.Id == x.Id))) Columns.Add(new ColumnSurface(this,definition) { SuppressActivation = Smoke });
        Columns.Sort((a,b) => Engine.State.Preferences.Columns.IndexOf(a.Definition).CompareTo(Engine.State.Preferences.Columns.IndexOf(b.Definition)));
    }
    public void ColumnsChanged(bool layout = true)
    {
        SynchronizeColumns(); if (layout) { foreach (var column in Columns) { Engine.State.Preferences.Positions.Remove(column.Definition.PositionKey); Engine.State.Preferences.Positions.Remove(ColumnDefaultPrefix + column.Definition.PositionKey); } RestorePositions(); }
        foreach (var column in Columns) { column.ApplySettings(); if (Expanded && column.Definition.Visible) column.Show(); else column.Hide(); }
        foreach (var editor in Editors) editor.RefreshColumns(); Engine.Touch(); Ball.RefreshData();
    }
    private void RestorePositions()
    {
        Ball.RestoreFromEdge();
        var area = WorkArea(Ball);
        var defaults = DefaultColumnPositions(); EnsureColumnDefaults(defaults);
        var visible = VisibleColumns;
        for (var i = 0; i < visible.Length; i++)
        {
            if (Engine.State.Preferences.Positions.TryGetValue(visible[i].Definition.PositionKey, out var position)) visible[i].Place(position);
            else visible[i].Place(Engine.State.Preferences.Positions[ColumnDefaultPrefix + visible[i].Definition.PositionKey]);
            Clamp(visible[i]);
        }
        if (Engine.State.Preferences.Positions.TryGetValue("ball", out var ball)) Ball.Place(ball);
        else Ball.Place(new WindowPosition { X = area.Right - 78, Y = area.Bottom - 76, Width = 58, Height = 58 });
        Clamp(Ball);
        Ball.RefreshEdgeAutoHide();
        if (Expanded) Ball.SetExpanded(true);
    }
    internal Rect MaximumRange { get { var columns = VisibleColumns; return columns.Length == 0 ? Rect.Empty : new Rect(columns.Min(x => x.Left),columns.Min(x => x.Top),columns.Max(x => x.Left+x.Width)-columns.Min(x => x.Left),columns.Max(x => x.Top+x.Height)-columns.Min(x => x.Top)); } }
    private ColumnSurface[] scaleWindows = [];
    private WindowPosition[]? scaleStart;
    private Rect scaleBounds;
    private double scaleDeltaX, scaleDeltaY;
    private System.Drawing.Point scalePointerStart;
    private double scalePointerDpi;
    private bool scalePreviousSuppression;
    public void BeginWorkspaceResize()
    { scalePreviousSuppression = SuppressCollapse; scaleWindows = VisibleColumns; if (scaleWindows.Length == 0) return; EnsureColumnDefaults(DefaultColumnPositions()); scaleDefaultStart = scaleWindows.Select(x => Copies.Of(Engine.State.Preferences.Positions[ColumnDefaultPrefix + x.Definition.PositionKey])).ToArray(); workspaceResizeFactor = 1; scaleStart = scaleWindows.Select(x => x.Position()).ToArray(); scaleBounds = MaximumRange; scaleDeltaX = scaleDeltaY = 0; scalePointerStart = Forms.Cursor.Position; scalePointerDpi = Native.GetDpiForWindow(new WindowInteropHelper(Ball).Handle) / 96.0; SuppressCollapse = true; }
    public void ResizeWorkspaceFromPointer()
    => ResizeWorkspaceAtPointer(Forms.Cursor.Position);
    internal void ResizeWorkspaceAtPointer(System.Drawing.Point point)
    { ResizeWorkspace((point.X - scalePointerStart.X) / scalePointerDpi - scaleDeltaX, (point.Y - scalePointerStart.Y) / scalePointerDpi - scaleDeltaY); }
    public void ResizeWorkspace(double deltaX, double deltaY)
    {
        if (scaleStart is null) return; scaleDeltaX += deltaX; scaleDeltaY += deltaY;
        var area = WorkArea(Ball);
        if (Smoke) area = new Rect(-10000, -10000, area.Width, area.Height);
        var requested = 1 + 2 * (-scaleDeltaX * scaleBounds.Width + scaleDeltaY * scaleBounds.Height) / (scaleBounds.Width * scaleBounds.Width + scaleBounds.Height * scaleBounds.Height);
        var minimum = Math.Max(80 / scaleStart.Min(x => x.Width), 260 / scaleStart.Min(x => x.Height));
        var maximum = Math.Max(minimum, Math.Min(area.Width / scaleBounds.Width, area.Height / scaleBounds.Height));
        var factor = Math.Clamp(requested, minimum, maximum);
        workspaceResizeFactor = factor;
        var left = area.Left + (area.Width - scaleBounds.Width * factor) / 2;
        var top = area.Top + (area.Height - scaleBounds.Height * factor) / 2;
        for (var i = 0; i < scaleWindows.Length; i++)
        {
            var position = scaleStart[i];
            var dpi = Math.Max(96, Native.GetDpiForWindow(new WindowInteropHelper(scaleWindows[i]).EnsureHandle())) / 96.0;
            double Pixel(double value) => Math.Round(value * dpi) / dpi;
            scaleWindows[i].Place(new WindowPosition { X = Pixel(left + (position.X - scaleBounds.Left) * factor), Y = Pixel(top + (position.Y - scaleBounds.Top) * factor), Width = Pixel(position.Width * factor), Height = Pixel(position.Height * factor) });
        }
    }
    public void EndWorkspaceResize()
    {
        if (scaleStart is null) return; scaleStart = null; SuppressCollapse = scalePreviousSuppression;
        var area = WorkArea(Ball); var defaults = Bounds(scaleDefaultStart);
        for (var i = 0; i < scaleWindows.Length; i++)
        {
            var column = scaleWindows[i]; Engine.State.Preferences.Positions[column.Definition.PositionKey] = column.Position();
            Engine.State.Preferences.Positions[ColumnDefaultPrefix + column.Definition.PositionKey] = ScaledDefault(scaleDefaultStart[i], defaults, area, workspaceResizeFactor);
        }
        Engine.Touch();
    }
    public void RestoreDefaults()
    {
        foreach (var key in Engine.State.Preferences.Positions.Keys.Where(x => x == "ball" || x.StartsWith(ColumnDefaultPrefix, StringComparison.Ordinal) || x.StartsWith("lane-", StringComparison.Ordinal) || x.StartsWith("column-",StringComparison.Ordinal)).ToArray()) Engine.State.Preferences.Positions.Remove(key);
        RestorePositions(); if (Expanded) { Ball.SetExpanded(false); Ball.SetExpanded(true); LayoutDrafts(); } Engine.Touch();
    }
    public void OrbMenu(FrameworkElement target)
    {
        ShowOrbMenu(Forms.Cursor.Position);
    }
    internal void ShowOrbMenu(System.Drawing.Point pointer)
    {
        if (Ball.IsEdgeDocked)
        {
            var bounds = Native.ScreenBounds(Ball);
            var gap = (int)Math.Ceiling(8 * Native.GetDpiForWindow(new WindowInteropHelper(Ball).Handle) / 96.0);
            EntryMenu.Show(new System.Drawing.Point((int)(bounds.Right - bounds.Height) - gap, pointer.Y), Forms.ToolStripDropDownDirection.Left);
        }
        else EntryMenu.Show(pointer);
    }
    internal Forms.ContextMenuStrip EntryMenu => tray.ContextMenuStrip!;
    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        var ball = menu.Items.Add(Engine.State.Preferences.BallVisible ? "隐藏悬浮球" : "恢复悬浮球", null, (_, _) => ToggleBallVisibility());
        menu.Items.Add("设置", null, (_, _) => ToggleSettings());
        menu.Items.Add("恢复窗口默认", null, (_, _) => RestoreDefaults());
        var undo = menu.Items.Add("撤销", null, (_, _) => Undo());
        var redo = menu.Items.Add("恢复", null, (_, _) => Redo());
        var ai = new Forms.ToolStripMenuItem("AI"); ai.DropDownItems.Add(new Forms.ToolStripMenuItem("尚未接入 AI") { Enabled = false }); menu.Items.Add(ai);
        menu.Items.Add("展开 / 收起看板", null, (_, _) => Toggle());
        menu.Items.Add("退出程序", null, async (_, _) => await Exit());
        menu.Opening += (_, _) => { Ball.SetEdgeMenuOpen(true); ai.DropDownItems.Clear(); ai.DropDownItems.Add(new Forms.ToolStripMenuItem("Codex · "+CodexStatus) { Enabled = false }); ai.DropDownItems.Add(new Forms.ToolStripMenuItem("dsh / workbuddy · 未接入") { Enabled = false }); ball.Text = Engine.State.Preferences.BallVisible ? "隐藏悬浮球" : "恢复悬浮球"; undo.Enabled = Engine.CanUndo; redo.Enabled = Engine.CanRedo; Ball.HidePopups(); };
        menu.Closed += (_, _) => Ball.SetEdgeMenuOpen(false);
        return menu;
    }
    private void HideBall() { Ball.HidePopups(); Ball.Hide(); Engine.State.Preferences.BallVisible = false; Engine.Touch(); }
    private void ToggleBallVisibility() { if (Engine.State.Preferences.BallVisible) HideBall(); else ShowBall(); }
    private void ShowBall() { Ball.Show(); Engine.State.Preferences.BallVisible = true; Engine.Touch(); }
    private async Task CheckReminders()
    {
        if (explicitSave > 0 || exiting || dataTransition) return;
        var due = ReminderClock.Tick(Engine.State, DateTime.Now); if (due.Count == 0) return;
        Engine.Notify(due);
        if (await Flush())
        {
            notified = due.ToArray();
            tray.ShowBalloonTip(4000, "任务提醒", $"{due.Count}项任务已进入待操作。点击查看。", Forms.ToolTipIcon.Info);
        }
    }
    private void ShowNotificationTasks()
    {
        if (notified.Length == 1) { Locate(notified[0]); return; }
        var chooser = new SurfaceWindow(340, 330); RegisterAuxiliary(chooser); var body = new StackPanel { Margin = new Thickness(12) };
        body.Children.Add(Ui.Button("关闭", chooser.Destroy));
        foreach (var id in notified)
        {
            var task = Engine.State.Tasks.FirstOrDefault(x => x.Id == id); if (task is null) continue;
            body.Children.Add(Ui.Button(task.DisplayTitle, () => { chooser.Destroy(); Locate(id); }));
        }
        chooser.Root.Children.Add(new ScrollViewer { Content = body }); var area = WorkArea(Ball); chooser.Left = area.Right - chooser.Width - 20; chooser.Top = area.Bottom - chooser.Height - 80; Clamp(chooser); chooser.Show();
    }
    public async Task Import(BoardState candidate)
    {
        if (!await Flush()) return;
        File.Copy(Store.MainPath, Store.MainPath + ".before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
        await saveGate.WaitAsync();
        try
        {
            await Store.SaveAsync(candidate);
            ResetEditors();
            DetachEngine(); Engine = new BoardEngine(candidate); AttachEngine(); dirtyVersion++; savedVersion = dirtyVersion;
            SynchronizeColumns(); settings?.Destroy(); settings = null; OnChanged(candidate.Tasks.Select(x => x.Id).ToArray()); ApplySettings(); RestorePositions();
            await Listening.Apply();
            if (Expanded) { foreach (var session in Engine.State.Sessions.Where(x => x.Visible && !x.Inline)) EnsureEditor(session).Show(); LayoutDrafts(); Ball.SetExpanded(false); Ball.SetExpanded(true); }
        }
        finally { saveGate.Release(); }
    }
    private void ResetEditors()
    {
        foreach (var editor in Editors.ToArray()) { if (editor.Session.TaskId is null) draftStack.Detach(editor); editor.Destroy(); }
        Editors.Clear(); foreach (var window in taskWindows.Values.ToArray()) window.Destroy(); taskWindows.Clear();
        draftOrder.Clear(); hiddenGroup.Clear(); previouslyActive = null; draftStack.Hide();
    }
    public async Task ImportConfiguration(ConfigurationBackup configuration)
    {
        if (dataTransition) throw new InvalidOperationException("正在切换或保存配置，请稍后重试");
        SetDataTransition(true);
        try
        {
        foreach (var editor in Editors) editor.PrepareHide();
        var candidate = configuration.Apply(Engine.State);
        await saveGate.WaitAsync();
        try
        {
            await Store.SaveAsync(candidate);
            Engine.State.Preferences = candidate.Preferences; Engine.State.AiOptions = candidate.AiOptions; Engine.State.PriorityOptions = candidate.PriorityOptions;
            SynchronizeColumns(); ColumnsChanged(false); SurfaceWindow.SetTransparency(candidate.Preferences.GlassTransparency, candidate.Preferences.FloatingTransparency); OptionsChanged(); ApplySettings(); RestorePositions();
            foreach (var editor in Editors) editor.RefreshOptionLabels();
            if (!Isolated) StartupLinks.SetAutoStart(candidate.Preferences.AutoStart);
        }
        finally { saveGate.Release(); }
        await Flush();
        await Listening.Apply();
        }
        finally { SetDataTransition(false); }
        if (!Isolated) await RestartCodex();
    }
    public async Task ChangeDataDirectory(string directory)
    {
        var target = Path.GetFullPath(directory);
        if (string.Equals(Path.TrimEndingDirectorySeparator(target), Path.TrimEndingDirectorySeparator(Store.DirectoryPath), StringComparison.OrdinalIgnoreCase)) return;
        if (dataTransition) throw new InvalidOperationException("正在切换或保存配置，请稍后重试");
        if (Isolated && !target.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("隔离测试只能切换到test-output内目录");
        var lease = DataPaths.Lock(target, out var acquired);
        if (!acquired) { lease.Dispose(); throw new InvalidOperationException("该数据目录已被另一个任务面板打开，请先退出另一个程序"); }
        bool retained = false;
        SetDataTransition(true);
        try
        {
            foreach (var column in Columns) column.PrepareHide(); foreach (var editor in Editors) editor.PrepareHide();
            if (!await Flush()) throw new IOException("原目录保存未完成，路径没有切换");
            var next = new AtomicStore(target, Store.ConfigurationDirectory);
            var existing = File.Exists(next.MainPath) || File.Exists(next.MainPath + ".bak");
            var candidate = existing ? ConfigurationBackup.Capture(Engine.State, target).Apply(next.Load()) : Copies.Of(Engine.State);
            await saveGate.WaitAsync();
            try
            {
                await next.SaveAsync(candidate); DataPaths.Save(pathSelectionFile, target, Store.ConfigurationDirectory);
                ResetEditors(); DetachEngine(); Engine = new BoardEngine(candidate); AttachEngine(); Store = next; dirtyVersion++; savedVersion = dirtyVersion;
                SynchronizeColumns(); ColumnsChanged(false); OnChanged(candidate.Tasks.Select(x => x.Id).ToArray()); ApplySettings(); SurfaceWindow.SetTransparency(candidate.Preferences.GlassTransparency, candidate.Preferences.FloatingTransparency); RestorePositions();
                if (Expanded) { foreach (var session in Engine.State.Sessions.Where(x => x.Visible && !x.Inline).ToArray()) EnsureEditor(session).Show(); LayoutDrafts(); }
                directoryLocks.Add(lease); retained = true;
            }
            finally { saveGate.Release(); }
        }
        finally { SetDataTransition(false); if (!retained) { lease.ReleaseMutex(); lease.Dispose(); } }
        await Listening.Apply();
        settings?.RefreshSection();
    }
    public void SetGlassTransparency(double value)
    { Engine.State.Preferences.GlassTransparency = Math.Clamp(value, 0, 100); SurfaceWindow.SetTransparency(value); Engine.Touch(); }
    public void SetBallTransparency(double value)
    { Engine.State.Preferences.BallTransparency = Math.Clamp(value, 0, 100); Ball.RefreshGlass(); Engine.Touch(); }
    public void SetFloatingTransparency(double value)
    { Engine.State.Preferences.FloatingTransparency = Math.Clamp(value, 0, 100); SurfaceWindow.SetTransparency(Engine.State.Preferences.GlassTransparency, Engine.State.Preferences.FloatingTransparency); Engine.Touch(); }
    private void Report(string message)
    {
        Ball?.SetValue(FrameworkElement.ToolTipProperty, message);
        if (Smoke) throw new InvalidOperationException(message);
        if (lastError == message) return; lastError = message; Ui.Error(message);
    }
    public async Task<bool> Exit()
    {
        if (exiting) return false;
        foreach (var column in Columns) column.PrepareHide();
        foreach (var editor in Editors) editor.PrepareHide();
        if (!await Flush()) return false;
        var restoreErrors = Listening.RestoreOriginals();
        if (restoreErrors.Length > 0) { Report(string.Join("；", restoreErrors)); return false; }
        exiting = true; await Codex.DisposeAsync(); Dispose(); Application.Current.Shutdown(); return true;
    }
    public void Dispose()
    {
        CardDrag?.Dispose(); CardDrag = null;
        globalHotkey?.Dispose(); globalHotkey = null;
        Listening.Dispose();
        Ui.WindowCreated = null;
        foreach (var lease in directoryLocks) { lease.ReleaseMutex(); lease.Dispose(); } directoryLocks.Clear();
        saveTimer.Stop(); reminderTimer.Stop(); DetachEngine();
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
        outsideClicks?.Dispose(); outsideClicks = null;
        _ = Codex.DisposeAsync(); tray.Visible = false; tray.Dispose(); trayIcon.Dispose();
        foreach (var editor in Editors.ToArray()) editor.Destroy(); Editors.Clear();
        foreach (var window in taskWindows.Values.ToArray()) window.Destroy(false); taskWindows.Clear();
        foreach (var window in auxiliary.ToArray()) window.Destroy(false); auxiliary.Clear(); draftStack.Destroy(false); foreach (var column in Columns) column.Destroy(); Ball.Destroy();
    }
    private delegate void ForegroundCallback(IntPtr hook, uint @event, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, ForegroundCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
}
