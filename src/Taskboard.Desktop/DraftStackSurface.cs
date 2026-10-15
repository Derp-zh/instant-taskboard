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
    private Canvas canvas = new();
    private readonly List<Border> tints = new();
    private readonly List<GlassRegion> regions = new();
    private Rect lastBounds;
    internal int DraftCount { get; private set; }
    private EditorSurface[] switchViews = [];
    private EditorSurface? pendingSwitch;
    internal void Select(EditorSurface editor, Action change, long? pressedAt = null)
    {
        if (pressedAt is long captured && IsEarlyContentSwapPress(captured)) return;
        if (IsContentSwapHandoff)
        {
            if (pendingSwitch is null && CanQueueContentSwap(pressedAt ?? Environment.TickCount64)) pendingSwitch = editor;
            return;
        }
        pendingSwitch = null;
        if (switchViews.Length < 2 || !switchViews.Contains(editor)) { change(); return; }
        SwapContent(change, editor.Width, SwitchDistance(editor));
    }
    internal double SwitchDistance(EditorSurface selected) => Math.Max(1, Array.IndexOf(switchViews, selected)) * selected.Width / 5;
    internal byte[] CaptureFrontCard(double scale)
        => CaptureCard(switchViews[0], scale, includeTint: false);
    internal EditorSurface FrontCard => switchViews[0];
    internal EditorSurface SelectedCard(double slot) => switchViews[Math.Clamp((int)Math.Round(slot / (switchViews[0].Width / 5)), 1, switchViews.Length - 1)];
    internal byte[] CaptureCard(EditorSurface view, double scale, bool preservePreview = false, bool includeTint = true)
    {
        var frame = canvas.Children.OfType<Border>().Single(x => x.Child is Grid grid && grid.Children.OfType<EditorSurface>().Contains(view));
        var snapshot = view.CreateSnapshot(); snapshot.Clip = preservePreview ? view.Clip : null;
        var contents = new Grid(); if (includeTint) contents.Children.Add(new Border { Background = Ui.Surface, Opacity = 1 - GlassTransparency / 100 });
        contents.Children.Add(snapshot);
        var capture = new Border { Width = frame.Width, Height = frame.Height, CornerRadius = frame.CornerRadius, BorderBrush = frame.BorderBrush, BorderThickness = frame.BorderThickness,
            Child = contents, Clip = new RectangleGeometry(new Rect(0, 0, frame.Width, frame.Height), 20, 20) };
        try
        {
        capture.Measure(new Size(frame.Width, frame.Height)); capture.Arrange(new Rect(0, 0, frame.Width, frame.Height)); capture.UpdateLayout();
        var width = (int)Math.Ceiling(frame.Width * scale); var height = (int)Math.Ceiling(frame.Height * scale);
        // A detached copy never redirects the live input visual to a bitmap target.
        var image = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32); image.Render(capture);
        var bytes = new byte[width * height * 4]; image.CopyPixels(bytes, width * 4, 0); return bytes;
        }
        finally { snapshot.Destroy(); }
    }
    internal void SwitchAt(Point point, long? pressedAt = null)
    {
        if (point.Y < 72 || point.Y >= Height || switchViews.Length < 2) return;
        var pane = switchViews[0].Width; var step = pane / 5;
        for (var i = 1; i < switchViews.Length; i++)
            if (point.X >= pane + (i - 1) * step && point.X < pane + i * step) { controller.ShowEditor(switchViews[i], true, pressedAt); return; }
    }
    public DraftStackSurface(BoardController controller) : base(516, 650)
    {
        this.controller = controller; WindowLayer = 1; Title = "新建任务";
        ContentSwapCompleted += () =>
        {
            var selected = pendingSwitch; pendingSwitch = null;
            if (selected is not null && IsVisible && controller.Editors.Contains(selected)) controller.ShowEditor(selected, true);
        };
        Root.Background = Ui.Brush("#01000000"); Root.Children.Add(canvas);
    }
    public void Layout(IReadOnlyList<EditorSurface> visible, Rect[] bounds, int page, int pages)
    {
        if (visible.Count == 0) { Hide(); return; }
        var first = bounds[0]; var outline = bounds[^1];
        switchViews = visible.ToArray();
        var entryShift = DraftCardGeometry.EntryShift(first.Width);
        var geometry = new Rect(first.Left, first.Top, outline.Right - first.Left - entryShift, first.Height);
        // Keep the old frames and their tint attached throughout native resize.
        if (geometry != lastBounds)
        {
            Width = geometry.Width; Height = geometry.Height; Left = geometry.Left; Top = geometry.Top;
            lastBounds = geometry;
        }
        var nextCanvas = new Canvas();
        foreach (var old in canvas.Children.OfType<Border>())
            if (old.Child is Grid grid)
                foreach (var view in grid.Children.OfType<EditorSurface>().ToArray()) grid.Children.Remove(view);
        tints.Clear(); regions.Clear(); DraftCount = visible.Count;
        var outlineShape = new Rectangle { Tag = "DraftNewOutline", Width = first.Width, Height = first.Height, RadiusX = 20, RadiusY = 20, Stroke = Ui.Muted, StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection([5, 5]), IsHitTestVisible = false,
            Clip = new RectangleGeometry(new Rect(first.Width * 4 / 5, 0, first.Width / 5, first.Height)) };
        Canvas.SetLeft(outlineShape, outline.Left - first.Left - entryShift); nextCanvas.Children.Add(outlineShape);
        var dashed = new Grid { Tag = "DraftNewHitArea", Width = first.Width / 5 - entryShift, Height = first.Height, Background = Ui.Brush("#01000000") };
        dashed.Clip = new RectangleGeometry(new Rect(0, 0, dashed.Width, dashed.Height));
        var strip = new StackPanel { Width = dashed.Width, VerticalAlignment = VerticalAlignment.Center };
        var add = Ui.Button("＋", () => controller.NewTask()); add.FontSize = 24; add.Background = Brushes.Transparent; add.BorderThickness = new Thickness(0);
        add.HorizontalContentAlignment = HorizontalAlignment.Center; add.Padding = new Thickness(0, 7, 0, 7);
        var addCaption = Ui.Label("新建任务", 12); addCaption.TextAlignment = TextAlignment.Center;
        strip.Children.Add(add); strip.Children.Add(addCaption);
        if (pages > 1)
        {
            var previous = Ui.Button("‹", () => controller.ChangeDraftPage(-1)); previous.IsEnabled = page > 0;
            var next = Ui.Button("›", () => controller.ChangeDraftPage(1)); next.IsEnabled = page + 1 < pages;
            strip.Children.Add(previous); strip.Children.Add(Ui.Label($"{page + 1}/{pages}", 11)); strip.Children.Add(next);
        }
        dashed.Children.Add(strip); dashed.MouseLeftButtonUp += (_, e) => { controller.NewTask(); e.Handled = true; };
        Canvas.SetLeft(dashed, outline.Left - first.Left + first.Width * 4 / 5); nextCanvas.Children.Add(dashed);
        for (var i = visible.Count - 1; i >= 0; i--)
        {
            var view = visible[i]; var x = bounds[i].Left - first.Left;
            view.SetDraftPresentation(i == 0, controller.DraftNumber(view.Session.Id));
            view.IsHitTestVisible = i == 0;
            view.Width = first.Width; view.Height = first.Height; view.Host = this;
            var shade = new Border { Background = Ui.Surface, Opacity = 1 - GlassTransparency / 100, IsHitTestVisible = false };
            tints.Add(shade); var inner = new Grid(); inner.Children.Add(shade); inner.Children.Add(view);
            var frame = new Border { Width = first.Width, Height = first.Height, Background = Brushes.Transparent, CornerRadius = new CornerRadius(20), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = inner };
            var previousOffset = i == 0 ? 0 : bounds[i].Left - bounds[i - 1].Left;
            var previewFrom = i == 0 ? 0 : first.Width - previousOffset;
            view.Clip = i == 0 ? null : new RectangleGeometry(new Rect(previewFrom, 0, first.Width - previewFrom, first.Height));
            frame.Clip = DraftCardGeometry.VisibleCard(first.Width, first.Height, previousOffset);
            regions.Add(new GlassRegion(new Rect(x, 0, first.Width, first.Height), previewFrom, FillCornerGaps: i > 0));
            var captured = view;
            frame.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (controller.ActiveDraft == captured.Session.Id || e.GetPosition(frame).Y < 72) return;
                var pressedAt = Environment.TickCount64 - unchecked((uint)Environment.TickCount - (uint)e.Timestamp);
                controller.ShowEditor(captured, true, pressedAt); e.Handled = true;
            };
            Canvas.SetLeft(frame, x); nextCanvas.Children.Add(frame);
        }
        var topMove = new Border { Width = geometry.Width - first.Width, Height = 72, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeAll, Tag = "DraftStripMoveArea" };
        topMove.MouseLeftButtonDown += (_, e) => { DragMove(); controller.EditorMoved(visible[0]); e.Handled = true; };
        Canvas.SetLeft(topMove, first.Width); nextCanvas.Children.Add(topMove);
        Root.Children.Add(nextCanvas); Root.Children.Remove(canvas); canvas = nextCanvas;
        controller.Clamp(this); UpdateGlassGeometry(); InvalidateAnimation(); Show(); Native.RaiseBall();
    }
    public void Detach(EditorSurface view)
    {
        if (view.Parent is Grid grid) grid.Children.Remove(view);
        view.Host = null;
    }
    internal override IEnumerable<GlassRegion> GlassRegions() => regions;
    protected override void ApplyGlassTint() => Root.Background = Ui.Brush("#01000000");
    public override void RefreshGlass() { foreach (var tint in tints) tint.Opacity = 1 - GlassTransparency / 100; base.RefreshGlass(); }
    public void ClearClosed()
    {
        pendingSwitch = null;
        // Capture the last draft before removing its view, so closing can roll it up.
        if (IsVisible && !IsScrollAnimating) WarmAnimation();
        Hide(); canvas.Children.Clear(); tints.Clear(); regions.Clear(); switchViews = []; DraftCount = 0;
    }
}
