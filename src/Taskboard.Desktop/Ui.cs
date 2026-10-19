using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

internal static class Ui
{
    private static ToolTip? activeHint;
    internal static ToolTip? ActiveHint => activeHint;
    internal static int HintCount { get; private set; }
    internal static string? LastHint { get; private set; }
    internal static readonly TimeSpan HintDuration = TimeSpan.FromMilliseconds(1200);
    internal static ControlTemplate HintTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        return new ControlTemplate(typeof(ToolTip)) { VisualTree = presenter };
    }
    internal static void Hint(FrameworkElement anchor, string message, bool show = true, FrameworkElement? stableRoot = null)
    {
        HintCount++; LastHint = message;
        if (!show) return;
        var bubble = CreateHint(anchor, message, stableRoot);
        if (bubble is not null) OpenHint(bubble);
    }
    internal static ToolTip? CreateHint(FrameworkElement anchor, string message, FrameworkElement? stableRoot = null, Point? screenPosition = null)
    {
        // Our controls belong to a child HwndSource, not a WPF Window.
        // Window.GetWindow(anchor) is null; WindowInteropHelper(null) throws
        // and the dispatcher replaces this hint with an error window.
        if (PresentationSource.FromVisual(anchor) is not System.Windows.Interop.HwndSource source || source.Handle == IntPtr.Zero) return null;
        var target = stableRoot ?? anchor;
        if (!ReferenceEquals(PresentationSource.FromVisual(target), source)) return null;
        var area = System.Windows.Forms.Screen.FromHandle(source.Handle).WorkingArea;
        var position = screenPosition ?? new Point(area.Left + area.Width / 2.0, area.Top + area.Height * .62);
        var center = target.PointFromScreen(position);
        return new ToolTip { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
            CustomPopupPlacementCallback = (size, _, _) => [new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(center.X - size.Width / 2, center.Y), System.Windows.Controls.Primitives.PopupPrimaryAxis.None)],
            Template = HintTemplate(), HasDropShadow = false, Tag = position,
            StaysOpen = true, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Content = new Border { CornerRadius = new CornerRadius(12), Background = Brush("#F0243044"), BorderBrush = Line, BorderThickness = new Thickness(1),
                Padding = new Thickness(11, 8, 11, 8), Child = new TextBlock { Text = message, Foreground = Text, FontSize = 12 } } };
    }
    internal static void OpenHint(ToolTip bubble)
    {
        if (activeHint is not null) activeHint.IsOpen = false;
        var target = (FrameworkElement)bubble.PlacementTarget;
        // System tooltip fade otherwise adds time after the requested timeout.
        bubble.Opened += (_, _) =>
        {
            if (LogicalTreeHelper.GetParent(bubble) is System.Windows.Controls.Primitives.Popup popup) popup.PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.None;
            if (bubble.Tag is Point position) HintScreenPin.Attach(bubble, position);
        };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = HintDuration };
        RoutedEventHandler unloaded = (_, _) => bubble.IsOpen = false;
        DependencyPropertyChangedEventHandler visible = (_, _) => { if (!target.IsVisible) bubble.IsOpen = false; };
        target.Unloaded += unloaded; target.IsVisibleChanged += visible;
        bubble.Closed += (_, _) =>
        {
            timer.Stop(); target.Unloaded -= unloaded; target.IsVisibleChanged -= visible;
            if (ReferenceEquals(activeHint, bubble)) activeHint = null;
        };
        timer.Tick += (_, _) => bubble.IsOpen = false;
        activeHint = bubble; bubble.IsOpen = true; timer.Start();
    }
    public static readonly Brush Text = Brush("#EBEEF6");
    public static readonly Brush Muted = Brush("#9CAAC2");
    public static readonly Brush Surface = Brush("#192333");
    public static readonly Brush Card = Brush("#C0243044");
    public static readonly Brush Line = Brush("#3A485E");
    public static readonly Brush Accent = Brush("#96DACC");
    public static Brush Brush(string color)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b;
    }
    public static TextBlock Label(string text, double size = 13, Brush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    public static Button Button(string text, Action click, bool accent = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(3), Background = accent ? Accent : Card,
            Foreground = accent ? Brush("#102A27") : Text, BorderBrush = Line, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
        button.Click += (_, _) => click(); return button;
    }
    public static TextBox Input(string text = "", bool multiline = false) => new()
    { Text = text, Foreground = Text, Background = Brush("#121D2D"), BorderBrush = Line, Padding = new Thickness(9), Margin = new Thickness(0, 4, 0, 8),
        FontSize = 13, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        MinHeight = multiline ? 80 : 34, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden };
    public static Grid DashedButton(string text, Action click)
    {
        var grid = new Grid { Margin = new Thickness(3), MinHeight = 38 };
        grid.Children.Add(new System.Windows.Shapes.Rectangle { RadiusX = 20, RadiusY = 20, Stroke = Muted, StrokeThickness = 1, StrokeDashArray = new DoubleCollection([4, 4]), IsHitTestVisible = false });
        var button = Button(text, click); button.Tag = "NewTaskEntry"; button.Margin = new Thickness(0);
        // Layered child windows pass fully transparent pixels to the desktop.
        // Keep an imperceptible alpha across the whole outlined hit target.
        button.Background = Brush("#01192333"); button.BorderThickness = new Thickness(0); grid.Children.Add(button); return grid;
    }
    public static Border Panel(UIElement child, Brush? background = null, double padding = 12) => new()
    { Child = child, Background = background ?? Card, Padding = new Thickness(padding), CornerRadius = new CornerRadius(20),
        BorderBrush = Line, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
    public static Border Pill(string name, string color) => new()
    { Child = Label(name, 11, Brush(color)), Background = Brush("#13202F"), BorderBrush = Brush(color), BorderThickness = new Thickness(1),
        Padding = new Thickness(7, 0, 7, 0), Margin = new Thickness(0, 3, 5, 0), CornerRadius = new CornerRadius(12) };
    public static Action<SurfaceWindow>? WindowCreated { get; set; }
    public static void Error(string text)
    {
        var window = MessageSurface(text); var body = (StackPanel)((ScrollViewer)window.Root.Children[0]).Content;
        body.Children.Add(Button("知道了", window.Destroy)); window.Show(); window.Activate(); Native.RaiseBall();
    }
    public static Task<bool> Confirm(string text, string accept = "确认导入")
    {
        var completion = new TaskCompletionSource<bool>(); var window = MessageSurface(text);
        var body = (StackPanel)((ScrollViewer)window.Root.Children[0]).Content;
        body.Children.Add(Button(accept, () => { completion.TrySetResult(true); window.Destroy(); }, true));
        body.Children.Add(Button("取消", window.Destroy)); window.Closed += (_, _) => completion.TrySetResult(false);
        window.Show(); window.Activate(); Native.RaiseBall(); return completion.Task;
    }
    private static SurfaceWindow MessageSurface(string text)
    {
        var window = new SurfaceWindow(400, 230) { Title = "即时任务看板提示" }; var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(Label(text)); window.Root.Children.Add(new ScrollViewer { Content = body });
        var area = SystemParameters.WorkArea; window.Left = area.Left + (area.Width - window.Width) / 2; window.Top = area.Top + (area.Height - window.Height) / 2;
        WindowCreated?.Invoke(window); return window;
    }
}
