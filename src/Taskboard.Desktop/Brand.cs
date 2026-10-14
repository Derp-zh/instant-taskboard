using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Taskboard.Desktop;
internal static class Brand
{
    internal const string Name = "即时任务看板";
    internal static Viewbox OrbGlyph()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Path { Data = Geometry.Parse("M 12,1 L 23,12 12,23 1,12 Z M 12,6 L 18,12 12,18 6,12 Z"),
            Stroke = Ui.Accent, StrokeThickness = 1.7, StrokeLineJoin = PenLineJoin.Round });
        return new Viewbox { Child = canvas, Width = 19, Height = 19, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
    }
    internal static System.Drawing.Icon TrayIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("/Taskboard;component/Assets/orb.ico",UriKind.Relative))!.Stream;
        return new System.Drawing.Icon(stream);
    }
}
