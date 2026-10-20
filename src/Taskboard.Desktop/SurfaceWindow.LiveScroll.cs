using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Taskboard.Desktop;

internal partial class SurfaceWindow
{
    // Separate visual layer: no changes to Root's controls, hit targets or UIA tree.
    private readonly Grid scrollGlow = CreateScrollGlow();
    private EventHandler? liveScrollFrame;
    private Action? cancelLivePreparation;
    internal int LiveScrollRevealCount { get; private set; }
    internal event Action? LiveScrollStarted;
    private static Grid CreateScrollGlow()
    {
        var glow = new Grid { IsHitTestVisible = false, Visibility = Visibility.Collapsed, UseLayoutRounding = true, SnapsToDevicePixels = true };
        foreach (var (width, alpha) in new[] { (28.0, (byte)46), (8.0, (byte)100) })
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 235, 246, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 235, 246, 255), .5));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 235, 246, 255), 1));
            glow.Children.Add(new Border { Width = width, Background = brush, HorizontalAlignment = HorizontalAlignment.Center });
        }
        glow.Children.Add(new Border { Width = ScrollMotion.CoreWidth, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        return glow;
    }
    private void CancelLiveScrollFrame()
    {
        if (liveScrollFrame is not null) CompositionTarget.Rendering -= liveScrollFrame;
        liveScrollFrame = null; scrollGlow.Visibility = Visibility.Collapsed;
        cancelLivePreparation?.Invoke(); cancelLivePreparation = null;
    }
    private async void RevealLiveScroll(bool visible)
    {
        if (scrollRunning && scrollTo == (visible ? 1 : 0)) return;
        using var participant = WorkspaceRevealBatch.Current?.Enroll();
        WorkspaceRevealGroup = participant?.GroupId ?? 0;
        var from = ScrollProgress; var initialLight = ScrollLight;
        if (!visible && from <= 0) { HideImmediately(); if (destroyAfterScroll) Destroy(false); return; }
        var generation = ++motion;
        CancelHandoff(); animationDeadline?.Stop(); backdrop?.CancelAnimation(); RestoreSwapBounds();
        IsContentSwapping = false; scrollRunning = true; animating = true; LastAnimationError = null;
        scrollFrom = from; scrollTo = visible ? 1 : 0; scrollStartLight = initialLight;
        scrollDuration = ScrollMotion.DurationMs; scrollClock.Reset();
        new WindowInteropHelper(this).EnsureHandle(); UpdateGlassGeometry();
        contentHost.AnimationHidden = false; contentHost.Visibility = Visibility.Visible; Root.Opacity = 1;
        Root.Measure(new Size(Width, Height)); Root.Arrange(new Rect(0, 0, Width, Height)); Root.UpdateLayout();
        scrollGlow.Opacity = initialLight; scrollGlow.Visibility = Visibility.Visible;
        Native.ClipLiveScroll(this, from);
        if (!IsVisible && visible) ShowImmediately();
        Native.ShowWindow(ContentHandle, 4); IsHitTestVisible = visible;
        await Dispatcher.InvokeAsync(() => Root.UpdateLayout(), DispatcherPriority.Render);
        if (generation != motion) return;
        // Let the first live layout reach composition before starting the
        // 100ms clock. Cold text/control rendering must not consume the flare.
        var prepared = new TaskCompletionSource<bool>(); var rendered = 0;
        var preparationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(80) };
        preparationDeadline.Tick += (_, _) => prepared.TrySetResult(true);
        cancelLivePreparation = () => { preparationDeadline.Stop(); prepared.TrySetResult(false); };
        liveScrollFrame = (_, _) => { if (++rendered >= 2) prepared.TrySetResult(true); };
        CompositionTarget.Rendering += liveScrollFrame;
        preparationDeadline.Start(); var ready = await prepared.Task; preparationDeadline.Stop();
        if (!ready || generation != motion) return;
        CompositionTarget.Rendering -= liveScrollFrame; liveScrollFrame = null; cancelLivePreparation = null;
        if (participant is not null) await participant.Ready();
        if (generation != motion) return;
        MotionDisplayStartTicks = Stopwatch.GetTimestamp(); scrollClock.Restart(); LiveScrollRevealCount++;
        UsesIndependentAnimation = false;
        void Finish()
        {
            if (generation != motion || !scrollRunning) return;
            CancelLiveScrollFrame(); animationDeadline?.Stop(); scrollRunning = false; animating = false;
            if (!visible) { HideImmediately(); if (destroyAfterScroll) Destroy(false); }
            else Native.ClipWindow(this, Height);
            IsHitTestVisible = true;
        }
        liveScrollFrame = (_, _) =>
        {
            if (generation != motion || !scrollRunning) return;
            var fraction = Math.Clamp(scrollClock.Elapsed.TotalMilliseconds / scrollDuration, 0, 1);
            Native.ClipLiveScroll(this, ScrollProgress);
            scrollGlow.Opacity = ScrollMotion.Light(fraction, visible, initialLight);
            if (fraction >= 1) Finish();
        };
        CompositionTarget.Rendering += liveScrollFrame;
        animationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(scrollDuration + 16) };
        animationDeadline.Tick += (_, _) => Finish(); animationDeadline.Start();
        LiveScrollStarted?.Invoke();
    }
}
