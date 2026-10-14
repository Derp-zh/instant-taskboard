using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

// One top-level HWND owns all draft views, shaped live backdrop regions, and
// the clear outline. Moving never hides or captures this window.
internal sealed class DraftStackSurface : SurfaceWindow
{
    private readonly BoardController controller;
    private readonly Canvas canvas = new();
    private readonly List<Border> tints = new();
    private readonly List<GlassRegion> regions = new();
    private Rect lastBounds;
    internal int DraftCount { get; private set; }
    public DraftStackSurface(BoardController controller) : base(516, 650)
    {
        this.controller = controller; WindowLayer = 1; Title = "新建任务";
        Root.Background = Ui.Brush("#01000000"); Root.Children.Add(canvas);
    }
    public void Layout(IReadOnlyList<EditorSurface> visible, Rect[] bounds, int page, int pages)
    {
        if (visible.Count == 0) { Hide(); return; }
        var first = bounds[0]; var outline = bounds[^1];
        var geometry = new Rect(first.Left, first.Top, outline.Right - first.Left, first.Height);
        foreach (var old in canvas.Children.OfType<Border>())
            if (old.Child is Grid grid)
                foreach (var view in grid.Children.OfType<EditorSurface>().ToArray()) grid.Children.Remove(view);
        canvas.Children.Clear(); tints.Clear(); regions.Clear(); DraftCount = visible.Count;
        if (geometry != lastBounds)
        {
            Width = geometry.Width; Height = geometry.Height; Left = geometry.Left; Top = geometry.Top;
            lastBounds = geometry;
        }
        var dashed = new Grid { Width = first.Width, Height = first.Height, Background = Ui.Brush("#01000000") };
        dashed.Clip = new RectangleGeometry(new Rect(first.Width * 4 / 5, 0, first.Width / 5, first.Height));
        dashed.Children.Add(new Rectangle { Margin = new Thickness(2), RadiusX = 20, RadiusY = 20, Stroke = Ui.Muted, StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection([5, 5]), IsHitTestVisible = false });
        var strip = new StackPanel { Width = first.Width / 5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var add = Ui.Button("＋", () => controller.NewTask(visible[0].Session.Working.Lane,visible[0].Session.Working.ColumnId)); add.FontSize = 24; add.Background = Brushes.Transparent; add.BorderThickness = new Thickness(0);
        add.HorizontalContentAlignment = HorizontalAlignment.Center; add.Padding = new Thickness(0, 7, 0, 7);
        var addCaption = Ui.Label("新建任务", 12); addCaption.TextAlignment = TextAlignment.Center;
        strip.Children.Add(add); strip.Children.Add(addCaption);
        if (pages > 1)
        {
            var previous = Ui.Button("‹", () => controller.ChangeDraftPage(-1)); previous.IsEnabled = page > 0;
            var next = Ui.Button("›", () => controller.ChangeDraftPage(1)); next.IsEnabled = page + 1 < pages;
            strip.Children.Add(previous); strip.Children.Add(Ui.Label($"{page + 1}/{pages}", 11)); strip.Children.Add(next);
        }
        dashed.Children.Add(strip); dashed.MouseLeftButtonUp += (_, e) => { controller.NewTask(visible[0].Session.Working.Lane,visible[0].Session.Working.ColumnId); e.Handled = true; };
        Canvas.SetLeft(dashed, outline.Left - first.Left); canvas.Children.Add(dashed);
        for (var i = visible.Count - 1; i >= 0; i--)
        {
            var view = visible[i]; var x = bounds[i].Left - first.Left;
            view.Width = first.Width; view.Height = first.Height; view.Host = this;
            var shade = new Border { Background = Ui.Surface, Opacity = 1 - Transparency / 100, IsHitTestVisible = false };
            tints.Add(shade); var inner = new Grid(); inner.Children.Add(shade); inner.Children.Add(view);
            var frame = new Border { Width = first.Width, Height = first.Height, CornerRadius = new CornerRadius(20), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = inner };
            var full = new RectangleGeometry(new Rect(0, 0, first.Width, first.Height), 20, 20);
            var visibleFrom = i == 0 ? 0 : first.Width - (bounds[i].Left - bounds[i - 1].Left);
            frame.Clip = visibleFrom == 0 ? full : new CombinedGeometry(GeometryCombineMode.Intersect, full, new RectangleGeometry(new Rect(visibleFrom, 0, first.Width - visibleFrom, first.Height)));
            regions.Add(new GlassRegion(new Rect(x, 0, first.Width, first.Height), visibleFrom));
            var captured = view;
            frame.PreviewMouseLeftButtonDown += (_, e) =>
            { if (controller.ActiveDraft == captured.Session.Id) return; controller.ShowEditor(captured, true); e.Handled = true; };
            Canvas.SetLeft(frame, x); canvas.Children.Add(frame);
        }
        controller.Clamp(this); UpdateGlassGeometry(); Show(); Native.RaiseBall();
    }
    public void Detach(EditorSurface view)
    {
        if (view.Parent is Grid grid) grid.Children.Remove(view);
        view.Host = null;
    }
    internal override IEnumerable<GlassRegion> GlassRegions() => regions;
    protected override void ApplyGlassTint() => Root.Background = Ui.Brush("#01000000");
    public override void RefreshGlass() { foreach (var tint in tints) tint.Opacity = 1 - Transparency / 100; base.RefreshGlass(); }
    public void ClearClosed()
    { canvas.Children.Clear(); tints.Clear(); regions.Clear(); UpdateGlassGeometry(); DraftCount = 0; Hide(); }
}
