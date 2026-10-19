using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BallSurface : SurfaceWindow
{
    protected override bool ScrollEnabled => false;
    private readonly BoardController controller;
    private readonly Border orb;
    private readonly TextBlock badge;
    private readonly Border countBubble;
    internal Border CountBubble => countBubble;
    internal string CountText => badge.Text;
    private readonly Grid expanded;
    private readonly TextBox search;
    private readonly DispatcherTimer hover;
    private readonly DispatcherTimer searchDelay;
    private readonly DispatcherTimer leaveDelay;
    private readonly SurfaceWindow results;
    private readonly SurfaceWindow preview;
    private sealed record SearchEntry(Guid Id, string Caption);
    private readonly ListBox resultItems = new();
    private readonly StackPanel previewItems = new();
    private Point rightStart;
    private DateTime rightPressed;
    private bool rightDown;
    private bool dragging;
    private double originalLeft;
    private double originalTop;
    internal override double GlassTransparency => controller.Engine.State.Preferences.BallTransparency;
    private readonly System.Windows.Shapes.Shape orbFill;
    private readonly System.Windows.Shapes.Path capsuleFill;
    private readonly System.Windows.Shapes.Path orbStroke;
    internal bool IsEdgeDocked => controller is not null && controller.Engine.State.Preferences.BallEdgeAutoHide;
    internal override System.Collections.Generic.IEnumerable<GlassRegion> GlassRegions() =>
        [new(new Rect(Width - 370, 0, 370, Height), Radius: GlassRadius, FlatRight: IsEdgeDocked)];
    protected override void ApplyGlassTint()
    {
        Root.Background = Brushes.Transparent;
        if (orbFill is not null) orbFill.Fill = Ui.Brush($"#{(int)Math.Round(255*(1-GlassTransparency/100)):X2}192333");
        if (capsuleFill is not null && orbFill is not null) capsuleFill.Fill = orbFill.Fill;
        if (orb is not null) ApplyDockShape();
    }
    public BallSurface(BoardController controller) : base(58, 58)
    {
        this.controller = controller;
        WindowLayer = 3;
        Title = "即时任务看板入口";
        orbFill = new System.Windows.Shapes.Path { Width=58,Height=58,HorizontalAlignment=HorizontalAlignment.Right,IsHitTestVisible = false, Fill = Ui.Brush("#99192333") };
        capsuleFill = new System.Windows.Shapes.Path { IsHitTestVisible=false,Fill=orbFill.Fill,Visibility=Visibility.Collapsed };
        Root.Children.Add(orbFill);
        Root.Children.Add(capsuleFill);
        expanded = new Grid { Visibility = Visibility.Collapsed, Width = 306, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4, 3, 60, 3) };
        expanded.ColumnDefinitions.Add(new ColumnDefinition()); expanded.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        expanded.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        search = Ui.Input(); search.Margin = new Thickness(4, 7, 0, 7); search.ToolTip = "搜索任务名称与所有阶段详情";
        expanded.Children.Add(search);
        var clear = Ui.Button("×", () => search.Clear()); clear.Cursor = Cursors.Arrow; clear.Padding = new Thickness(0); Grid.SetColumn(clear, 1); expanded.Children.Add(clear);
        var settings = Ui.Button("设置", controller.ToggleSettings); settings.Cursor = Cursors.Arrow; settings.Padding = new Thickness(3); Grid.SetColumn(settings, 2); expanded.Children.Add(settings);
        Root.Children.Add(new Border { Child = expanded, CornerRadius = new CornerRadius(27), Background = Brushes.Transparent });
        var orbStack = new Grid(); orbStroke = new System.Windows.Shapes.Path { Stroke=Ui.Line,StrokeThickness=1,IsHitTestVisible=false, Margin = new Thickness(.5) };
        orbStack.Children.Add(orbStroke); orbStack.Children.Add(Brand.OrbGlyph());
        badge = new TextBlock { Text = "", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text, TextWrapping = TextWrapping.NoWrap };
        countBubble = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Padding = new Thickness(2),
            Background = Ui.Brush("#334258"), BorderBrush = Ui.Brush("#F28F98"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 8, 0),
            Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = badge } };
        orbStack.Children.Add(countBubble);
        orb = new Border { Child = orbStack, Width = 54, Height = 54, CornerRadius = new CornerRadius(27), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(2), Cursor = Cursors.Arrow };
        Root.Children.Add(orb);
        ApplyDockShape();
        SizeChanged += (_, _) => { ApplyDockShape(); expanded.Opacity = Width > 64 ? 1 : 0; };
        InitializeEdgeAutoHide();
        orb.MouseLeftButtonUp += (_, e) => { controller.Toggle(); e.Handled = true; };
        orb.MouseRightButtonDown += (_, e) =>
        {
            rightDown = true; dragging = false; rightPressed = DateTime.UtcNow;
            rightStart = PointToScreen(e.GetPosition(this)); originalLeft = Left; originalTop = Top; orb.CaptureMouse(); e.Handled = true;
        };
        orb.MouseMove += (_, e) =>
        {
            if (!rightDown || e.RightButton != MouseButtonState.Pressed) return;
            var screen = PointToScreen(e.GetPosition(this)); var delta = screen - rightStart;
            if (!dragging && DateTime.UtcNow - rightPressed >= TimeSpan.FromMilliseconds(300) && delta.Length >= 5) dragging = true;
            if (dragging)
            {
                var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                var dip = transform.Transform(delta); MoveDraggedBall(originalLeft + dip.X, originalTop + dip.Y);
                HidePopups();
            }
            e.Handled = true;
        };
        orb.MouseRightButtonUp += (_, e) =>
        {
            rightDown = false; orb.ReleaseMouseCapture();
            if (dragging) { dragging = false; controller.Clamp(this); if (IsEdgeDocked) DockAtRight(); controller.SaveBall(this); Native.RaiseBall(); }
            else { HidePopups(); controller.OrbMenu(orb); } e.Handled = true;
            ResetEdgeIdle();
        };
        resultItems.Background = Brushes.Transparent; resultItems.BorderThickness = new Thickness(0); resultItems.Foreground = Ui.Text;
        resultItems.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VirtualizingPanel.SetIsVirtualizing(resultItems, true); VirtualizingPanel.SetVirtualizationMode(resultItems, VirtualizationMode.Recycling);
        ScrollViewer.SetHorizontalScrollBarVisibility(resultItems, ScrollBarVisibility.Disabled);
        var itemText = new FrameworkElementFactory(typeof(TextBlock)); itemText.SetBinding(TextBlock.TextProperty, new Binding(nameof(SearchEntry.Caption)));
        itemText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); itemText.SetValue(TextBlock.MarginProperty, new Thickness(7, 9, 7, 9));
        resultItems.ItemTemplate = new DataTemplate { VisualTree = itemText };
        results = new SurfaceWindow(340, 330) { FloatingAnimations = false }; results.Root.Children.Add(resultItems); preview = MakePopup(previewItems, 198, 340);
        controller.RegisterAuxiliary(results);
        resultItems.SelectionChanged += (_, _) => { if (resultItems.SelectedItem is SearchEntry selected && selected.Id != Guid.Empty) { results.Hide(); controller.Locate(selected.Id); } };
        hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        hover.Tick += (_, _) => { hover.Stop(); if (!controller.Expanded && orb.IsMouseOver && !rightDown && !edgeMenuOpen) ShowPreview(); };
        leaveDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
        leaveDelay.Tick += (_, _) => { if (!preview.IsVisible || !PointerInPreviewZone()) { leaveDelay.Stop(); preview.Hide(); } };
        orb.MouseEnter += (_, _) => { if (!controller.Expanded) { hover.Stop(); hover.Start(); } };
        orb.MouseLeave += (_, _) => { hover.Stop(); leaveDelay.Stop(); leaveDelay.Start(); };
        preview.MouseEnter += (_, _) => leaveDelay.Start(); preview.MouseLeave += (_, _) => leaveDelay.Start();
        searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        search.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); ShowResults(); };
        search.GotKeyboardFocus += (_, _) => ShowResults();
        Closed += (_, _) => { hover.Stop(); searchDelay.Stop(); leaveDelay.Stop(); results.Destroy(); preview.Destroy(); };
        UseFixedBallCanvas();
    }
    private static SurfaceWindow MakePopup(StackPanel children, double width, double height)
    {
        var window = new SurfaceWindow(width, height) { FloatingAnimations = false }; window.Root.Children.Add(new ScrollViewer { Content = children,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) }); return window;
    }
    public void SetExpanded(bool expandedState)
    {
        RestoreFromEdge(); ResetEdgeIdle();
        HidePopups();
        if (IsEdgeDocked) DockAtRight();
        else if (expandedState && !controller.Smoke)
        {
            var area = controller.WorkArea(this);
            Left = Math.Clamp(Left + Width, area.Left + Math.Min(370, area.Width), area.Right) - Width;
        }
        var right = Left + Width;
        if (!expandedState && Width <= 58 && !HasActiveMotion) { expanded.Visibility = Visibility.Collapsed; capsuleFill.Visibility=Visibility.Collapsed; orbFill.Visibility=Visibility.Visible; return; }
        RevealToLeft(expandedState, () =>
        {
            capsuleFill.Visibility = Visibility.Visible; orbFill.Visibility = Visibility.Collapsed;
            if (expandedState) { expanded.Visibility = Visibility.Visible; expanded.Opacity = Width > 64 ? 1 : 0; }
        }, () =>
        {
            if (!expandedState) { Width = 58; Left = right - Width; expanded.Visibility = Visibility.Collapsed; controller.Clamp(this); if (IsEdgeDocked) DockAtRight(); }
            if (expandedState) { controller.Clamp(this); if (IsEdgeDocked) DockAtRight(); }
            capsuleFill.Visibility = expandedState ? Visibility.Visible : Visibility.Collapsed; orbFill.Visibility = expandedState ? Visibility.Collapsed : Visibility.Visible;
            UpdateBadge();
        });
        UpdateBadge();
    }
    internal void ClickDuringAnimation() => controller.Toggle();
    internal bool ContainsOrbPoint(Point point) => point.X >= Width - 58 && point.X <= Width && point.Y >= 0 && point.Y <= 58 &&
        (IsEdgeDocked && point.X >= Width - 29 || Math.Pow(point.X - (Width - 29), 2) + Math.Pow(point.Y - 29, 2) <= 29 * 29);
    internal void MoveDraggedBall(double left, double top)
    {
        Top = top;
        if (IsEdgeDocked) DockAtRight(); else Left = left;
    }
    private void ApplyDockShape()
    {
        ((System.Windows.Shapes.Path)orbFill).Data = BallGeometry(58, 58, IsEdgeDocked);
        capsuleFill.Data = BallGeometry(370, Height, IsEdgeDocked);
        if (edgeDot is not null) edgeDot.Margin = new Thickness(370 - Width + 4, 0, 0, 0);
        orb.Width = orb.Height = IsEdgeDocked ? 58 : 54;
        orb.Margin = new Thickness(IsEdgeDocked ? 0 : 2);
        orb.CornerRadius = IsEdgeDocked ? new CornerRadius(29, 0, 0, 29) : new CornerRadius(27);
        orb.Tag = "BallClickTarget";
        orbStroke.Data = BallGeometry(orb.Width - 1, orb.Height - 1, IsEdgeDocked);
    }
    internal IInputElement? HitTestOrb(Point point) => Root.InputHitTest(new Point(point.X + 370 - Width, point.Y));
    internal Point OrbPointToScreen(Point point) => Root.PointToScreen(new Point(point.X + 370 - Width, point.Y));
    internal static Geometry BallGeometry(double width, double height, bool flatRight)
    {
        var radius = height / 2;
        return flatRight ? new CombinedGeometry(GeometryCombineMode.Intersect,
            new RectangleGeometry(new Rect(0, 0, width + radius, height), radius, radius),
            new RectangleGeometry(new Rect(0, 0, width, height))) : new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
    }
    public void UpdateBadge()
    {
        var action = controller.Engine.State.Preferences.Columns.Single(x => x.BuiltinLane == Lane.Action);
        var count = controller.Engine.State.Tasks.Count(action.Contains);
        badge.Text = count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        countBubble.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEdgeBadge(count);
    }
    public void RefreshData() { UpdateBadge(); if (results.IsVisible) ShowResults(); if (preview.IsVisible) ShowPreview(); }
    public void HidePopups() { hover.Stop(); results.Hide(); preview.Hide(); }
    private void PopupPlace(SurfaceWindow window)
    {
        var area = controller.WorkArea(this); window.Left = Math.Clamp(Left + Width - window.Width, area.Left, Math.Max(area.Left, area.Right - window.Width));
        var available = Top - area.Top - 8;
        if (available >= 120) { window.Height = Math.Min(340, available); window.Top = Top - window.Height - 8; }
        else { window.Height = Math.Min(340, Math.Max(120, area.Bottom - Top - Height - 8)); window.Top = Top + Height + 8; }
        controller.Clamp(window); window.Show(); Native.RaiseBall();
    }
    private void ShowResults()
    {
        if (!controller.Expanded || string.IsNullOrWhiteSpace(search.Text)) { results.Hide(); return; }
        preview.Hide(); resultItems.ItemsSource = controller.Engine.Search(search.Text).Select(task => new SearchEntry(task.Id, task.DisplayTitle + " · " + BoardColumns.For(controller.Engine.State.Preferences,task).Name)).ToArray();
        if (resultItems.Items.Count == 0) resultItems.ItemsSource = new[] { new SearchEntry(Guid.Empty, "没有匹配的任务") };
        PopupPlace(results);
    }
    private void ShowPreview()
    {
        results.Hide(); previewItems.Children.Clear();
        var tasks = controller.Engine.State.Tasks.Where(x => x.ColumnId is null && x.Lane == Lane.Action).ToArray();
        if (tasks.Length == 0) previewItems.Children.Add(Ui.Label("暂无待操作任务", 12, Ui.Muted));
        foreach (var task in tasks)
        {
            var body = new StackPanel(); var title = Ui.Label(task.DisplayTitle, 13); title.FontWeight = FontWeights.SemiBold; body.Children.Add(title);
            var detail = Ui.Label(string.Join("\n", BoardEngine.CardStages(task).Select(x => x.Text)), 12, Ui.Muted); detail.MaxHeight = 70; detail.TextTrimming = TextTrimming.CharacterEllipsis; body.Children.Add(detail);
            var tags = new WrapPanel();
            foreach (var ai in controller.Engine.State.AiOptions.Where(x => task.AiIds.Contains(x.Id))) tags.Children.Add(Ui.Pill(ai.Name, ai.Color));
            var priority = controller.Engine.State.PriorityOptions.FirstOrDefault(x => x.Id == task.PriorityId);
            if (priority is not null) tags.Children.Add(Ui.Pill(priority.Name, priority.Color)); body.Children.Add(tags);
            var button = Ui.Button("", () => { preview.Hide(); controller.Locate(task.Id); }); button.Content = body; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            previewItems.Children.Add(button);
        }
        PopupPlace(preview);
        leaveDelay.Start();
    }
    internal bool PointerInPreviewZone()
    {
        var pointer = System.Windows.Forms.Cursor.Position;
        var ball = Native.ScreenBounds(this); var popup = Native.ScreenBounds(preview);
        return HoverCorridor.Contains(ball,popup,new Point(pointer.X,pointer.Y));
    }
    internal SurfaceWindow PreviewForVerification() { ShowPreview(); return preview; }
    public bool Owns(IntPtr handle) => new System.Windows.Interop.WindowInteropHelper(results).Handle == Native.GetAncestor(handle, 2) || new System.Windows.Interop.WindowInteropHelper(preview).Handle == Native.GetAncestor(handle, 2);
}
