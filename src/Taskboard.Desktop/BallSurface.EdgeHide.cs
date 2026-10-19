using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Taskboard.Desktop;

internal sealed partial class BallSurface
{
    private readonly DispatcherTimer edgeIdle = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Ellipse edgeDot = new()
    {
        Width = 6, Height = 6, IsHitTestVisible = false, RenderTransform = new TranslateTransform(0, -10),
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(4, 0, 0, 0), Visibility = Visibility.Collapsed, Tag = "BallEdgeDot"
    };
    private DateTime edgeLastUse = DateTime.UtcNow;
    private bool edgeMenuOpen, edgeRevealing;
    private double edgeVisibleLeft;
    private Rect edgeDesktop;
    private EventHandler? edgeFrame;
    private readonly Stopwatch edgeClock = new();
    internal bool IsEdgeRestoring => edgeFrame is not null && edgeRevealing;
    internal bool IsEdgeAnimating => edgeFrame is not null;
    internal Rect EdgeVisibleClip => new(Math.Clamp(edgeDesktop.Left - Left, 0, Width), 0,
        Math.Max(0, Math.Clamp(edgeDesktop.Right - Left, 0, Width) - Math.Clamp(edgeDesktop.Left - Left, 0, Width)), Height);
    internal bool IsEdgeHidden { get; private set; }
    internal bool EdgeHiddenOnLeft => false;
    internal Ellipse EdgeDot => edgeDot;

    private void InitializeEdgeAutoHide()
    {
        edgeDot.Fill = countBubble.BorderBrush;
        Root.Children.Add(edgeDot);
        edgeIdle.Tick += (_, _) => AdvanceEdgeIdle(DateTime.UtcNow);
        Root.MouseEnter += (_, _) => { RestoreFromEdge(animate: true); ResetEdgeIdle(); };
        Root.MouseLeave += (_, _) => ResetEdgeIdle();
        IsVisibleChanged += (_, _) => RefreshEdgeAutoHide();
        Closed += (_, _) => { edgeIdle.Stop(); StopEdgeMotion(); };
    }
    internal void ResetEdgeIdle() => edgeLastUse = DateTime.UtcNow;
    internal void SetEdgeMenuOpen(bool open) { edgeMenuOpen = open; RestoreFromEdge(); ResetEdgeIdle(); }
    internal void RefreshEdgeAutoHide()
    {
        edgeIdle.Stop(); RestoreFromEdge();
        ApplyDockShape(); UpdateGlassGeometry();
        if (IsEdgeDocked)
        {
            DockAtRight();
            if (IsVisible) edgeIdle.Start();
        }
        ResetEdgeIdle();
    }
    // Always persist the visible position; hidden/animated coordinates are temporary.
    internal double VisibleBallLeft => IsEdgeHidden ? edgeVisibleLeft : Left + Width - 58;
    internal void DockAtRight()
    {
        edgeDesktop = DesktopBounds();
        Left = edgeDesktop.Right - Width;
        Top = Math.Clamp(Top, edgeDesktop.Top, Math.Max(edgeDesktop.Top, edgeDesktop.Bottom - Height));
        edgeVisibleLeft = Left;
    }
    internal void AdvanceEdgeIdle(DateTime now)
    {
        if (!IsEdgeDocked || !IsVisible) { RestoreFromEdge(); return; }
        if (controller.Expanded || HasActiveMotion || IsEdgeAnimating || rightDown || dragging || edgeMenuOpen || preview.IsVisible || results.IsVisible)
        { edgeLastUse = now; return; }
        var pointer = System.Windows.Forms.Cursor.Position;
        var bounds = Native.ScreenBounds(this);
        var scale = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96.0;
        var local = new Point((pointer.X - bounds.Left) / scale, (pointer.Y - bounds.Top) / scale);
        if ((!IsEdgeHidden || pointer.X < bounds.Left + Width * scale * .25) && ContainsOrbPoint(local))
        { RestoreFromEdge(animate: true); edgeLastUse = now; return; }
        if (!IsEdgeHidden && now - edgeLastUse >= TimeSpan.FromSeconds(2)) HideAtEdge(DesktopBounds());
    }
    private Rect DesktopBounds()
    {
        if (controller.Smoke) return new Rect(-12000, -12000, 4000, 4000);
        var bounds = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).EnsureHandle()).Bounds;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(transform.Transform(new Point(bounds.Left, bounds.Top)), transform.Transform(new Point(bounds.Right, bounds.Bottom)));
    }
    internal void HideAtEdge(Rect desktop, bool animate = true)
    {
        if (!IsEdgeDocked || IsEdgeHidden || controller.Expanded || HasActiveMotion || Width > 58) return;
        edgeDesktop = desktop; edgeVisibleLeft = desktop.Right - Width;
        Top = Math.Clamp(Top, desktop.Top, Math.Max(desktop.Top, desktop.Bottom - Height));
        IsEdgeHidden = true; HidePopups(); UpdateBadge();
        var target = desktop.Right - Width * .25;
        if (animate && IsVisible) StartEdgeMotion(target, revealing: false);
        else { Left = target; edgeDot.Opacity = 1; Native.ClipWindow(this, Height); }
    }
    internal void RestoreFromEdge(bool animate = false)
    {
        if (!IsEdgeHidden) return;
        if (animate && IsVisible)
        {
            if (!IsEdgeRestoring) StartEdgeMotion(edgeVisibleLeft, revealing: true);
            return;
        }
        StopEdgeMotion(); IsEdgeHidden = false; Left = edgeVisibleLeft;
        Native.ClipWindow(this, Height); UpdateBadge();
    }
    private void StartEdgeMotion(double target, bool revealing)
    {
        StopEdgeMotion(); edgeRevealing = revealing;
        var from = Left; var dotFrom = edgeDot.Opacity;
        if (!revealing) { dotFrom = 0; edgeDot.Opacity = 0; }
        edgeClock.Restart();
        edgeFrame = (_, _) =>
        {
            var fraction = Math.Clamp(edgeClock.Elapsed.TotalMilliseconds / 100, 0, 1);
            Left = from + (target - from) * (1 - Math.Pow(1 - fraction, 3));
            edgeDot.Opacity = revealing ? dotFrom * (1 - fraction) : fraction;
            Native.ClipWindow(this, Height);
            if (fraction < 1) return;
            StopEdgeMotion();
            if (revealing) { IsEdgeHidden = false; UpdateBadge(); }
        };
        CompositionTarget.Rendering += edgeFrame;
    }
    private void StopEdgeMotion()
    {
        if (edgeFrame is not null) CompositionTarget.Rendering -= edgeFrame;
        edgeFrame = null; edgeClock.Stop();
    }
    private void UpdateEdgeBadge(int count)
    {
        if (IsEdgeHidden) countBubble.Visibility = Visibility.Collapsed;
        edgeDot.Visibility = IsEdgeHidden && count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!IsEdgeAnimating) edgeDot.Opacity = 1;
    }
}
