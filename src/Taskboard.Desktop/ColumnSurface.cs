using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class ColumnSurface : SurfaceWindow
{
    protected override bool ScrollEnabled => false;
    internal override double GlassTransparency => Transparency;
    // A layered child needs nonzero alpha over its entire existing input area.
    // The visible tint remains in the persistent compositor layer.
    protected override void ApplyGlassTint() => Root.Background = Ui.Brush("#01192333");
    private readonly BoardController controller;
    public BoardColumn Definition { get; private set; }
    public Lane Lane => Definition.BuiltinLane ?? Lane.Refining;
    private readonly TextBlock heading;
    private readonly Thumb group;
    private readonly ObservableCollection<object> items = new();
    private readonly ListBox list;
    private readonly TextBlock count;
    private readonly Grid footer = new();
    private readonly Thumb corner;
    private bool edgeResize;
    private readonly MenuItem resizeToggle;
    private readonly Grid edges = new();
    private Action? flushInline;
    private Action? finishInline;
    private Action? releaseInlineInput;
    private Guid? inlineTask;
    private System.Windows.Threading.DispatcherTimer? inlineTimer;
    private Guid? draggedTask;
    private Grid? cardPreview;
    private Guid? previewBefore;
    private long lastDragScroll;
    internal void RestartCardMarquees()
    {
        foreach (var item in items.OfType<WorkItem>()) RenderedCard(item.Id)?.SetMarquees(true);
        InvalidateAnimation();
    }
    public ColumnSurface(BoardController controller, Lane lane) : this(controller, controller.Engine.State.Preferences.Columns.First(x => x.BuiltinLane == lane)) { }
    public ColumnSurface(BoardController controller, BoardColumn definition) : base(240, 680)
    {
        this.controller = controller; Definition = definition; MinWidth = 80; MinHeight = 260; WindowLayer = 0;
        Title = definition.Name;
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        Root.RowDefinitions.Add(new RowDefinition());
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Margin = new Thickness(12, 3.6, 12, 0), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = heading = Ui.Label(definition.Name, 15); title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 2.4, 0, 2.4);
        title.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(title);
        count = Ui.Label("0", 12, Ui.Muted); count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(0, 2.4, 0, 2.4);
        Grid.SetColumn(count, 1); header.Children.Add(count); Root.Children.Add(header); MoveWith(header);
        list = new ListBox { ItemsSource = items, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, AllowDrop = true };
        VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
        ScrollViewer.SetCanContentScroll(list, true); ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.FocusableProperty, false));
        var template = new ControlTemplate(typeof(ListBoxItem));
        template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)); style.Setters.Add(new Setter(Control.TemplateProperty, template));
        list.ItemContainerStyle = style;
        var cardFactory = new FrameworkElementFactory(typeof(TaskCard));
        cardFactory.SetValue(TaskCard.ControllerProperty, controller);
        var taskTemplate = new DataTemplate { VisualTree = cardFactory };
        var entryFactory = new FrameworkElementFactory(typeof(ContentPresenter)); entryFactory.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding());
        list.ItemTemplateSelector = new LaneTemplateSelector(taskTemplate, new DataTemplate { VisualTree = entryFactory });
        Grid.SetRow(list, 1); Root.Children.Add(list);
        list.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent("Taskboard.Task") ? DragDropEffects.Move : DragDropEffects.None;
            if (e.Effects == DragDropEffects.Move)
            {
                var point = e.GetPosition(list); var scroll = FindScroll(list);
                if (Environment.TickCount64 - lastDragScroll >= 70)
                {
                    if (point.Y < 30 && scroll is not null) { QuickScroll.StepItem(scroll, -1); lastDragScroll = Environment.TickCount64; }
                    else if (point.Y > list.ActualHeight - 30 && scroll is not null) { QuickScroll.StepItem(scroll, 1); lastDragScroll = Environment.TickCount64; }
                }
                if (controller.CardDrag is { } drag) drag.Preview(this, DropBefore(point));
            }
            e.Handled = true;
        };
        list.DragLeave += (_, e) => { if (!new Rect(list.RenderSize).Contains(e.GetPosition(list))) controller.CardDrag?.Leave(this); };
        list.Drop += (_, e) =>
        {
            if (e.Data.GetData("Taskboard.Task") is not Guid id) return;
            if (controller.CardDrag is { } drag && drag.Id == id)
            {
                var previewTarget = cardPreview is not null ? previewBefore : DropBefore(e.GetPosition(list));
                drag.Dispose(); controller.CardDrag = null;
                controller.MoveTask(id, Lane, previewTarget, Definition.BuiltinLane is null ? Definition.Id : null); e.Effects = DragDropEffects.Move; e.Handled = true; return;
            }
            var point = e.GetPosition(list); var source = list.InputHitTest(point) as DependencyObject;
            var card = Ancestor<TaskCard>(source);
            Guid? before = null;
            if (card?.DataContext is WorkItem hit)
            {
                if (e.GetPosition(card).Y < card.ActualHeight / 2) before = hit.Id;
                else before = items.Skip(items.IndexOf(hit) + 1).OfType<WorkItem>().FirstOrDefault()?.Id;
            }
            controller.MoveTask(id, Lane, before, Definition.BuiltinLane is null ? Definition.Id : null); e.Handled = true;
        };
        footer.Margin = new Thickness(0, 0, 0, 10); items.Add(footer);
        ResetFooter();
        var menu = new ContextMenu(); resizeToggle = new MenuItem { Header = "调整本窗口大小" };
        resizeToggle.Click += (_, _) => { if (edgeResize) StopResize(); else { edgeResize = true; resizeToggle.Header = "结束调整"; edges.Visibility = Visibility.Visible; count.Text = "调整中"; controller.SuppressCollapse = true; } }; menu.Items.Add(resizeToggle);
        var reset = new MenuItem { Header = "本窗口恢复到默认" };
        reset.Click += (_, _) => { if (edgeResize) StopResize(); controller.RestoreColumnDefault(this); }; menu.Items.Add(reset);
        Root.ContextMenu = menu;
        Root.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) StopResize(); };
        Deactivated += (_, _) => { if (edgeResize) StopResize(); };
        corner = new Thumb { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE, Background = Ui.Line, Opacity = .75, Margin = new Thickness(2) };
        Grid.SetRowSpan(corner, 3); Root.Children.Add(corner);
        edges.Visibility = Visibility.Collapsed; Grid.SetRowSpan(edges, 3); Root.Children.Add(edges);
        Edge(HorizontalAlignment.Left, VerticalAlignment.Stretch, Cursors.SizeWE, e => { var delta = Math.Min(e.HorizontalChange, Width - MinWidth); Left += delta; Width -= delta; });
        Edge(HorizontalAlignment.Right, VerticalAlignment.Stretch, Cursors.SizeWE, e => Width = Math.Max(MinWidth, Width + e.HorizontalChange));
        Edge(HorizontalAlignment.Stretch, VerticalAlignment.Top, Cursors.SizeNS, e => { var delta = Math.Min(e.VerticalChange, Height - MinHeight); Top += delta; Height -= delta; });
        Edge(HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Cursors.SizeNS, e => Height = Math.Max(MinHeight, Height + e.VerticalChange));
        corner.DragDelta += (_, e) => { Width = Math.Max(MinWidth, Width + e.HorizontalChange); Height = Math.Max(MinHeight, Height + e.VerticalChange); };
        corner.DragCompleted += (_, _) => controller.SavePosition(Definition.PositionKey, this);
        PositionChanged += () => controller.SavePosition(Definition.PositionKey, this);
        {
            group = new Thumb { Width = 22, Height = 22, Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNESW, ToolTip = "拖动：等比例调整最大范围" };
            var glyph = new FrameworkElementFactory(typeof(TextBlock)); glyph.SetValue(TextBlock.TextProperty, "⤢"); glyph.SetValue(TextBlock.FontSizeProperty, 20.0); glyph.SetValue(TextBlock.ForegroundProperty, Ui.Accent); glyph.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
            var dragArea = new FrameworkElementFactory(typeof(Border)); dragArea.SetValue(Border.BackgroundProperty, Brushes.Transparent); dragArea.AppendChild(glyph);
            group.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = dragArea }; group.Tag = "WorkspaceResizeGrip"; Grid.SetRowSpan(group, 3); Root.Children.Add(group);
            group.DragStarted += (_, _) => controller.BeginWorkspaceResize(); group.DragDelta += (_, _) => controller.ResizeWorkspaceFromPointer(); group.DragCompleted += (_, _) => controller.EndWorkspaceResize();
        }
        Refresh(); ApplySettings();
        Closed += (_, _) => { inlineTimer?.Stop(); releaseInlineInput?.Invoke(); };
    }

    private void Edge(HorizontalAlignment horizontal, VerticalAlignment vertical, Cursor cursor, Action<DragDeltaEventArgs> drag)
    {
        var thumb = new Thumb { HorizontalAlignment = horizontal, VerticalAlignment = vertical, Cursor = cursor, Background = Brushes.Transparent };
        if (horizontal == HorizontalAlignment.Stretch) thumb.Height = 8; else thumb.Width = 8;
        var template = new ControlTemplate(typeof(Thumb)); var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty, Brushes.Transparent); template.VisualTree = border; thumb.Template = template;
        thumb.DragDelta += (_, e) => drag(e); thumb.DragCompleted += (_, _) => StopResize(); edges.Children.Add(thumb);
    }
    private void StopResize() { edgeResize = false; resizeToggle.Header = "调整本窗口大小"; edges.Visibility = Visibility.Collapsed; count.Text = items.OfType<WorkItem>().Count().ToString(); controller.SuppressCollapse = false; controller.SavePosition(Definition.PositionKey, this); }
    public void ApplySettings() { corner.Visibility = controller.Engine.State.Preferences.ResizeCorner ? Visibility.Visible : Visibility.Collapsed; group.Visibility = controller.Engine.State.Preferences.Columns.FirstOrDefault(x=>!x.Deleted && x.Visible)?.Id == Definition.Id ? Visibility.Visible : Visibility.Collapsed; }
    public void Rebind(BoardColumn definition) { Definition = definition; Title = heading.Text = definition.Name; Refresh(); ApplySettings(); }
    public void Refresh()
    {
        InvalidateAnimation();
        if (cardPreview is not null) items.Remove(cardPreview);
        var desired = controller.Engine.State.Tasks.Where(x => Definition.Contains(x) && x.Id != draggedTask && x.Id != inlineTask).ToArray();
        for (var i = items.Count - 1; i >= 0; i--) if (items[i] is WorkItem item && !desired.Any(x => x.Id == item.Id)) items.RemoveAt(i);
        for (var i = 0; i < desired.Length; i++)
        {
            if (i < items.Count && items[i] is WorkItem at && at.Id == desired[i].Id) { if (!ReferenceEquals(items[i], desired[i])) items[i] = desired[i]; continue; }
            var existing = items.OfType<WorkItem>().FirstOrDefault(x => x.Id == desired[i].Id);
            if (existing is not null) { var index = items.IndexOf(existing); items.Move(index, i); if (!ReferenceEquals(items[i], desired[i])) items[i] = desired[i]; }
            else items.Insert(i, desired[i]);
        }
        count.Text = edgeResize ? "调整中" : (desired.Length + (cardPreview is null ? 0 : 1) + (inlineTask is Guid id && controller.Engine.State.Tasks.Any(x => x.Id == id && Definition.Contains(x)) ? 1 : 0)).ToString();
        if (cardPreview is not null) InsertPreview();
    }
    internal void BeginCardDrag(Guid id) { draggedTask = id; Refresh(); }
    internal void EndCardDrag() { ClearCardPreview(); draggedTask = null; Refresh(); }
    internal void ClearCardPreview()
    {
        if (cardPreview is not null) items.Remove(cardPreview); cardPreview = null; previewBefore = null;
        if (!edgeResize) count.Text = items.OfType<WorkItem>().Count().ToString();
    }
    internal bool SetCardPreview(WorkItem item, Guid? before)
    {
        if (cardPreview is not null && previewBefore == before) return false;
        if (cardPreview is null)
        {
            cardPreview = new Grid { Margin = new Thickness(0, 0, 0, 8), IsHitTestVisible = false, ClipToBounds = true };
            var previewItem = Copies.Of(item); Definition.Assign(previewItem);
            var card = new TaskCard { Controller = controller, DataContext = previewItem, Margin = new Thickness(0), BorderBrush = Brushes.Transparent };
            cardPreview.Children.Add(card);
            cardPreview.Children.Add(new System.Windows.Shapes.Rectangle { RadiusX = 12, RadiusY = 12, Stroke = Ui.Accent,
                StrokeThickness = 1, StrokeDashArray = new DoubleCollection([4, 4]), Margin = new Thickness(.5) });
        }
        else items.Remove(cardPreview);
        previewBefore = before; InsertPreview(); if (!edgeResize) count.Text = (items.OfType<WorkItem>().Count() + 1).ToString(); return true;
    }
    private void InsertPreview()
    {
        if (cardPreview is null) return;
        var before = items.OfType<WorkItem>().FirstOrDefault(x => x.Id == previewBefore);
        items.Insert(before is null ? items.IndexOf(footer) : items.IndexOf(before), cardPreview);
    }
    private Guid? DropBefore(Point point)
    {
        if (cardPreview is not null && VisualTreeHelper.GetParent(cardPreview) is not null &&
            new Rect(cardPreview.RenderSize).Contains(list.TranslatePoint(point, cardPreview))) return previewBefore;
        foreach (var item in items.OfType<WorkItem>())
        {
            var card = FindChild<TaskCard>(list.ItemContainerGenerator.ContainerFromItem(item));
            if (card is not null && card.TranslatePoint(new Point(0, card.ActualHeight / 2), list).Y > point.Y) return item.Id;
        }
        return null;
    }
    public void Repaint() { var values = items.ToArray(); items.Clear(); foreach (var item in values) items.Add(item); }
    public void RefreshCards(System.Collections.Generic.IReadOnlyCollection<Guid> ids)
    {
        InvalidateAnimation();
        foreach (var item in items.OfType<WorkItem>().Where(x => ids.Contains(x.Id)))
            FindChild<TaskCard>(list.ItemContainerGenerator.ContainerFromItem(item))?.Rebuild();
    }
    internal TaskCard? RenderedCard(Guid id)
    {
        var item = items.OfType<WorkItem>().FirstOrDefault(x => x.Id == id);
        return item is null ? null : FindChild<TaskCard>(list.ItemContainerGenerator.ContainerFromItem(item));
    }
    public TaskCard? Locate(Guid id)
    {
        var item = items.OfType<WorkItem>().FirstOrDefault(x => x.Id == id); if (item is null) return null;
        list.ScrollIntoView(item); list.UpdateLayout();
        var container = list.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;
        return FindChild<TaskCard>(container);
    }
    private void ResetFooter()
    {
        footer.Children.Clear();
        footer.Children.Add(Ui.DashedButton("＋ 新建任务", () => { if (Lane == Lane.Inspiration && controller.Engine.State.Preferences.InspirationQuickEntry) InlineNew(); else controller.OpenNewEntry(Lane, Definition.BuiltinLane is null ? Definition.Id : null); }));
    }
    internal void RefreshEntryMode() { if (finishInline is not null) finishInline(); else ResetFooter(); }
    internal void RevealNewEntry() { list.ScrollIntoView(footer); list.UpdateLayout(); }
    internal bool NewEntryIsLast => ReferenceEquals(items.LastOrDefault(), footer) && VisualTreeHelper.GetParent(footer) is not null;
    internal bool IsTaskCardAt(Point screenPoint) => IsVisible &&
        Ancestor<TaskCard>(Root.InputHitTest(Root.PointFromScreen(screenPoint)) as DependencyObject) is { ArchivedPreview: false };
    internal bool IsOutsideDismissArea(Point screenPoint)
    {
        if (!IsVisible) return false;
        var hit = Root.InputHitTest(Root.PointFromScreen(screenPoint)) as DependencyObject;
        return hit is not null && Ancestor<TaskCard>(hit) is null && !SurfaceWindow.IsInteractive(hit)
            && Ancestor<System.Windows.Controls.Primitives.ScrollBar>(hit) is null;
    }
    public void PrepareHide()
    {
        flushInline?.Invoke();
        foreach (var item in items.OfType<WorkItem>()) RenderedCard(item.Id)?.SetMarquees(false);
        InvalidateAnimation();
    }
    private static T? Ancestor<T>(DependencyObject? source) where T : DependencyObject
    { for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current)) if (current is T value) return value; return null; }
    private static ScrollViewer? FindScroll(DependencyObject source) => FindChild<ScrollViewer>(source);
    internal static T? FindChild<T>(DependencyObject? source) where T : DependencyObject
    {
        if (source is null) return null;
        if (source is T value) return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
        { var found = FindChild<T>(VisualTreeHelper.GetChild(source, i)); if (found is not null) return found; }
        return null;
    }
}

internal sealed class LaneTemplateSelector(DataTemplate task, DataTemplate entry) : DataTemplateSelector
{
    public override DataTemplate SelectTemplate(object item, DependencyObject container) => item is WorkItem ? task : entry;
}

internal sealed partial class TaskCard : Border
{
    public static readonly DependencyProperty ControllerProperty = DependencyProperty.Register(nameof(Controller), typeof(BoardController), typeof(TaskCard),
        new PropertyMetadata(null, (d, _) => ((TaskCard)d).Build()));
    public BoardController Controller { get => (BoardController)GetValue(ControllerProperty); set => SetValue(ControllerProperty, value); }
    public bool ArchivedPreview { get; set; }
    private Point start;
    private bool pressed;
    private long flashGeneration;
    internal int BuildCount { get; private set; }
    private string? presentationKey;
    private string PresentationKey(WorkItem item) => System.Text.Json.JsonSerializer.Serialize(new
    {
        item.DisplayTitle, item.Lane, StageCount = item.Stages.Count, item.TimerUnread, item.PendingReasons, ArchivedPreview,
        Stages = (ArchivedPreview ? BoardEngine.CardStages(item) : Controller.VisibleCardStages(item)).Select(x => new
        { x.Id, x.Name, x.Text, x.IsRunning, x.Codex?.ThreadId, x.Codex?.ThreadName }),
        Ai = Controller.Engine.State.AiOptions.Where(x => item.AiIds.Contains(x.Id)).Select(x => new { x.Name, x.Color }),
        Priority = Controller.Engine.State.PriorityOptions.Where(x => x.Id == item.PriorityId).Select(x => new { x.Name, x.Color })
    });
    public TaskCard()
    {
        TaskCardLayout.Frame(this); Cursor = Cursors.Hand;
        DataContextChanged += (_, _) => Build();
        PreviewMouseDown += (_,_) =>
        {
            if (!ArchivedPreview && DataContext is WorkItem { TimerUnread: true } item)
            { item.TimerUnread = false; Controller.Engine.Touch(); Dispatcher.BeginInvoke(new Action(() => Controller.Engine.Notify([item.Id])), System.Windows.Threading.DispatcherPriority.Background); }
        };
        MouseLeftButtonDown += (_, e) => { if (ArchivedPreview || SurfaceWindow.IsInteractive(e.OriginalSource as DependencyObject)) return; start = e.GetPosition(this); pressed = true; CaptureMouse(); e.Handled = true; };
        MouseMove += (_, e) =>
        {
            if (!pressed || e.LeftButton != MouseButtonState.Pressed || DataContext is not WorkItem item) return;
            if ((e.GetPosition(this) - start).Length < 7) return;
            pressed = false; ReleaseMouseCapture();
            var drag = new CardDragSession(Controller, this, item, start); Controller.CardDrag = drag;
            GiveFeedbackEventHandler feedback = (_, args) => { drag.Move(); args.UseDefaultCursors = false; Mouse.SetCursor(Cursors.Arrow); args.Handled = true; };
            GiveFeedback += feedback;
            try { DragDrop.DoDragDrop(this, new DataObject("Taskboard.Task", item.Id), DragDropEffects.Move); }
            finally { GiveFeedback -= feedback; drag.Dispose(); Controller.CardDrag = null; Mouse.SetCursor(null); }
        };
        MouseLeftButtonUp += (_, e) => { var click = pressed && IsMouseOver; pressed = false; ReleaseMouseCapture(); if (click && DataContext is WorkItem item) Controller.ToggleTaskEditor(item.Id); e.Handled = true; };
        LostMouseCapture += (_, _) => pressed = false;
        MouseLeave += (_, e) => { if (e.LeftButton != MouseButtonState.Pressed) pressed = false; };
    }
    private void Build()
    {
        BuildCount++;
        if (collapseScopes > 0) return;
        if (DataContext is not WorkItem item || Controller is null) return;
        presentationKey = PresentationKey(item);
        stageSections.Clear();
        var stack = new StackPanel(); var title = Ui.Label(item.DisplayTitle, 13, Ui.Accent); title.FontWeight = FontWeights.SemiBold;
        title.Tag = "TaskTitle"; title.TextTrimming = TextTrimming.CharacterEllipsis; title.MaxHeight = 42;
        var titleRow = Heading(new MarqueeText(title) { HorizontalAlignment = HorizontalAlignment.Left }, item, item.Stages.Count == 1 ? item.Stages[0] : null);
        stack.Children.Add(TaskCardLayout.Title(titleRow));
        var stages = ArchivedPreview ? BoardEngine.CardStages(item) : Controller.VisibleCardStages(item); var showNames = item.Stages.Count > 1;
        if (stages.Count > 0) stack.Children.Add(new CardSeparator(Ui.Accent, overlay: true) { Tag = "TaskTitleSeparator" });
        CardHeading? firstHeading = null; Border? firstPill = null; TextBlock? firstDetails = null;
        foreach (var stage in stages)
        {
            var section = new StackPanel { Tag = "TaskStageSection", DataContext = stage, Opacity = stage.IsRunning == true ? 1 : .45, ClipToBounds = true };
            if (showNames)
            {
                if (stage != stages[0]) section.Children.Add(new CardSeparator(Ui.Line) { Tag = "TaskStageSeparator" });
                var name = Ui.Pill(stage.Name, "#9CAAC2"); name.HorizontalAlignment = HorizontalAlignment.Left;
                name.Tag = "TaskStageName"; name.ToolTip = stage.Name;
                var label = (TextBlock)name.Child; label.FontSize = 12; label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
                name.Child = null; name.Child = new MarqueeText(label);
                var heading = Heading(name, item, stage); section.Children.Add(heading);
                if (stage == stages[0]) { firstHeading = heading; firstPill = name; }
            }
            var text = Ui.Label(stage.Text, 14, Ui.Text); text.Tag = "TaskStageDetails";
            if (stage == stages[0]) firstDetails = text;
            text.TextTrimming = TextTrimming.CharacterEllipsis; text.MaxHeight = showNames ? 42 : 64; section.Children.Add(text);
            stageSections[stage.Id] = section; stack.Children.Add(section);
        }
        var pills = new WrapPanel();
        foreach (var option in Controller.Engine.State.AiOptions.Where(x => item.AiIds.Contains(x.Id))) pills.Children.Add(Ui.Pill(option.Name, option.Color));
        var priority = Controller.Engine.State.PriorityOptions.FirstOrDefault(x => x.Id == item.PriorityId);
        if (priority is not null) pills.Children.Add(Ui.Pill(priority.Name, priority.Color));
        stack.Children.Add(pills);
        if (item.PendingReasons.Count > 0) stack.Children.Add(Ui.Label(string.Join(" · ", item.PendingReasons), 11, Ui.Brush("#E9C582")));
        Compact(stack); var content = new Grid(); content.Children.Add(stack);
        if (firstHeading is not null && firstPill is not null && firstDetails is not null)
        {
            void MatchGap() => TaskCardLayout.MatchTitleGap(firstHeading, firstPill, firstDetails);
            MatchGap(); content.Loaded += (_, _) => MatchGap();
        }
        if (item.TimerUnread) content.Children.Add(new System.Windows.Shapes.Ellipse { Width=6,Height=6,Fill=Ui.Brush("#F28F98"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false });
        Child = content;
        if (ArchivedPreview)
        {
            var historyMenu = new ContextMenu(); var restore = new MenuItem { Header = "取消归档" };
            restore.Click += (_, _) => Controller.UnarchiveTask(item.Id); historyMenu.Items.Add(restore); ContextMenu = historyMenu; Cursor = Cursors.Arrow; return;
        }
        var menu = new ContextMenu(); var delete = new MenuItem { Header = "删除任务（可撤销）" };
        delete.Click += (_, _) => Controller.DeleteTask(item.Id); menu.Items.Add(delete);
        if (Controller.Engine.CanArchive(item))
        {
            var archive = new MenuItem { Header = "归档" }; archive.Click += (_, _) => Controller.ArchiveTask(item.Id); menu.Items.Add(archive);
        }
        if (item.PendingReasons.Contains("定时提醒到期"))
        {
            var acknowledge = new MenuItem { Header = "已查看提醒" };
            acknowledge.Click += (_, _) => { item.TimerUnread = false; item.PendingReasons.Remove("定时提醒到期"); Controller.Engine.Notify([item.Id]); Build(); };
            menu.Items.Add(acknowledge);
        }
        ContextMenu = menu;
    }
    public void Rebuild()
    {
        if (DataContext is WorkItem item && Controller is not null && presentationKey == PresentationKey(item)) return;
        Build();
    }
    internal void SetMarquees(bool restart)
    {
        Visit(this);
        void Visit(DependencyObject node)
        {
            if (node is MarqueeText text) { if (restart) text.Restart(); else text.Pause(); return; }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Visit(VisualTreeHelper.GetChild(node, i));
        }
    }
    private CardHeading Heading(FrameworkElement label, WorkItem item, Stage? stage)
    {
        var row = new CardHeading(label);
        if (stage is null || item.Lane == Lane.Inspiration) return row;
        {
            var binding = stage.Codex;
            var text = Ui.Label(binding is null ? "未绑定" : string.IsNullOrWhiteSpace(binding.ThreadName) ? "未命名会话" : binding.ThreadName, 11, Ui.Muted);
            text.TextAlignment = TextAlignment.Right;
            var name = new MarqueeText(text, conversation: true) { Tag = "TaskConversationName", Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right, ToolTip = binding is null ? "未绑定" : text.Text + " · " + binding.ThreadId, DataContext = stage };
            row.SetConversation(name);
        }
        if (item.Stages.Count > 1)
        {
            row.SetStateButton(StageButton(item, stage));
        }
        return row;
    }
    private Button StageButton(WorkItem item, Stage stage)
    {
        var running = stage.IsRunning == true;
        var button = Ui.Button("", () => { });
        button.Click += (_, _) =>
        {
            if (!Controller.SetStageRunningByUser(item.Id, stage.Id, stage.IsRunning != true)) { Ui.Hint(button, BoardEngine.LastRunningStageMessage, !Controller.Smoke, this); return; }
            Controller.Persist();
        };
        button.Tag = "TaskStageRun"; button.DataContext = stage; button.Content = new StageRunIcon(running, !ArchivedPreview);
        button.Width = button.Height = 24; button.Padding = new Thickness(0); button.Margin = new Thickness(3, 0, 0, 0);
        button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.IsEnabled = !ArchivedPreview;
        button.ToolTip = ArchivedPreview ? "阶段状态（归档预览）" : running ? "运行中，点击改为未运行" : "未运行，点击改为运行中";
        System.Windows.Automation.AutomationProperties.SetName(button, stage.Name + "：" + (running ? "运行中" : "未运行"));
        return button;
    }
    private static void Compact(DependencyObject root)
    {
        if (root is TextBlock text) text.Margin = new Thickness(text.Margin.Left, text.Margin.Top * .6, text.Margin.Right, text.Margin.Bottom * .6);
        if (root is Border pill && pill.Child is TextBlock or MarqueeText) pill.Margin = new Thickness(pill.Margin.Left, pill.Margin.Top * .6, pill.Margin.Right, pill.Margin.Bottom * .6);
        // Newly constructed card visuals only; no shared editor styles change.
        if (root is Panel panel) foreach (UIElement child in panel.Children) Compact(child);
        else if (root is Decorator decorator && decorator.Child is not null) Compact(decorator.Child);
    }
    public void Flash()
    {
        StopFlash(); var generation = ++flashGeneration;
        var brush = new SolidColorBrush(Colors.Transparent); BorderBrush = brush;
        var animation = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(800) };
        animation.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.IndianRed, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.Transparent, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        animation.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.IndianRed, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400))));
        animation.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.Transparent, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));
        animation.Completed += (_, _) => { if (generation == flashGeneration) BorderBrush = Ui.Line; }; brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }
    public void StopFlash() { flashGeneration++; if (BorderBrush is SolidColorBrush brush && !brush.IsFrozen) brush.BeginAnimation(SolidColorBrush.ColorProperty, null); BorderBrush = Ui.Line; }
}
