using System;
using System.Windows;
using System.Windows.Media;

namespace Taskboard.Desktop;

internal static class DraftCardGeometry
{
    internal const double Radius = 20;
    internal static Geometry VisibleCard(double width, double height, double previousOffset)
    {
        var full = new RectangleGeometry(new Rect(0, 0, width, height), Radius, Radius);
        if (previousOffset <= 0) return full;
        // Paint only pixels outside the preceding rounded card. The small
        // corner gaps are included; the middle never has two glass tints.
        return new CombinedGeometry(GeometryCombineMode.Exclude, full,
            new RectangleGeometry(new Rect(-previousOffset, 0, width, height), Radius, Radius));
    }
    internal static double EntryShift(double pane) => Math.Min(Radius, Math.Max(0, pane / 5 - 16));
}
