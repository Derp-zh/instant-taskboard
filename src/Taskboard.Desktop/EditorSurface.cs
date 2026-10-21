using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class EditorSurface : Grid
{
    internal const double DefaultHeight = 650 * 11.0 / 13;
    public Grid Root => this;
    public SurfaceWindow? Host { get; internal set; }
    public bool IsActive => Host?.IsActive == true && (Session.TaskId is not null || controller.ActiveDraft == Session.Id);
    private readonly BoardController controller;
    private readonly bool snapshotMode;
    public EditSession Session { get; }
    private readonly StackPanel stages = new();
    private readonly System.Collections.Generic.Dictionary<Guid, TextBox> stageThreads = new();
    internal readonly System.Collections.Generic.Dictionary<Guid, StageConversationPicker> StagePickers = new();
    private readonly TextBlock status;
    private readonly Button aiButton;
    private readonly Button priorityButton;
    private readonly OptionMenuToggle aiMenu;
    private readonly OptionMenuToggle priorityMenu;
    private readonly DispatcherTimer autosave;
    private bool submitting;
    private bool closing;
    private readonly Func<System.Threading.Tasks.Task<bool>> saveAndClose;
    internal System.Threading.Tasks.Task<bool> SaveAndClose() => saveAndClose();
    private readonly Button closeButton;
    private readonly Border draftNumber;
    private readonly TextBlock numberText;
    internal int DraftNumber { get; private set; }
    internal void SetDraftPresentation(bool front, int number)
    {
        DraftNumber = number; numberText.Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        closeButton.Visibility = front ? Visibility.Visible : Visibility.Collapsed;
        draftNumber.Visibility = front ? Visibility.Collapsed : Visibility.Visible;
    }
    public EditorSurface(BoardController controller, EditSession session, bool snapshotMode = false)
    {
        this.controller = controller; Session = session; this.snapshotMode = snapshotMode;
        autosave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
        Root.RowDefinitions.Add(new RowDefinition()); Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeAll, Margin = new Thickness(14, 4, 10, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Ui.Label(session.TaskId is null ? "新建任务 · 自动暂存" : "编辑任务 · 保存后生效", 13));
        numberText = new TextBlock { FontSize = 12, Foreground = Brushes.White };
        var legacyClose = Ui.Button("×", () => { }); legacyClose.FontFamily = new FontFamily("Microsoft YaHei UI");
        legacyClose.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var badgeDiameter = Math.Min(legacyClose.DesiredSize.Width, legacyClose.DesiredSize.Height) - 6;
        draftNumber = new Border { Tag = "DraftOrdinal", Width = badgeDiameter, Height = badgeDiameter, Margin = new Thickness(3),
            Background = Ui.Brush("#C04B5B70"), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(badgeDiameter / 2), IsHitTestVisible = false,
            Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(8, 4, 8, 4), Child = numberText }, Visibility = Visibility.Collapsed };
        closeButton = Ui.Button(session.TaskId is null ? "×" : "取消保存", CloseEditor); closeButton.Tag = "EditorClose"; closeButton.Width = session.TaskId is null ? 48 : 84; closeButton.Height = 40; closeButton.Padding = new Thickness(0);
        closeButton.Background = Ui.Brush("#C04B5B70"); closeButton.BorderBrush = Brushes.White; closeButton.Foreground = Brushes.White;
        closeButton.Style = (Style)Application.Current.FindResource("EditorCloseButton");
        var closeSlot = new Grid { VerticalAlignment = VerticalAlignment.Center }; closeSlot.Children.Add(closeButton); closeSlot.Children.Add(draftNumber);
        Grid.SetColumn(closeSlot, 1); header.Children.Add(closeSlot); Root.Children.Add(header);
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
        newStage.Tag = "AddStage"; newStage.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(newStage);
        var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        for (var i = 0; i < 3; i++) row.ColumnDefinitions.Add(new ColumnDefinition());
        var statePanel = new StackPanel(); statePanel.Children.Add(Ui.Label("当前状态", 12, Ui.Muted));
        var lane = laneSelector = new ComboBox { ItemsSource = controller.Engine.State.Preferences.Columns.Where(x => !x.Deleted).ToArray(), SelectedItem = session.StatusSelected ? BoardColumns.For(controller.Engine.State.Preferences,session.Working) : null, Margin = new Thickness(0, 3, 5, 0), MinHeight = 32, ToolTip = "请选择当前状态" };
        lane.SelectionChanged += (_, _) => { if (lane.SelectedItem is BoardColumn definition) { definition.Assign(session.Working); session.StatusSelected = true; Changed(); } };
        statePanel.Children.Add(lane); row.Children.Add(statePanel);
        var aiPanel = new StackPanel(); aiPanel.Children.Add(Ui.Label("平台（多选）", 12, Ui.Muted));
        aiButton = Ui.Button("选择", () => ShowOptions(true)); aiPanel.Children.Add(aiButton); Grid.SetColumn(aiPanel, 1); row.Children.Add(aiPanel);
        var priorityPanel = new StackPanel(); priorityPanel.Children.Add(Ui.Label("优先级", 12, Ui.Muted));
        priorityButton = Ui.Button("选择", () => ShowOptions(false)); priorityPanel.Children.Add(priorityButton); Grid.SetColumn(priorityPanel, 2); row.Children.Add(priorityPanel);
        aiMenu = new OptionMenuToggle(aiButton); priorityMenu = new OptionMenuToggle(priorityButton);
        body.Children.Add(row); body.Children.Add(optionManager); RefreshOptionLabels();
        var reminderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        var reminderEnabled = new CheckBox { Tag = "ReminderToggle", Width = 22, Height = 22, IsChecked = session.Working.Reminder.Enabled, Foreground = Ui.Text,
            VerticalAlignment = VerticalAlignment.Center, Clip = new EllipseGeometry(new Point(11, 11), 11, 11) };
        System.Windows.Automation.AutomationProperties.SetName(reminderEnabled, "定时提醒");
        reminderRow.Children.Add(reminderEnabled);
        reminderRow.Children.Add(new TextBlock { Text = "定时提醒", Foreground = Ui.Text, Margin = new Thickness(8, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center });
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
        body.Children.Add(reminderRow); body.Children.Add(reminderPanel);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); Root.Children.Add(scroll);
        var bottom = new Grid { Margin = new Thickness(12, 6, 12, 12) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        status = Ui.Label(session.TaskId is null ? "未保存到任务列表" : "修改尚未提交", 11, Ui.Muted); Grid.SetColumnSpan(status, 2); bottom.Children.Add(status);
        var delete = Ui.Button("删除任务", () =>
        {
            if (closing || submitting) return;
            autosave.Stop();
            if (Session.TaskId is Guid task) controller.DeleteTask(task);
            else controller.DeleteDraft(this);
        }); delete.Height = closeButton.Height; delete.HorizontalAlignment = HorizontalAlignment.Left; delete.Foreground = Ui.Brush("#F28F98"); Grid.SetRow(delete, 1); bottom.Children.Add(delete);
        saveAndClose = async () =>
        {
            if (submitting || closing || snapshotMode) return false;
            if (!Session.StatusSelected) { status.Text = "请先选择当前状态，再保存任务。"; laneSelector.Focus(); laneSelector.IsDropDownOpen = true; return false; }
            if (reminderEnabled.IsChecked == true && error.Text.Length > 0) { Ui.Error(error.Text); return false; }
            submitting = true;
            Root.IsHitTestVisible = false;
            try
            {
                autosave.Stop(); controller.Engine.StageSession(Session);
                if (await controller.SubmitSession(Session)) { controller.RemoveEditor(this); Destroy(); return true; }
                return false;
            }
            finally { submitting = false; Root.IsHitTestVisible = true; }
        };
        var save = Ui.Button("保存任务", async () => await SaveAndClose(), true);
        save.Foreground = Ui.Brush("#102033"); save.Tag = "SaveTask";
        if (session.TaskId is not null) closeButton.SetBinding(FrameworkElement.WidthProperty,
            new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = save });
        save.Height = closeButton.Height; Grid.SetColumn(save, 1); Grid.SetRow(save, 1); bottom.Children.Add(save); Grid.SetRow(bottom, 2); Root.Children.Add(bottom);
        autosave.Tick += async (_, _) => { autosave.Stop(); if (await controller.Flush() && !submitting && !closing) status.Text = Session.TaskId is null ? Session.Working.HasContent ? "草稿已暂存" : "未保存到任务列表" : "修改已暂存，正式任务仍保持原值"; };
        // Block edits during persistence without triggering the disabled theme's
        // global dimming/focus change just before the closing snapshot.
        Root.PreviewKeyDown += (_, e) => { if (submitting || closing) e.Handled = true; };
        Root.PreviewTextInput += (_, e) => { if (submitting || closing) e.Handled = true; };
        Root.AddHandler(TextCompositionManager.PreviewTextInputStartEvent, new TextCompositionEventHandler((_, e) => { if (submitting || closing) e.Handled = true; }));
        Root.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent, new TextCompositionEventHandler((_, e) => { if (submitting || closing) e.Handled = true; }));
        DataObject.AddPastingHandler(Root, (_, e) => { if (submitting || closing) e.CancelCommand(); });
        Root.PreviewMouseDown += (_, e) => { foreach (var picker in StagePickers.Values) picker.CollapseUnlessContains(e.OriginalSource as DependencyObject); };
    }
    private void Changed()
    {
        if (snapshotMode) return;
        if (status is null) return;
        controller.Engine.StageSession(Session); status.Text = Session.TaskId is null ? Session.Working.HasContent ? "正在暂存…" : "未保存到任务列表" : "修改尚未提交";
        autosave.Stop(); autosave.Start();
    }
    private void RenderStages()
    {
        var buffers = StagePickers.ToDictionary(x => x.Key, x => (Text: x.Value.Input.Text, Expanded: x.Value.Pane.Visibility == Visibility.Visible));
        foreach (var picker in StagePickers.Values) picker.Dispose(); StagePickers.Clear(); stages.Children.Clear(); stageThreads.Clear();
        foreach (var stage in Session.Working.Stages)
        {
            var panel = new StackPanel();
            var heading = new DockPanel { Margin = new Thickness(7, 5, 7, 0) };
            var current = Ui.Button("", () => { }); current.Tag = "StageRun";
            current.Content = StageRunIcon.Caption(false, false);
            current.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            current.Width = current.DesiredSize.Width - current.Margin.Left - current.Margin.Right;
            void PresentRunState()
            {
                current.Content = StageRunIcon.Caption(stage.IsRunning == true, !snapshotMode);
                current.ToolTip = stage.IsRunning == true ? "点击设为未运行" : "点击设为运行中";
                System.Windows.Automation.AutomationProperties.SetName(current, stage.IsRunning == true ? "运行中，点击切换为未运行" : "未运行，点击切换为运行中");
            }
            PresentRunState(); current.Click += (_, _) =>
            {
                var running = stage.IsRunning != true;
                if (!BoardEngine.CanSetStageRunning(Session.Working, stage.Id, running)) { Ui.Hint(current, BoardEngine.LastRunningStageMessage, !controller.Smoke); return; }
                stage.IsRunning = running; Changed(); PresentRunState();
            };
            var bindingPanel = StageBindingEditor(stage, out var bindingButton);
            if (buffers.TryGetValue(stage.Id, out var buffer)) { stageThreads[stage.Id].Text = buffer.Text; bindingPanel.Visibility = buffer.Expanded ? Visibility.Visible : Visibility.Collapsed; }
            bindingButton.Width = current.Width * 1.6;
            DockPanel.SetDock(bindingButton, Dock.Right); heading.Children.Add(bindingButton);
            DockPanel.SetDock(current, Dock.Right); heading.Children.Add(current);
            var name = Ui.Input(stage.Name); name.MinHeight = 28; name.MaxWidth = 155; name.Padding = new Thickness(9, 4, 9, 4);
            name.Margin = new Thickness(0, 3, 3, 3); name.HorizontalAlignment = HorizontalAlignment.Left; name.Foreground = Ui.Accent;
            name.ToolTip = "阶段名称"; name.TextChanged += (_, _) => { stage.Name = name.Text; Changed(); };
            var remove = Ui.Button("×", () => { }); remove.Tag = "StageRemove"; remove.DataContext = stage;
            remove.Width = remove.Height = 24; remove.Padding = new Thickness(0); remove.Margin = new Thickness(1, 3, 4, 3);
            remove.VerticalAlignment = VerticalAlignment.Center; remove.ToolTip = "删除此阶段";
            remove.HorizontalContentAlignment = HorizontalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(remove, "删除阶段：" + stage.Name);
            remove.Click += (_, _) =>
            {
                if (snapshotMode || submitting || closing) return;
                if (!controller.Engine.RemoveStage(Session, stage.Id, out var message))
                { if (message is not null) Ui.Hint(remove, message, !controller.Smoke); return; }
                RenderStages(); Changed();
            };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(name); nameRow.Children.Add(remove); heading.Children.Add(nameRow);
            panel.Children.Add(heading); panel.Children.Add(bindingPanel);
            var text = Ui.Input(stage.Text, true); text.Style = (Style)FindResource("StageBodyInput"); text.Background = Brushes.Transparent;
            text.BorderThickness = new Thickness(0); text.Margin = new Thickness(0); text.Padding = new Thickness(10, 5, 10, 10);
            text.MaxHeight = 180; text.ToolTip = "阶段详情"; text.TextChanged += (_, _) => { stage.Text = text.Text; Changed(); }; panel.Children.Add(text);
            var frame = Ui.Panel(panel, Ui.Brush("#A0121D2D"), 0); frame.CornerRadius = new CornerRadius(20);
            StagePickers[stage.Id].StageScope = frame;
            panel.IsKeyboardFocusWithinChanged += (_, _) => frame.BorderBrush = panel.IsKeyboardFocusWithin ? Ui.Accent : Ui.Line;
            stages.Children.Add(frame);
        }
    }
    private StackPanel StageBindingEditor(Stage stage, out Button button)
    {
        var picker = new StageConversationPicker(controller, stage, Changed, snapshotMode);
        StagePickers[stage.Id] = picker; stageThreads[stage.Id] = picker.Input; button = picker.Trigger; return picker.Pane;
    }
    public void RefreshOptionLabels()
    {
        var selected = controller.Engine.State.AiOptions.Where(x => Session.Working.AiIds.Contains(x.Id)).Select(x => x.Name).ToArray();
        var caption = selected.Length == 0 ? "未选择 ▾" : selected[0] + (selected.Length > 1 ? " +" + (selected.Length - 1) : "");
        aiButton.Content = new TextBlock { Text = caption, TextTrimming = TextTrimming.CharacterEllipsis };
        aiButton.ToolTip = selected.Length == 0 ? "未选择平台" : string.Join("、", selected);
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
        _ = controller.CleanupRetainedStages();
        if (Session.TaskId is null) { controller.CloseDraftWindow(); return; }
        closing = true; Root.IsHitTestVisible = false;
        try
        {
            autosave.Stop(); controller.Engine.Discard(Session);
            if (!await controller.Flush()) { Session.Visible = true; controller.Engine.StageSession(Session); return; }
            controller.RemoveEditor(this); Destroy();
        }
        finally { closing = false; Root.IsHitTestVisible = true; }
    }
    public void PrepareHide() { foreach (var picker in StagePickers.Values) picker.Collapse(); aiMenu.Close(); priorityMenu.Close(); autosave.Stop(); controller.Engine.StageSession(Session); }
    internal void Suspend()
    {
        if (closing || submitting) return;
        PrepareHide(); Session.Visible = false; controller.Engine.StageSession(Session);
        controller.RemoveEditor(this); Destroy();
    }
    public void Show() => controller.ShowEditor(this, false);
    public void Activate() => controller.ShowEditor(this, true);
    public void Destroy() { foreach (var picker in StagePickers.Values) picker.Dispose(); aiMenu.Dispose(); priorityMenu.Dispose(); autosave.Stop(); Host = null; }
    private void ShowOptionManager(bool ai)
    {
        optionManager.Children.Clear(); optionManager.Visibility = Visibility.Visible;
        var heading = new DockPanel(); var close = Ui.Button("×", () => optionManager.Visibility = Visibility.Collapsed); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        heading.Children.Add(Ui.Label(ai ? "平台选项" : "优先级选项", 14)); optionManager.Children.Add(heading);
        foreach (var option in ai ? controller.Engine.State.AiOptions : controller.Engine.State.PriorityOptions)
        {
            var row = new DockPanel(); var remove = Ui.Button("删除", () => { controller.DeleteOption(option.Id, ai); ShowOptionManager(ai); }); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var color = Ui.Button("●", () => controller.ColorMenu(option, optionManager)); color.Foreground = Ui.Brush(option.Color); DockPanel.SetDock(color, Dock.Right); row.Children.Add(color);
            var name = Ui.Input(option.Name); name.LostKeyboardFocus += (_, _) => { if (string.IsNullOrWhiteSpace(name.Text)) { name.Text = option.Name; return; } option.Name = name.Text.Trim(); controller.OptionsChanged(); }; row.Children.Add(name); optionManager.Children.Add(row);
        }
        optionManager.Children.Add(Ui.Button("＋ 新增选项", () => { var values = ai ? controller.Engine.State.AiOptions : controller.Engine.State.PriorityOptions; values.Add(new FieldOption { Name = "新选项" }); controller.OptionsChanged(); ShowOptionManager(ai); }));
    }
    private readonly ComboBox laneSelector;
    internal EditorSurface CreateSnapshot()
    {
        var width = double.IsNaN(Width) ? ActualWidth : Width;
        var height = double.IsNaN(Height) ? ActualHeight : Height;
        var copy = new EditorSurface(controller, Copies.Of(Session), snapshotMode: true) { Width = width, Height = height };
        QuickScroll.SetVerticalBarWidth(copy, QuickScroll.GetVerticalBarWidth(this));
        QuickScroll.SetThumbMargin(copy, QuickScroll.GetThumbMargin(this));
        copy.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, GetValue(System.Windows.Documents.TextElement.FontFamilyProperty));
        copy.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, GetValue(System.Windows.Documents.TextElement.ForegroundProperty));
        copy.SetDraftPresentation(closeButton.Visibility == Visibility.Visible, DraftNumber); copy.status.Text = status.Text;
        foreach (var pair in StagePickers)
            if (copy.StagePickers.TryGetValue(pair.Key, out var counterpart)) counterpart.Pane.Visibility = pair.Value.Pane.Visibility;
        copy.Measure(new Size(width, height)); copy.Arrange(new Rect(0, 0, width, height)); copy.UpdateLayout();
        foreach (var pair in SnapshotChildren<TextBox>(this).Zip(SnapshotChildren<TextBox>(copy))) pair.Second.Text = pair.First.Text;
        foreach (var pair in SnapshotChildren<ScrollViewer>(this).Zip(SnapshotChildren<ScrollViewer>(copy)))
        { pair.Second.ScrollToVerticalOffset(pair.First.VerticalOffset); pair.Second.ScrollToHorizontalOffset(pair.First.HorizontalOffset); }
        copy.UpdateLayout();
        foreach (var pair in SnapshotChildren<StageRunIcon>(this).Zip(SnapshotChildren<StageRunIcon>(copy))) pair.Second.FreezeAt(pair.First.Angle);
        foreach (var pair in SnapshotChildren<TextBox>(this).Zip(SnapshotChildren<TextBox>(copy)))
            if (pair.First.Template.FindName("frame", pair.First) is Border sourceFrame && pair.Second.Template.FindName("frame", pair.Second) is Border copyFrame)
                copyFrame.BorderBrush = sourceFrame.BorderBrush;
        foreach (var pair in stages.Children.OfType<Border>().Zip(copy.stages.Children.OfType<Border>())) pair.Second.BorderBrush = pair.First.BorderBrush;
        foreach (var pair in SnapshotChildren<Button>(this).Zip(SnapshotChildren<Button>(copy)))
        {
            if (!Equals(Convert.ToString(pair.First.Content), Convert.ToString(pair.Second.Content))) continue;
            var sourceFrame = pair.First.Template.FindName("border", pair.First) as Border ?? pair.First.Template.FindName("frame", pair.First) as Border;
            var copyFrame = pair.Second.Template.FindName("border", pair.Second) as Border ?? pair.Second.Template.FindName("frame", pair.Second) as Border;
            if (sourceFrame is not null && copyFrame is not null) { copyFrame.BorderBrush = sourceFrame.BorderBrush; copyFrame.Background = sourceFrame.Background; }
        }
        return copy;
    }
    private static System.Collections.Generic.IEnumerable<T> SnapshotChildren<T>(DependencyObject element) where T : DependencyObject
    {
        if (element is T value) yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var child in SnapshotChildren<T>(VisualTreeHelper.GetChild(element, i))) yield return child;
    }
    public void RefreshColumns() { var selected = Session.StatusSelected ? BoardColumns.For(controller.Engine.State.Preferences,Session.Working) : null; laneSelector.ItemsSource = controller.Engine.State.Preferences.Columns.Where(x => !x.Deleted).ToArray(); laneSelector.SelectedItem = selected; }
    private readonly StackPanel optionManager = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 8) };
}

internal sealed class TaskEditorWindow : SurfaceWindow
{
    protected override bool ScrollEnabled => true;
    public EditorSurface View { get; }
    public TaskEditorWindow(EditorSurface view) : base(430, EditorSurface.DefaultHeight)
    { View = view; WindowLayer = 1; Title = "编辑任务"; Root.Children.Add(view); view.Host = this; Closed += (_, _) => view.Destroy(); }
}
