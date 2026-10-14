using System;
using System.Windows;

namespace Taskboard.Desktop;
internal static class HoverCorridor
{
    internal static bool Contains(Rect ball, Rect popup, Point point)
    {
        if (ball.Contains(point) || popup.Contains(point)) return true;
        var left = Math.Max(ball.Left,popup.Left)-4; var right = Math.Min(ball.Right,popup.Right)+4;
        if (right <= left) return false;
        var top = Math.Min(ball.Bottom,popup.Bottom); var bottom = Math.Max(ball.Top,popup.Top);
        return bottom >= top && new Rect(left,top,right-left,bottom-top).Contains(point);
    }
}
