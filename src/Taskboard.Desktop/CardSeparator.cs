using System;
using System.Windows;
using System.Windows.Media;

namespace Taskboard.Desktop;

// Snap both colors to one physical pixel, even when compact margins land
// between pixels. A title separator overlays its existing gap (zero height).
internal sealed class CardSeparator : FrameworkElement
{
    private readonly Brush color;
    internal CardSeparator(Brush color, bool overlay = false)
    {
        this.color = color; IsHitTestVisible = false; Height = overlay ? 0 : 1;
        if (!overlay) Margin = new Thickness(0, 3.6, 0, 2.4);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var dpi = VisualTreeHelper.GetDpi(this); var position = new Point();
        if (PresentationSource.FromVisual(this) is not null) position = PointToScreen(new Point());
        var y = (Math.Round(position.Y) - position.Y) / dpi.DpiScaleY;
        var x = (Math.Round(position.X) - position.X) / dpi.DpiScaleX;
        var width = Math.Max(0, Math.Round(ActualWidth * dpi.DpiScaleX) / dpi.DpiScaleX);
        dc.DrawRectangle(color, null, new Rect(x, y, width, 1 / dpi.DpiScaleY));
    }
}
