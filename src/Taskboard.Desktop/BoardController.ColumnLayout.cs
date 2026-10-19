using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Taskboard.Core;
using Forms = System.Windows.Forms;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    // Saved independently of manual per-column moves/resizes, using the
    // existing validated configuration position format for old-data compatibility.
    private const string ColumnDefaultPrefix = "layout-default:";
    private WindowPosition[] scaleDefaultStart = [];
    private double workspaceResizeFactor = 1;
    private Dictionary<string, WindowPosition> DefaultColumnPositions()
    {
        var area = WorkArea(Ball);
        var monitor = Forms.Screen.FromHandle(new WindowInteropHelper(Ball).Handle).Bounds;
        var scale = 96.0 / Math.Max(96, Native.GetDpiForWindow(new WindowInteropHelper(Ball).Handle));
        var totalWidth = Math.Min(area.Width - 24, monitor.Width * scale * 7 / 8);
        var visible = VisibleColumns; var count = Math.Max(1, visible.Length);
        var gap = count == 1 ? 0 : Math.Min(Math.Min(24, Math.Max(12, area.Width / 50)), Math.Max(0, (totalWidth - count * 80) / (count - 1.0)));
        var width = Math.Max(80, (totalWidth - (count - 1) * gap) / count);
        var height = Math.Max(260, (area.Height - 90) * .87);
        var left = area.Left + (area.Width - width * count - (count - 1) * gap) / 2;
        var top = area.Top + (area.Height - height) / 2;
        return visible.Select((column, i) => (column.Definition.PositionKey, Position: new WindowPosition { X = left + i * (width + gap), Y = top, Width = width, Height = height }))
            .ToDictionary(x => x.PositionKey, x => x.Position);
    }
    private void EnsureColumnDefaults(Dictionary<string, WindowPosition> defaults)
    {
        if (defaults.Count == 0) return;
        var positions = Engine.State.Preferences.Positions;
        // Older versions did not store the group baseline. Estimate its common
        // scale from median saved sizes; a single manually resized column does
        // not dictate every other column's reset size.
        var factor = 1.0;
        if (!positions.Keys.Any(x => x.StartsWith(ColumnDefaultPrefix, StringComparison.Ordinal)))
        {
            var ratios = defaults.Where(x => positions.ContainsKey(x.Key)).SelectMany(x => new[] { positions[x.Key].Width / x.Value.Width, positions[x.Key].Height / x.Value.Height }).OrderBy(x => x).ToArray();
            if (ratios.Length > 0) factor = (ratios[(ratios.Length - 1) / 2] + ratios[ratios.Length / 2]) / 2;
        }
        var area = WorkArea(Ball); var bounds = Bounds(defaults.Values);
        factor = Math.Clamp(factor, Math.Max(80 / defaults.Values.Min(x => x.Width), 260 / defaults.Values.Min(x => x.Height)), Math.Max(1, Math.Min(area.Width / bounds.Width, area.Height / bounds.Height)));
        foreach (var (key, value) in defaults)
            positions.TryAdd(ColumnDefaultPrefix + key, ScaledDefault(value, bounds, area, factor));
    }
    private static Rect Bounds(IEnumerable<WindowPosition> positions)
    {
        var values = positions.ToArray();
        return new Rect(values.Min(x => x.X), values.Min(x => x.Y), values.Max(x => x.X + x.Width) - values.Min(x => x.X), values.Max(x => x.Y + x.Height) - values.Min(x => x.Y));
    }
    private WindowPosition ScaledDefault(WindowPosition position, Rect bounds, Rect area, double factor)
    {
        var dpi = Math.Max(96, Native.GetDpiForWindow(new WindowInteropHelper(Ball).Handle)) / 96.0;
        double Pixel(double value) => Math.Round(value * dpi) / dpi;
        return new WindowPosition
        {
            X = Pixel(area.Left + (area.Width - bounds.Width * factor) / 2 + (position.X - bounds.Left) * factor),
            Y = Pixel(area.Top + (area.Height - bounds.Height * factor) / 2 + (position.Y - bounds.Top) * factor),
            Width = Pixel(position.Width * factor), Height = Pixel(position.Height * factor)
        };
    }
    public void RestoreColumnDefault(ColumnSurface column)
    {
        EnsureColumnDefaults(DefaultColumnPositions());
        if (!Engine.State.Preferences.Positions.TryGetValue(ColumnDefaultPrefix + column.Definition.PositionKey, out var position)) return;
        column.Place(Copies.Of(position)); Clamp(column);
        SavePosition(column.Definition.PositionKey, column);
    }
}
