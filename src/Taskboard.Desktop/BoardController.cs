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

internal sealed class BoardController : IDisposable
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
    private int draftPage;
    internal Rect[] LastDraftBounds { get; private set; } = [];
    private SettingsSurface? settings;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    internal CodexObserver Codex { get; private set; } = new();
    internal NotificationListeningService Listening { get; }
    internal string CodexStatus => Codex.Status;
    private void UpdateCodexWatches() => Codex.Watch(Engine.State.Tasks.Where(x => x.Codex is not null).Select(x => new CodexWatch(x.Id,x.Codex!.ThreadId,x.Codex.Generation)).ToArray());
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
                var changed = Engine.State.Tasks.Where(x => x.Codex is { Phase: not CodexPhase.Unknown }).ToArray();
                foreach (var task in changed) task.Codex!.Phase = CodexPhase.Unknown;
                if (changed.Length > 0) Engine.Notify(changed.Select(x => x.Id).ToArray());
            }
            settings?.RefreshCodexStatus();
        }));
        observer.Event += (watch,message,sequence,epoch,authoritative) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (exiting || Codex != observer || observer.Epoch != epoch) return;
            var task = Engine.State.Tasks.FirstOrDefault(x => x.Id == watch.TaskId);
            if (task?.Codex?.ThreadId != watch.ThreadId) return;
            var before = task.Lane;
            if (CodexState.Apply(task,message,sequence,watch.Generation,epoch,authoritative))
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
        Isolated = isolated; Smoke = smoke; Store = new AtomicStore(dataDirectory);
        pathSelectionFile = Path.Combine(isolated ? Path.GetFullPath(dataDirectory) : AppContext.BaseDirectory, "launch-settings.json");
        Engine = new BoardEngine(Store.Load()); AttachEngine();
        Listening = new NotificationListeningService(Application.Current.Dispatcher, () => Engine.State.Preferences.Listening, isolated,
            isolated ? Path.Combine(dataDirectory, "notification-sound-restore.json") :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InstantTaskboard", "notification-sound-restore.json"));
        Listening.Changed += () => settings?.RefreshListeningStatus();
        SurfaceWindow.SetTransparency(Engine.State.Preferences.GlassTransparency);
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        saveTimer.Tick += async (_, _) => { saveTimer.Stop(); await Flush(); };
        foreach (var definition in Engine.State.Preferences.Columns.Where(x => !x.Deleted)) Columns.Add(new ColumnSurface(this, definition));
        Ball = new BallSurface(this);
        Native.SetBall(Ball);
        draftStack = new DraftStackSurface(this);
        RestorePositions();
        Ui.WindowCreated = window => { RegisterAuxiliary(window); Clamp(window); };
        trayIcon = Brand.TrayIcon(); tray = new Forms.NotifyIcon { Icon = trayIcon, Visible = true, Text = Brand.Name };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
        tray.BalloonTipClicked += (_, _) => { SetExpanded(true); ShowNotificationTasks(); };
        tray.ContextMenuStrip = BuildTrayMenu();
        reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        reminderTimer.Tick += async (_, _) => await CheckReminders(); reminderTimer.Start();
        foregroundCallback = (_, _, handle, _, _, _, _) =>
        {
            if (handle == IntPtr.Zero || exiting) return;
            GetWindowThreadProcessId(handle, out var process);
            if (process == Environment.ProcessId) { Application.Current.Dispatcher.BeginInvoke(new Action(Native.RaiseBall)); return; }
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Expanded && !SuppressCollapse && !Smoke) SetExpanded(false);
            }));
        };
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 0);
    }
    public async Task Start()
    {
        foreach(var column in VisibleColumns) column.PrimeWindow();
        if (Engine.State.Preferences.BallVisible) Ball.Show();
        if (!Isolated)
        {
            var machine = StartupLinks.MachineIdentity();
            var markers = Engine.State.Preferences.MachineMarkers;
            if (!markers.ContainsKey("shortcut:" + machine))
            {
                try
                {
                    StartupLinks.Shortcut();
                    markers["shortcut:" + machine] = Environment.ProcessPath ?? AppContext.BaseDirectory;
                    Engine.Touch(); await Flush();
                }
                catch (Exception e) { Report("首次桌面快捷方式创建失败：" + e.Message); }
            }
            if (!markers.ContainsKey("startup:" + machine))
            {
                try
                {
                    StartupLinks.SetAutoStart(Engine.State.Preferences.AutoStart);
                    markers["startup:" + machine] = Environment.ProcessPath ?? AppContext.BaseDirectory;
                    Engine.Touch(); await Flush();
                }
                catch (Exception e) { Report("首次开机启动设置失败：" + e.Message); }
            }
        }
        if (Store.RecoveryMessage is not null) Report(Store.RecoveryMessage);
        if (Engine.State.Sessions.Count > 0) Ball.ToolTip = $"已恢复{Engine.State.Sessions.Count}份草稿或未提交编辑，展开后继续。";
        await CheckReminders();
        foreach (var task in Engine.State.Tasks.Where(x => x.Codex is not null)) { task.Codex!.Phase = CodexPhase.Unknown; task.Codex.SourceEpoch = ""; }
        if (!Isolated) await RestartCodex();
        await Listening.Apply();
        foreach (var column in VisibleColumns) column.WarmAnimation();
    }
    private void AttachEngine() { Engine.Changed += OnChanged; Engine.Dirty += OnDirty; }
    private void DetachEngine() { Engine.Changed -= OnChanged; Engine.Dirty -= OnDirty; }
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
        await saveGate.WaitAsync(); explicitSave++;
        try
        {
            // Persist a validated proposed submission before altering official in-memory tasks.
            var frozen = Copies.Of(session);
            var candidate = Copies.Of(Engine.State);
            var proposal = new BoardEngine(candidate);
            var proposedSession = candidate.Sessions.FirstOrDefault(x => x.Id == session.Id) ?? frozen;
            proposedSession.Working = Copies.Of(frozen.Working);
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
        if (!expanded)
        {
            hiddenDialogs.Clear(); hiddenDialogs.AddRange(Native.Dialogs()); foreach (var dialog in hiddenDialogs) Native.ShowWindow(dialog, 0);
            hiddenGroup.Clear(); hiddenGroup.AddRange(taskWindows.Values.Cast<SurfaceWindow>().Append(draftStack).Concat(auxiliary).Where(x => x.IsVisible).OrderBy(x => x.ActivationOrder));
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
        } finally {Native.EndWorkspaceChange();}
    }
    public EditorSurface NewTask(Lane lane, Guid? columnId = null)
    {
        SetExpanded(true); var session = Engine.NewSession(lane); session.Working.ColumnId = columnId; session.Original.ColumnId = columnId;
        var editor = EnsureEditor(session); PromoteDraft(editor); editor.Show(); editor.Activate(); Native.RaiseBall(); return editor;
    }
    public EditorSurface OpenNewEntry(Lane lane, Guid? columnId = null)
    {
        var saved = Engine.State.Sessions.Where(x => x.TaskId is null && !x.Inline && !x.Visible && x.Working.Lane == lane && x.Working.ColumnId == columnId).ToArray();
        if (saved.Length == 0) return NewTask(lane, columnId);
        SetExpanded(true);
        foreach (var session in saved) { session.Visible = true; EnsureEditor(session); }
        var front = EnsureEditor(saved[^1]); PromoteDraft(front); front.Activate(); Engine.Touch(); return front;
    }
    public EditorSurface EditTask(Guid id)
    {
        SetExpanded(true); var session = Engine.Edit(id); var editor = EnsureEditor(session);
        editor.Show(); editor.Activate(); Native.RaiseBall(); return editor;
    }
    private EditorSurface EnsureEditor(EditSession session)
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
        if (Engine.State.Preferences.Positions.TryGetValue(key, out var position)) window.Place(position);
        else
        {
            var area = WorkArea(Ball); window.Left = area.Left + Math.Max(30, (area.Width - window.Width) / 2) + (Editors.Count(x => x.Session.TaskId is not null) - 1) * 26;
            window.Top = area.Top + Math.Max(12, (area.Height - window.Height) / 2);
        }
        Clamp(window); return editor;
    }
    public void RemoveEditor(EditorSurface editor)
    {
        Editors.Remove(editor); draftOrder.Remove(editor.Session.Id);
        if (taskWindows.Remove(editor.Session.Id, out var window)) { hiddenGroup.Remove(window); if (previouslyActive == window) previouslyActive = null; window.Destroy(); }
        else draftStack.Detach(editor);
        Engine.State.Preferences.Positions.Remove("edit-" + editor.Session.Id); Engine.Touch();
        LayoutDrafts();
    }
    private void PromoteDraft(EditorSurface editor)
    { draftOrder.Remove(editor.Session.Id); draftOrder.Insert(0, editor.Session.Id); draftPage = 0; LayoutDrafts(); }
    public void ShowEditor(EditorSurface editor, bool activate)
    {
        if (!Expanded) SetExpanded(true);
        if (editor.Session.TaskId is null)
        { if (activate && ActiveDraft != editor.Session.Id) PromoteDraft(editor); else LayoutDrafts(); if (activate) draftStack.Activate(); }
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
            if (Engine.State.Preferences.Positions.TryGetValue("draft-stack", out var placement))
            { left = Math.Clamp(placement.X, area.Left, area.Right - width * 1.2); top = Math.Clamp(placement.Y, area.Top, area.Bottom - height); }
            var slots = Math.Max(1, (int)Math.Floor((area.Right - 12 - left - width) / step));
            var olderPerPage = Math.Max(0, slots - 1);
            var totalPages = olderPerPage > 0 ? Math.Max(1, (int)Math.Ceiling((drafts.Length - 1.0) / olderPerPage)) : Math.Max(1, drafts.Length);
            draftPage = Math.Clamp(draftPage, 0, totalPages - 1);
            var visible = olderPerPage > 0 ? new[] { drafts[0] }.Concat(drafts.Skip(1 + draftPage * olderPerPage).Take(olderPerPage)).ToArray() : new[] { drafts[draftPage] };
            LastDraftBounds = visible.Select((_, i) => new Rect(left + i * step, top, width, height)).Append(new Rect(left + visible.Length * step, top, width, height)).ToArray();
            draftStack.Layout(visible, LastDraftBounds, draftPage, totalPages);
            Native.RaiseBall();
        }
        finally { layingOutDrafts = false; }
    }
    private void RefreshDraftStack() { if (draftStack is not null && !Editors.Any(x => x.Session.TaskId is null)) draftStack.ClearClosed(); }
    public void RegisterAuxiliary(SurfaceWindow window)
    { auxiliary.Add(window); window.Closed += (_, _) => { auxiliary.Remove(window); hiddenGroup.Remove(window); if (previouslyActive == window) previouslyActive = null; }; }
    public void MoveTask(Guid id, Lane lane, Guid? before, Guid? columnId = null) { Engine.Move(id, lane, before, columnId); Persist(); }
    public void DeleteTask(Guid id) { Engine.Delete(id); Persist(); }
    public void Undo() { Engine.Undo(); Persist(); }
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
    public void ApplySettings() { foreach (var column in Columns) column.ApplySettings(); }
    public void SavePosition(string key, SurfaceWindow window) { Engine.State.Preferences.Positions[key] = window.Position(); Engine.Touch(); }
    public void SaveBall(BallSurface window)
    {
        Engine.State.Preferences.Positions["ball"] = new WindowPosition { X = window.Left + window.Width - 58, Y = window.Top, Width = 58, Height = 58 }; Engine.Touch();
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
        if (Smoke) { window.Left = -10000; window.Top = -10000; window.ShowActivated = false; return; }
        var area = WorkArea(window);
        window.Width = Math.Min(window.Width, area.Width); window.Height = Math.Min(window.Height, area.Height);
        window.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - window.Width));
        window.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - window.Height));
    }
    private void SynchronizeColumns()
    {
        foreach (var column in Columns.ToArray())
        { var definition = Engine.State.Preferences.Columns.FirstOrDefault(x => x.Id == column.Definition.Id && !x.Deleted); if (definition is null) { column.PrepareHide(); column.Destroy(); Columns.Remove(column); } else column.Rebind(definition); }
        foreach (var definition in Engine.State.Preferences.Columns.Where(x => !x.Deleted && !Columns.Any(y => y.Definition.Id == x.Id))) Columns.Add(new ColumnSurface(this,definition));
        Columns.Sort((a,b) => Engine.State.Preferences.Columns.IndexOf(a.Definition).CompareTo(Engine.State.Preferences.Columns.IndexOf(b.Definition)));
    }
    public void ColumnsChanged(bool layout = true)
    {
        SynchronizeColumns(); if (layout) { foreach (var column in Columns) Engine.State.Preferences.Positions.Remove(column.Definition.PositionKey); RestorePositions(); }
        foreach (var column in Columns) { column.ApplySettings(); if (Expanded && column.Definition.Visible) column.Show(); else column.Hide(); }
        foreach (var editor in Editors) editor.RefreshColumns(); Engine.Touch(); Ball.RefreshData();
    }
    private void RestorePositions()
    {
        var area = WorkArea(Ball);
        var monitor = Forms.Screen.FromHandle(new WindowInteropHelper(Ball).Handle).Bounds;
        var scale = 96.0 / Math.Max(96, Native.GetDpiForWindow(new WindowInteropHelper(Ball).Handle));
        var gap = Math.Min(24, Math.Max(12, area.Width / 50));
        var totalWidth = Math.Min(area.Width - 24, monitor.Width * scale * 7 / 8);
        var visible = VisibleColumns; var count = Math.Max(1,visible.Length);
        gap = Math.Min(gap, Math.Max(0,(totalWidth-count*80)/(count-1.0)));
        if (count == 1) gap = 0;
        var width = Math.Max(80, (totalWidth - (count-1)*gap) / count);
        var height = Math.Max(260, (area.Height - 90) * .87);
        var left = area.Left + (area.Width - width * count - (count-1) * gap) / 2;
        var top = area.Top + (area.Height - height) / 2;
        for (var i = 0; i < visible.Length; i++)
        {
            if (Engine.State.Preferences.Positions.TryGetValue(visible[i].Definition.PositionKey, out var position)) visible[i].Place(position);
            else visible[i].Place(new WindowPosition { X = left + i * (width + gap), Y = top, Width = width, Height = height });
            Clamp(visible[i]);
        }
        if (Engine.State.Preferences.Positions.TryGetValue("ball", out var ball)) Ball.Place(ball);
        else Ball.Place(new WindowPosition { X = area.Right - 78, Y = area.Bottom - 76, Width = 58, Height = 58 });
        Clamp(Ball);
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
    { scalePreviousSuppression = SuppressCollapse; scaleWindows = VisibleColumns; if (scaleWindows.Length == 0) return; scaleStart = scaleWindows.Select(x => x.Position()).ToArray(); scaleBounds = MaximumRange; scaleDeltaX = scaleDeltaY = 0; scalePointerStart = Forms.Cursor.Position; scalePointerDpi = Native.GetDpiForWindow(new WindowInteropHelper(Ball).Handle) / 96.0; SuppressCollapse = true; }
    public void ResizeWorkspaceFromPointer()
    => ResizeWorkspaceAtPointer(Forms.Cursor.Position);
    internal void ResizeWorkspaceAtPointer(System.Drawing.Point point)
    { ResizeWorkspace((point.X - scalePointerStart.X) / scalePointerDpi - scaleDeltaX, (point.Y - scalePointerStart.Y) / scalePointerDpi - scaleDeltaY); }
    public void ResizeWorkspace(double deltaX, double deltaY)
    {
        if (scaleStart is null) return; scaleDeltaX += deltaX; scaleDeltaY += deltaY;
        var area = WorkArea(Ball);
        if (Smoke) area = new Rect(-10000, -10000, area.Width, area.Height);
        var requested = 1 + (-scaleDeltaX * scaleBounds.Width + scaleDeltaY * scaleBounds.Height) / (scaleBounds.Width * scaleBounds.Width + scaleBounds.Height * scaleBounds.Height);
        var minimum = Math.Max(80 / scaleStart.Min(x => x.Width), 260 / scaleStart.Min(x => x.Height));
        var maximum = Math.Max(minimum, Math.Min(area.Width / scaleBounds.Width, area.Height / scaleBounds.Height));
        var factor = Math.Clamp(requested, minimum, maximum);
        var left = Math.Clamp(scaleBounds.Right - scaleBounds.Width * factor, area.Left, Math.Max(area.Left, area.Right - scaleBounds.Width * factor));
        var top = Math.Clamp(scaleBounds.Top, area.Top, Math.Max(area.Top, area.Bottom - scaleBounds.Height * factor));
        for (var i = 0; i < scaleWindows.Length; i++)
        {
            var position = scaleStart[i];
            var dpi = Native.GetDpiForWindow(new WindowInteropHelper(scaleWindows[i]).Handle) / 96.0;
            double Pixel(double value) => Math.Round(value * dpi) / dpi;
            scaleWindows[i].Place(new WindowPosition { X = Pixel(left + (position.X - scaleBounds.Left) * factor), Y = Pixel(top + (position.Y - scaleBounds.Top) * factor), Width = Pixel(position.Width * factor), Height = Pixel(position.Height * factor) });
        }
    }
    public void EndWorkspaceResize()
    {
        if (scaleStart is null) return; scaleStart = null; SuppressCollapse = scalePreviousSuppression;
        foreach (var column in scaleWindows) Engine.State.Preferences.Positions[column.Definition.PositionKey] = column.Position(); Engine.Touch();
    }
    public void RestoreDefaults()
    {
        foreach (var key in Engine.State.Preferences.Positions.Keys.Where(x => x == "ball" || (x.StartsWith("lane-", StringComparison.Ordinal) || x.StartsWith("column-",StringComparison.Ordinal))).ToArray()) Engine.State.Preferences.Positions.Remove(key);
        RestorePositions(); if (Expanded) { Ball.SetExpanded(false); Ball.SetExpanded(true); LayoutDrafts(); } Engine.Touch();
    }
    public void OrbMenu(FrameworkElement target)
    {
        var menu = new ContextMenu();
        Add("设置", Settings); Add("隐藏悬浮球", HideBall); Add("恢复窗口默认位置", RestoreDefaults);
        var undo = Add("撤销", Undo); undo.IsEnabled = Engine.CanUndo;
        var ai = new MenuItem { Header = "AI" }; ai.Items.Add(new MenuItem { Header = "Codex · "+CodexStatus, IsEnabled = false }); ai.Items.Add(new MenuItem { Header = "dsh / workbuddy · 未接入", IsEnabled = false }); menu.Items.Add(ai);
        menu.PlacementTarget = target; menu.IsOpen = true;
        MenuItem Add(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); return item; }
    }
    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("设置", null, (_, _) => Settings());
        var ball = menu.Items.Add("隐藏悬浮球", null, (_, _) => { if (Engine.State.Preferences.BallVisible) HideBall(); else ShowBall(); });
        menu.Items.Add("恢复窗口默认位置", null, (_, _) => RestoreDefaults());
        var undo = menu.Items.Add("撤销", null, (_, _) => Undo());
        var ai = new Forms.ToolStripMenuItem("AI"); ai.DropDownItems.Add(new Forms.ToolStripMenuItem("尚未接入 AI") { Enabled = false }); menu.Items.Add(ai);
        menu.Items.Add("展开 / 收起看板", null, (_, _) => Toggle());
        menu.Items.Add("退出程序", null, async (_, _) => await Exit());
        menu.Opening += (_, _) => { ai.DropDownItems.Clear(); ai.DropDownItems.Add(new Forms.ToolStripMenuItem("Codex · "+CodexStatus) { Enabled = false }); ai.DropDownItems.Add(new Forms.ToolStripMenuItem("dsh / workbuddy · 未接入") { Enabled = false }); ball.Text = Engine.State.Preferences.BallVisible ? "隐藏悬浮球" : "恢复悬浮球"; undo.Enabled = Engine.CanUndo; Ball.HidePopups(); };
        return menu;
    }
    private void HideBall() { Ball.HidePopups(); Ball.Hide(); Engine.State.Preferences.BallVisible = false; Engine.Touch(); }
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
            SynchronizeColumns(); ColumnsChanged(false); SetGlassTransparency(candidate.Preferences.GlassTransparency); OptionsChanged(); ApplySettings(); RestorePositions();
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
            var next = new AtomicStore(target);
            var existing = File.Exists(next.MainPath) || File.Exists(next.MainPath + ".bak");
            var candidate = existing ? ConfigurationBackup.Capture(Engine.State, target).Apply(next.Load()) : Copies.Of(Engine.State);
            await saveGate.WaitAsync();
            try
            {
                await next.SaveAsync(candidate); DataPaths.Save(pathSelectionFile, target);
                ResetEditors(); DetachEngine(); Engine = new BoardEngine(candidate); AttachEngine(); Store = next; dirtyVersion++; savedVersion = dirtyVersion;
                SynchronizeColumns(); ColumnsChanged(false); OnChanged(candidate.Tasks.Select(x => x.Id).ToArray()); ApplySettings(); SurfaceWindow.SetTransparency(candidate.Preferences.GlassTransparency); RestorePositions();
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
        Listening.Dispose();
        Ui.WindowCreated = null;
        foreach (var lease in directoryLocks) { lease.ReleaseMutex(); lease.Dispose(); } directoryLocks.Clear();
        saveTimer.Stop(); reminderTimer.Stop(); DetachEngine();
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
        _ = Codex.DisposeAsync(); tray.Visible = false; tray.Dispose(); trayIcon.Dispose();
        foreach (var editor in Editors.ToArray()) editor.Destroy(); Editors.Clear();
        foreach (var window in taskWindows.Values.ToArray()) window.Destroy(); taskWindows.Clear();
        foreach (var window in auxiliary.ToArray()) window.Destroy(); auxiliary.Clear(); draftStack.Destroy(); foreach (var column in Columns) column.Destroy(); Ball.Destroy();
    }
    private delegate void ForegroundCallback(IntPtr hook, uint @event, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, ForegroundCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
}
