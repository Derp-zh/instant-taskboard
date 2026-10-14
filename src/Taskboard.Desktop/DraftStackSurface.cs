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
    protected override bool ScrollEnabled => true;
    private readonly BoardController controller;
    private readonly Canvas canvas = new();
    private readonly List<Border> tints = new();
    private readonly List<GlassRegion> regions = new();
    private Rect lastBounds;
    internal int DraftCount { get; private set; }
    private EditorSurface[] switchViews = [];
    internal double SwitchDistance(EditorSurface selected) => Math.Max(1, Array.IndexOf(switchViews, selected)) * selected.Width / 5;
    internal byte[] CaptureFrontCard(double scale)
    {
        var frame = canvas.Children.OfType<Border>().Single(x => x.Child is Grid grid && grid.Children.OfType<EditorSurface>().Contains(switchViews[0]));
        var width = (int)Math.Ceiling(frame.Width * scale); var height = (int)Math.Ceiling(frame.Height * scale);
        var image = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32); image.Render(frame);
        var bytes = new byte[width * height * 4]; image.CopyPixels(bytes, width * 4, 0); return bytes;
    }
    internal void SwitchAt(Point point)
    {
        if (point.Y < 72 || point.Y >= Height || switchViews.Length < 2) return;
        var pane = switchViews[0].Width; var step = pane / 5;
        for (var i = 1; i < switchViews.Length; i++)
            if (point.X >= pane + (i - 1) * step && point.X < pane + i * step) { controller.ShowEditor(switchViews[i], true); return; }
    }
    public DraftStackSurface(BoardController controller) : base(516, 650)
    {
        this.controller = controller; WindowLayer = 1; Title = "新建任务";
        Root.Background = Ui.Brush("#01000000"); Root.Children.Add(canvas);
    }
    public void Layout(IReadOnlyList<EditorSurface> visible, Rect[] bounds, int page, int pages)
    {
        if (visible.Count == 0) { Hide(); return; }
        var first = bounds[0]; var outline = bounds[^1];
        switchViews = visible.ToArray();
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
            view.SetDraftPresentation(i == 0, controller.DraftNumber(view.Session.Id));
            view.IsHitTestVisible = i == 0;
            view.Width = first.Width; view.Height = first.Height; view.Host = this;
            var shade = new Border { Background = Ui.Surface, Opacity = 1 - Transparency / 100, IsHitTestVisible = false };
            tints.Add(shade); var inner = new Grid(); inner.Children.Add(shade); inner.Children.Add(view);
            var frame = new Border { Width = first.Width, Height = first.Height, Background = Brushes.Transparent, CornerRadius = new CornerRadius(20), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = inner };
            var full = new RectangleGeometry(new Rect(0, 0, first.Width, first.Height), 20, 20);
            var visibleFrom = i == 0 ? 0 : Math.Max(0, first.Width - (bounds[i].Left - bounds[i - 1].Left) - 20);
            // Only the background extends into the rounded seam. The preview
            // controls must not bleed through the active card's right edge.
            var previewFrom = i == 0 ? 0 : first.Width - (bounds[i].Left - bounds[i - 1].Left);
            view.Clip = i == 0 ? null : new RectangleGeometry(new Rect(previewFrom, 0, first.Width - previewFrom, first.Height));
            frame.Clip = visibleFrom == 0 ? full : new CombinedGeometry(GeometryCombineMode.Intersect, full, new RectangleGeometry(new Rect(visibleFrom, 0, first.Width - visibleFrom, first.Height)));
            regions.Add(new GlassRegion(new Rect(x, 0, first.Width, first.Height), visibleFrom));
            var captured = view;
            frame.PreviewMouseLeftButtonDown += (_, e) =>
            { if (controller.ActiveDraft == captured.Session.Id || e.GetPosition(frame).Y < 72) return; controller.ShowEditor(captured, true); e.Handled = true; };
            Canvas.SetLeft(frame, x); canvas.Children.Add(frame);
        }
        var topMove = new Border { Width = geometry.Width - first.Width, Height = 72, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeAll, Tag = "DraftStripMoveArea" };
        topMove.MouseLeftButtonDown += (_, e) => { DragMove(); controller.EditorMoved(visible[0]); e.Handled = true; };
        Canvas.SetLeft(topMove, first.Width); canvas.Children.Add(topMove);
        controller.Clamp(this); UpdateGlassGeometry(); InvalidateAnimation(); Show(); Native.RaiseBall();
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
    {
        // Capture the last draft before removing its view, so closing can roll it up.
        if (IsVisible && !IsScrollAnimating) WarmAnimation();
        Hide(); canvas.Children.Clear(); tints.Clear(); regions.Clear(); switchViews = []; DraftCount = 0;
    }
}
