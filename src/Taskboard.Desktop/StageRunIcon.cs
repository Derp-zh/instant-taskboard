using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Taskboard.Desktop;

internal sealed class StageRunIcon : Viewbox
{
    internal double Angle => 0;
    internal StageRunIcon(bool running, bool animate = true)
    {
        Width = Height = 14; IsHitTestVisible = false;
        var canvas = new Canvas { Width = 14, Height = 14 };
        if (running)
        {
            foreach (var left in new[] { 3.0, 8.0 })
            {
                var bar = new Rectangle { Width = 3, Height = 10, Fill = Ui.Accent };
                Canvas.SetLeft(bar, left); Canvas.SetTop(bar, 2); canvas.Children.Add(bar);
            }
        }
        else
        {
            var geometry = Geometry.Parse("M 3.5,1.5 L 12,7 L 3.5,12.5 Z"); geometry.Freeze();
            canvas.Children.Add(new Path { Data = geometry, Fill = Ui.Muted });
        }
        Child = canvas;
    }
    internal void FreezeAt(double angle) { }
    internal static StackPanel Caption(bool running, bool animate = true)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new StageRunIcon(running) { Opacity = running ? 1 : .5, Margin = new Thickness(0, 0, 6, 0) });
        panel.Children.Add(new TextBlock { Text = running ? "运行中" : "未运行", VerticalAlignment = VerticalAlignment.Center }); return panel;
    }
}