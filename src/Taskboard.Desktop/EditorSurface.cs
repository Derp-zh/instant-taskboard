using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class EditorSurface : Grid
{
    public Grid Root => this;
    public SurfaceWindow? Host { get; internal set; }
    public bool IsActive => Host?.IsActive == true && (Session.TaskId is not null || controller.ActiveDraft == Session.Id);
    private readonly BoardController controller;
    public EditSession Session { get; }
    private readonly StackPanel stages = new();
    private readonly TextBlock status;
    private readonly Button aiButton;
    private readonly Button priorityButton;
    private readonly OptionMenuToggle aiMenu;
    private readonly OptionMenuToggle priorityMenu;
    private readonly DispatcherTimer autosave;
    private bool submitting;
    private bool closing;
    public EditorSurface(BoardController controller, EditSession session)
    {
        this.controller = controller; Session = session;
        autosave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
        Root.RowDefinitions.Add(new RowDefinition()); Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeAll, Margin = new Thickness(14, 4, 10, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Ui.Label(session.TaskId is null ? "新建任务 · 自动暂存" : "编辑任务 · 保存后生效", 13));
        var close = Ui.Button("×", CloseEditor); close.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(close, 1); header.Children.Add(close); Root.Children.Add(header);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (Host is null || e.ButtonState != System.Windows.Input.MouseButtonState.Pressed || SurfaceWindow.IsInteractive(e.OriginalSource as DependencyObject)) return;
            Host.DragMove(); controller.EditorMoved(this); e.Handled = true;
        };
        var body = new StackPanel { Margin = new Thickness(16, 0, 16, 0) };
        body.Children.Add(Ui.Label("任务名称", 12, Ui.Muted));
        var title = Ui.Input(session.Working.Title); title.TextChanged += (_, _) => { session.Working.Title = title.Text; Changed(); }; body.Children.Add(title);
        body.Children.Add(Ui.Label("任务详情", 12, Ui.Muted)); body.Children.Add(stages); RenderStages();
        var newStage = Ui.Button("＋ 新阶段", () => { controller.Engine.AddStage(session); RenderStages(); Changed(); });
        newStage.HorizontalAlignment = HorizontalAlignment.Right; body.Children.Add(newStage);
        var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        for (var i = 0; i < 3; i++) row.ColumnDefinitions.Add(new ColumnDefinition());
        var statePanel = new StackPanel(); statePanel.Children.Add(Ui.Label("当前状态", 12, Ui.Muted));
        var lane = laneSelector = new ComboBox { ItemsSource = controller.Engine.State.Preferences.Columns.Where(x => !x.Deleted).ToArray(), SelectedItem = BoardColumns.For(controller.Engine.State.Preferences,session.Working), Margin = new Thickness(0, 3, 5, 0), MinHeight = 32 };
        lane.SelectionChanged += (_, _) => { if (lane.SelectedItem is BoardColumn definition) { definition.Assign(session.Working); Changed(); } };
        statePanel.Children.Add(lane); row.Children.Add(statePanel);
        var aiPanel = new StackPanel(); aiPanel.Children.Add(Ui.Label("AI（多选）", 12, Ui.Muted));
        aiButton = Ui.Button("选择", () => ShowOptions(true)); aiPanel.Children.Add(aiButton); Grid.SetColumn(aiPanel, 1); row.Children.Add(aiPanel);
        var priorityPanel = new StackPanel(); priorityPanel.Children.Add(Ui.Label("优先级", 12, Ui.Muted));
        priorityButton = Ui.Button("选择", () => ShowOptions(false)); priorityPanel.Children.Add(priorityButton); Grid.SetColumn(priorityPanel, 2); row.Children.Add(priorityPanel);
        aiMenu = new OptionMenuToggle(aiButton); priorityMenu = new OptionMenuToggle(priorityButton);
        body.Children.Add(row); body.Children.Add(optionManager); RefreshOptionLabels();
        var codexPanel = new StackPanel(); codexPanel.Children.Add(Ui.Label("Codex会话关联",12,Ui.Muted));
        var threadId = Ui.Input(session.Working.Codex?.ThreadId ?? ""); threadId.ToolTip = "准确会话编号；空白表示不关联";
        var bindingNote = Ui.Label(session.Working.Codex?.Caption ?? "未关联",11,Ui.Muted);
        var chats = new ComboBox { MinHeight = 32, MaxDropDownHeight = 240, DisplayMemberPath = nameof(CodexThread.Caption), Visibility = Visibility.Collapsed };
        chats.SelectionChanged += (_,_) => { if (chats.SelectedItem is CodexThread selected) threadId.Text = selected.Id; };
        var browse = Ui.Button("选择现有聊天",async () =>
        {
            try { var values = await controller.Codex.Catalog(); chats.ItemsSource = values; chats.Visibility = values.Length>0 ? Visibility.Visible : Visibility.Collapsed; bindingNote.Text = values.Length>0 ? controller.CodexStatus : "暂未读到聊天，可粘贴准确会话编号；实时状态待确认。"; }
            catch { bindingNote.Text = "读取中断；实时状态待确认，可稍后重试。"; }
        });
        threadId.TextChanged += (_,_) =>
        {
            var id = threadId.Text.Trim(); if (id.Length > 0 && !Guid.TryParse(id,out _)) { bindingNote.Text = "请输入完整会话编号，暂未更改关联。"; return; }
            if (id != session.Working.Codex?.ThreadId) session.Working.Codex = id.Length == 0 ? null : new CodexBinding { ThreadId = id };
            bindingNote.Text = id.Length>0 ? "保存后关联 · "+controller.CodexStatus : "未关联"; Changed();
        };
        codexPanel.Children.Add(threadId); codexPanel.Children.Add(browse); codexPanel.Children.Add(chats); codexPanel.Children.Add(bindingNote); body.Children.Add(codexPanel);
        var reminderEnabled = new CheckBox { Content = "定时提醒", IsChecked = session.Working.Reminder.Enabled, Foreground = Ui.Text, Margin = new Thickness(0, 4, 0, 8) };
        var reminderPanel = new StackPanel { Visibility = reminderEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed };
        var repeat = new ComboBox { ItemsSource = new[] { "一次", "每天", "每周" }, SelectedIndex = (int)session.Working.Reminder.Repeat, MinHeight = 30, Margin = new Thickness(0, 0, 0, 6) };
        var weekdays = new ComboBox { ItemsSource = new[] { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" },
            SelectedIndex = (int)session.Working.Reminder.Weekday, MinHeight = 30, Margin = new Thickness(0, 0, 0, 6) };
        var time = Ui.Input(session.Working.Reminder.Time.ToString(@"hh\:mm")); time.ToolTip = "时间，如09:00";
        var date = new DatePicker { SelectedDate = session.Working.Reminder.NextLocal.Date, Margin = new Thickness(0, 0, 0, 8) };
        date.CalendarStyle = (Style)FindResource(typeof(System.Windows.Controls.Calendar));
        var error = Ui.Label("", 11, Ui.Brush("#F28F98"));
        void Schedule()
        {
            var r = session.Working.Reminder;
            r.Enabled = reminderEnabled.IsChecked == true; reminderPanel.Visibility = r.Enabled ? Visibility.Visible : Visibility.Collapsed;
            date.Visibility = repeat.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            weekdays.Visibility = repeat.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (!TimeSpan.TryParseExact(time.Text, @"h\:mm", CultureInfo.InvariantCulture, out var parsed) &&
                !TimeSpan.TryParseExact(time.Text, @"hh\:mm", CultureInfo.InvariantCulture, out parsed)) { error.Text = "请输入有效时间，如09:00"; return; }
            if (parsed < TimeSpan.Zero || parsed >= TimeSpan.FromDays(1)) { error.Text = "时间应在00:00至23:59之间"; return; }
            error.Text = ""; r.Time = parsed; r.Repeat = (Repeat)Math.Max(0, repeat.SelectedIndex); r.Weekday = (DayOfWeek)Math.Max(0, weekdays.SelectedIndex);
            r.NextLocal = r.Repeat == Repeat.Once ? (date.SelectedDate ?? DateTime.Today).Date.Add(parsed) : ReminderClock.Next(r, DateTime.Now);
            Changed();
        }
        reminderEnabled.Checked += (_, _) => Schedule(); reminderEnabled.Unchecked += (_, _) => Schedule();
        repeat.SelectionChanged += (_, _) => Schedule(); weekdays.SelectionChanged += (_, _) => Schedule();
        time.TextChanged += (_, _) => Schedule(); date.SelectedDateChanged += (_, _) => Schedule();
        date.Visibility = repeat.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        weekdays.Visibility = repeat.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        reminderPanel.Children.Add(repeat); reminderPanel.Children.Add(weekdays); reminderPanel.Children.Add(Ui.Label("时间（24小时制）", 11, Ui.Muted));
        reminderPanel.Children.Add(time); reminderPanel.Children.Add(date); reminderPanel.Children.Add(error);
        body.Children.Add(reminderEnabled); body.Children.Add(reminderPanel);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); Root.Children.Add(scroll);
        var bottom = new Grid { Margin = new Thickness(12, 6, 12, 12) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status = Ui.Label(session.TaskId is null ? "未保存到任务列表" : "修改尚未提交", 11, Ui.Muted); bottom.Children.Add(status);
        var save = Ui.Button("保存任务", async () =>
        {
            if (submitting) return;
            if (threadId.Text.Trim().Length > 0 && !Guid.TryParse(threadId.Text.Trim(),out _)) { bindingNote.Text = "请输入完整会话编号，或清空关联后保存。"; return; }
            if (reminderEnabled.IsChecked == true && error.Text.Length > 0) { Ui.Error(error.Text); return; }
            submitting = true;
            Root.IsEnabled = false;
            try
            {
                autosave.Stop(); controller.Engine.StageSession(Session);
                if (await controller.SubmitSession(Session)) { controller.RemoveEditor(this); Destroy(); }
            }
            finally { submitting = false; Root.IsEnabled = true; }
        }, true);
        Grid.SetColumn(save, 1); bottom.Children.Add(save); Grid.SetRow(bottom, 2); Root.Children.Add(bottom);
        autosave.Tick += async (_, _) => { autosave.Stop(); if (await controller.Flush()) status.Text = Session.TaskId is null ? "草稿已暂存" : "修改已暂存，正式任务仍保持原值"; };
    }
    private void Changed()
    {
        if (status is null) return;
        controller.Engine.StageSession(Session); status.Text = Session.TaskId is null ? "正在暂存…" : "修改尚未提交";
        autosave.Stop(); autosave.Start();
    }
    private void RenderStages()
    {
        stages.Children.Clear();
        foreach (var stage in Session.Working.Stages)
        {
            var panel = new StackPanel();
            var heading = new DockPanel { Margin = new Thickness(7, 5, 7, 0) };
            var current = Ui.Button(stage.Id == Session.Working.CurrentStageId ? "当前阶段" : "设为当前", () =>
            { Session.Working.CurrentStageId = stage.Id; Changed(); RenderStages(); });
            DockPanel.SetDock(current, Dock.Right); heading.Children.Add(current);
            var name = Ui.Input(stage.Name); name.MinHeight = 28; name.MaxWidth = 155; name.Padding = new Thickness(9, 4, 9, 4);
            name.Margin = new Thickness(0, 3, 3, 3); name.HorizontalAlignment = HorizontalAlignment.Left; name.Foreground = Ui.Accent;
            name.ToolTip = "阶段名称"; name.TextChanged += (_, _) => { stage.Name = name.Text; Changed(); }; heading.Children.Add(name);
            if (Session.Working.Stages.Count > 1) panel.Children.Add(heading);
            var text = Ui.Input(stage.Text, true); text.Style = (Style)FindResource("StageBodyInput"); text.Background = Brushes.Transparent;
            text.BorderThickness = new Thickness(0); text.Margin = new Thickness(0); text.Padding = new Thickness(10, 5, 10, 10);
            text.MaxHeight = 180; text.ToolTip = "阶段详情"; text.TextChanged += (_, _) => { stage.Text = text.Text; Changed(); }; panel.Children.Add(text);
            var frame = Ui.Panel(panel, Ui.Brush("#A0121D2D"), 0); frame.CornerRadius = new CornerRadius(20);
            panel.IsKeyboardFocusWithinChanged += (_, _) => frame.BorderBrush = panel.IsKeyboardFocusWithin ? Ui.Accent : Ui.Line;
            stages.Children.Add(frame);
        }
    }
    public void RefreshOptionLabels()
    {
        var selected = controller.Engine.State.AiOptions.Where(x => Session.Working.AiIds.Contains(x.Id)).Select(x => x.Name).ToArray();
        var caption = selected.Length == 0 ? "未选择 ▾" : selected[0] + (selected.Length > 1 ? " +" + (selected.Length - 1) : "");
        aiButton.Content = new TextBlock { Text = caption, TextTrimming = TextTrimming.CharacterEllipsis };
        aiButton.ToolTip = selected.Length == 0 ? "未选择AI" : string.Join("、", selected);
        var p = controller.Engine.State.PriorityOptions.FirstOrDefault(x => x.Id == Session.Working.PriorityId);
        priorityButton.Content = new TextBlock { Text = p?.Name ?? "未选择 ▾", TextTrimming = TextTrimming.CharacterEllipsis };
        priorityButton.ToolTip = p?.Name ?? "未选择优先级";
        priorityButton.Foreground = p is null ? Ui.Text : Ui.Brush(p.Color);
    }
    private void ShowOptions(bool ai)
    {
        var toggle = ai ? aiMenu : priorityMenu;
        if (toggle.CloseOrConsume()) return;
        (ai ? priorityMenu : aiMenu).Close();
        var button = ai ? aiButton : priorityButton; var menu = new ContextMenu();
        var clear = new MenuItem { Header = "清空选择" };
        clear.Click += (_, _) => { if (ai) Session.Working.AiIds.Clear(); else Session.Working.PriorityId = null; Changed(); RefreshOptionLabels(); }; menu.Items.Add(clear);
        foreach (var option in ai ? controller.Engine.State.AiOptions : controller.Engine.State.PriorityOptions)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var select = new CheckBox { Content = option.Name, Foreground = Ui.Brush(option.Color), VerticalAlignment = VerticalAlignment.Center,
                IsChecked = ai ? Session.Working.AiIds.Contains(option.Id) : Session.Working.PriorityId == option.Id, MinWidth = 140 };
            select.Click += (_, _) =>
            {
                if (ai) { if (select.IsChecked == true) { if (!Session.Working.AiIds.Contains(option.Id)) Session.Working.AiIds.Add(option.Id); } else Session.Working.AiIds.Remove(option.Id); }
                else { Session.Working.PriorityId = select.IsChecked == true ? option.Id : null; menu.IsOpen = false; }
                Changed(); RefreshOptionLabels();
            };
            row.Children.Add(select);
            var color = Ui.Button("●", () => controller.ColorMenu(option, button)); color.Foreground = Ui.Brush(option.Color); row.Children.Add(color);
            menu.Items.Add(new MenuItem { Header = row, StaysOpenOnClick = true });
        }
        menu.Items.Add(UiMenu("＋ 管理选项", () => ShowOptionManager(ai)));
        toggle.Open(menu);
    }
    private static MenuItem UiMenu(string text, Action click) { var item = new MenuItem { Header = text }; item.Click += (_, _) => click(); return item; }
    private async void CloseEditor()
    {
        if (closing || submitting) return;
        closing = true; Root.IsEnabled = false;
        try
        {
            autosave.Stop(); controller.Engine.Discard(Session);
            if (!await controller.Flush()) { Session.Visible = true; controller.Engine.StageSession(Session); return; }
            controller.RemoveEditor(this); Destroy();
        }
        finally { closing = false; Root.IsEnabled = true; }
    }
    public void PrepareHide() { aiMenu.Close(); priorityMenu.Close(); autosave.Stop(); controller.Engine.StageSession(Session); }
    public void Show() => controller.ShowEditor(this, false);
    public void Activate() => controller.ShowEditor(this, true);
    public void Destroy() { aiMenu.Dispose(); priorityMenu.Dispose(); autosave.Stop(); Host = null; }
    private void ShowOptionManager(bool ai)
    {
        optionManager.Children.Clear(); optionManager.Visibility = Visibility.Visible;
        var heading = new DockPanel(); var close = Ui.Button("×", () => optionManager.Visibility = Visibility.Collapsed); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        heading.Children.Add(Ui.Label(ai ? "AI选项" : "优先级选项", 14)); optionManager.Children.Add(heading);
        foreach (var option in ai ? controller.Engine.State.AiOptions : controller.Engine.State.PriorityOptions)
        {
            var row = new DockPanel(); var remove = Ui.Button("删除", () => { controller.DeleteOption(option.Id, ai); ShowOptionManager(ai); }); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var color = Ui.Button("●", () => controller.ColorMenu(option, optionManager)); color.Foreground = Ui.Brush(option.Color); DockPanel.SetDock(color, Dock.Right); row.Children.Add(color);
            var name = Ui.Input(option.Name); name.LostKeyboardFocus += (_, _) => { if (string.IsNullOrWhiteSpace(name.Text)) { name.Text = option.Name; return; } option.Name = name.Text.Trim(); controller.OptionsChanged(); }; row.Children.Add(name); optionManager.Children.Add(row);
        }
        optionManager.Children.Add(Ui.Button("＋ 新增选项", () => { var values = ai ? controller.Engine.State.AiOptions : controller.Engine.State.PriorityOptions; values.Add(new FieldOption { Name = "新选项" }); controller.OptionsChanged(); ShowOptionManager(ai); }));
    }
    private readonly ComboBox laneSelector;
    public void RefreshColumns() { var selected = BoardColumns.For(controller.Engine.State.Preferences,Session.Working); laneSelector.ItemsSource = controller.Engine.State.Preferences.Columns.Where(x => !x.Deleted).ToArray(); laneSelector.SelectedItem = selected; }
    private readonly StackPanel optionManager = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 8) };
}

internal sealed class TaskEditorWindow : SurfaceWindow
{
    public EditorSurface View { get; }
    public TaskEditorWindow(EditorSurface view) : base(430, 650)
    { View = view; WindowLayer = 1; Title = "编辑任务"; Root.Children.Add(view); view.Host = this; Closed += (_, _) => view.Destroy(); }
}
