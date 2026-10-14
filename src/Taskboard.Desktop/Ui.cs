using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

internal static class Ui
{
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
        var button = Button(text, click); button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); grid.Children.Add(button); return grid;
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
