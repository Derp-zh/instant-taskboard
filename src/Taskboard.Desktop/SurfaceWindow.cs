using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal partial class SurfaceWindow : Window
{
    public readonly Grid Root = new SurfaceContentRoot();
    private bool allowClose;
    private bool activateWhenShown;
    private bool animating;
    protected bool HasActiveMotion => animating;
    internal bool IsRevealAnimating => animating;
    internal bool FloatingAnimations { get; set; } = true;
    internal bool SuppressActivation { get; set; }
    protected virtual bool ScrollEnabled => FloatingAnimations && GlassEnabled;
    private bool scrollRunning, destroyAfterScroll;
    private double scrollFrom, scrollTo, scrollDuration, scrollStartLight;
    private readonly Stopwatch scrollClock = new();
    internal bool IsScrollAnimating => scrollRunning;
    internal bool IsContentSwapping { get; private set; }
    internal bool IsContentSwapHandoff => IsContentSwapping || handoff is not null || handoffCommitting;
    internal event Action? ContentSwapCompleted;
    internal long ContentSwapCount { get; private set; }
    internal double LastContentSwapPreparationMs { get; private set; }
    internal long ContentSwapFinishTicks { get; private set; }
    private long contentSwapStartedAt;
    internal bool CanQueueContentSwap(long pressedAt) =>
        contentSwapStartedAt > 0 && pressedAt - contentSwapStartedAt >= DraftSwitchMotion.DurationMs * 2 / 3;
    internal bool IsEarlyContentSwapPress(long pressedAt) =>
        contentSwapStartedAt > 0 && pressedAt - contentSwapStartedAt < DraftSwitchMotion.DurationMs * 2 / 3;
    private Rect? swapBounds;
    internal Point SwapInputOffset { get; private set; }
    private void RestoreSwapBounds()
    {
        if (swapBounds is not Rect original) return;
        swapBounds = null; SwapInputOffset = new Point();
        Left = original.Left; Top = original.Top; Width = original.Width; Height = original.Height;
        Root.UpdateLayout(); UpdateGlassGeometry(); Native.ClipWindow(this, Height); imageDirty = true;
    }
    internal bool ShouldRestore => scrollRunning ? scrollTo > 0 : IsVisible;
    internal event Action? BeforeOpen;
    internal double ScrollProgress => scrollRunning ? ScrollMotion.Progress(scrollFrom, scrollTo, scrollClock.Elapsed.TotalMilliseconds, scrollDuration) : IsVisible ? 1 : 0;
    internal double ScrollLight => scrollRunning ? ScrollMotion.Light(scrollClock.Elapsed.TotalMilliseconds / Math.Max(1, scrollDuration), scrollTo == 1, scrollStartLight) : 0;
    internal bool CanInterruptTaskOpening => !scrollRunning || scrollTo <= 0 ||
        (scrollClock.IsRunning && ScrollMotion.CanInterruptOpening(scrollClock.Elapsed.TotalMilliseconds));
    public new void Show()
    {
        if (destroyAfterScroll) return;
        // Calling an existing/opening window is a focus operation. Replaying
        // its reveal or restoring input mid-animation would cause a flash.
        if (IsVisible && (!scrollRunning || scrollTo > 0)) return;
        BeforeOpen?.Invoke();
        if (ScrollEnabled && (!IsVisible || scrollRunning)) { RevealScroll(true); return; }
        ShowImmediately();
    }
    private void ShowImmediately() {if(!animating) {Root.Opacity=1;contentHost.Visibility=Visibility.Visible;backdrop?.SetVisible(true);} base.Show();if(!animating)Native.ShowWindow(ContentHandle,4);if(activateWhenShown){activateWhenShown=false;base.Activate();}Native.RaiseBall();}
    public new bool Activate() { if (SuppressActivation) { ActivationOrder = ++activationSequence; return false; } if (!IsVisible) { activateWhenShown = true; ActivationOrder = ++activationSequence; return false; } return base.Activate(); }
    public new void Hide()
    {
        activateWhenShown = false;
        if (ScrollEnabled && IsVisible) { RevealScroll(false); return; }
        HideImmediately();
    }
    private void HideImmediately()
    {motion++;animating=false;scrollRunning=false;IsContentSwapping=false;contentHost.AnimationHidden=false;animationDeadline?.Stop();CancelHandoff();backdrop?.CancelAnimation();RestoreSwapBounds();backdrop?.SetVisible(false);Root.Opacity=0;Native.ShowWindow(ContentHandle,0);base.Hide();}
    public bool GlassEnabled { get; set; } = true;
    public int WindowLayer { get; set; } = 2;
    public static double Transparency { get; private set; } = 20;
    internal static double FloatingTransparency { get; private set; } = 10;
    public string BackdropMode { get; internal set; } = "未初始化";
    internal int NativeHideCount { get; private set; }
    internal int ActivationPaintsSuppressed { get; private set; }
    public long ActivationOrder { get; private set; }
    private static long activationSequence;
    private long motion;
    internal long MotionGeneration => motion;
    private DispatcherTimer? animationDeadline;
    private EventHandler? handoff;
    private bool handoffCommitting;
    private EventHandler? liveListFrame;
    private double liveListHeight;
    internal int LiveListRevealCount { get; private set; }
    private void CancelHandoff()
    {
        if(handoff is not null) CompositionTarget.Rendering-=handoff;
        if(liveListFrame is not null) CompositionTarget.Rendering-=liveListFrame;
        CancelLiveScrollFrame(); CancelLiveBallFrame();
        liveListFrame=null; handoff=null; handoffCommitting=false;
    }
    private async void CompleteHandoff(long generation, Action? complete = null)
    {
        if (generation != motion) return;
        handoffCommitting = true;
        try
        {
            void ShowLive()
            {
                contentHost.AnimationHidden = false; contentHost.Visibility = Visibility.Visible;
                Root.Opacity = 1; Native.ShowWindow(ContentHandle, 4);
            }
            if (backdrop is not null) await backdrop.CommitRestingFrame(ShowLive); else ShowLive();
            if (generation != motion) return;
            Native.DwmFlush(); backdrop?.CancelAnimation(); handoffCommitting = false; complete?.Invoke();
        }
        catch (Exception e) when (e is COMException or ObjectDisposedException) { }
        finally { if (generation == motion) handoffCommitting = false; }
    }
    private byte[]? animationImage;
    private int imageWidth, imageHeight;
    private bool imageDirty = true;
    internal double LastAnimationPreparationMs { get; private set; }
    internal double LastSnapshotMs { get; private set; }
    internal double LastShowMs { get; private set; }
    internal long MotionDisplayStartTicks { get; private set; }
    internal long WorkspaceRevealGroup { get; private set; }
    internal bool UsesIndependentAnimation { get; private set; }
    internal string? LastAnimationError { get; private set; }
    private LiveBackdrop? backdrop;
    private readonly GlassContentHost contentHost;
    internal bool LiveContentHidden => contentHost.AnimationHidden;
    internal int RestingHandoffCount => backdrop?.RestingHandoffCount ?? 0;
    internal int PersistentBackdropMotionCount => LiveListRevealCount;
    internal double TaskOpeningElapsedMs => scrollClock.IsRunning ? scrollClock.Elapsed.TotalMilliseconds : -1;
    internal IntPtr ContentHandle => contentHost.ContentHandle;
    internal Rect LiveContentBounds => Native.ScreenBounds(ContentHandle);
    protected void UseFixedBallCanvas()
    {
        Root.Width = 370; contentHost.Width = 370; contentHost.HorizontalAlignment = HorizontalAlignment.Right;
        contentHost.RightAnchoredCanvasWidth = 370;
    }
    internal virtual double GlassTransparency => FloatingTransparency;
    internal double GlassRadius => this is BallSurface ? Height / 2 : 20;
    internal virtual IEnumerable<GlassRegion> GlassRegions() => [new(new Rect(0, 0, Width, Height), Radius: GlassRadius)];
    public event Action? PositionChanged;
    public SurfaceWindow(double width, double height, bool layered = false)
    {
        Width = width; Height = height; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        FontFamily = new FontFamily("Microsoft YaHei UI"); Foreground = Ui.Text;
        Root.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, FontFamily);
        Root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Foreground);
        QuickScroll.SetVerticalBarWidth(Root, this is ColumnSurface ? 6 : 6 * 1.34);
        QuickScroll.SetThumbMargin(Root, this is ColumnSurface ? new Thickness(1) : new Thickness(1.34, 1, 1.34, 1));
        Root.Background = Ui.Surface;
        Root.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 16, 16);
        var presentation = new Grid();
        presentation.Children.Add(Root); presentation.Children.Add(scrollGlow);
        contentHost = new GlassContentHost(presentation, (SurfaceContentRoot)Root); Content = contentHost;
        SourceInitialized += (_, _) =>
        {
            Native.HideFromTaskSwitcher(new WindowInteropHelper(this).Handle);
            if (GlassEnabled) Native.TryGlass(this);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(CompositionChanged);
            Native.ClipWindow(this, Height);
        };
        Activated += (_, _) => { ActivationOrder = ++activationSequence; Native.RaiseBall(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) Native.RaiseBall(); };
        SizeChanged += (_, _) => { UpdateGlassGeometry(); if (!animating) Native.ClipWindow(this, ActualHeight); };
        DpiChanged += (_, _) => { UpdateGlassGeometry(); if (!animating) Native.ClipWindow(this, Height); };
        Root.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,_) => InvalidateAnimation()));
        Root.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((_,_) => InvalidateAnimation()));
        Closed += (_, _) => { CancelHandoff(); animationDeadline?.Stop(); backdrop?.Dispose(); backdrop = null; contentHost.Dispose(); };
        Closing += (_, e) => { if (!allowClose) { e.Cancel = true; Hide(); } };
    }
    private IntPtr CompositionChanged(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0046 && updatingBallBounds && lParam != IntPtr.Zero)
        {
            var position = Marshal.PtrToStructure<WindowPos>(lParam);
            position.Flags |= 3; // Reconcile both logical properties before one native move/resize.
            Marshal.StructureToPtr(position, lParam, false);
        }
        if (message == 0x031E && GlassEnabled) Native.TryGlass(this);
        if (message == 0x0018 && wParam == IntPtr.Zero) NativeHideCount++;
        if (message == 0x0014) { handled = true; return new IntPtr(1); }
        // These windows draw their entire frame themselves. Let activation
        // change keyboard focus, but prevent Windows from repainting a native
        // non-client frame over the transparent composition target on click.
        if (message == 0x0086 && GlassEnabled) { ActivationPaintsSuppressed++; handled = true; return new IntPtr(1); }
        if (message == 0x0202 && animating && this is BallSurface ball)
        {
            var packed = lParam.ToInt64(); var scale = Native.GetDpiForWindow(hwnd)/96.0;
            var x = (short)(packed & 0xffff)/scale; var y = (short)((packed >> 16)&0xffff)/scale;
            if (ball.ContainsOrbPoint(new Point(x, y))) { ball.ClickDuringAnimation(); handled=true; }
        }
        if (message == 0x0084 && this is BallSurface entry)
        {
            var packed = lParam.ToInt64(); var p = new Point((short)(packed & 0xffff),(short)((packed >> 16)&0xffff));
            var bounds = Native.ScreenBounds(this); var radius = bounds.Height/2;
            var x = Math.Clamp(p.X,bounds.Left+radius,bounds.Right-radius); var y = bounds.Top+radius;
            if (!(entry.IsEdgeDocked && p.X >= bounds.Right - radius) && (p.X-x)*(p.X-x)+(p.Y-y)*(p.Y-y)>radius*radius) { handled = true; return new IntPtr(-1); }
        }
        return IntPtr.Zero;
    }
    public void Destroy() => Destroy(true);
    public void Destroy(bool animate)
    {
        if (animate && ScrollEnabled && IsVisible) { destroyAfterScroll = true; RevealScroll(false); return; }
        HideImmediately(); allowClose = true; motion++; Close();
    }
    public void MoveWith(UIElement header)
    {
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInteractive(source)) return;
            if (e.ButtonState != MouseButtonState.Pressed) return;
            DragMove(); PositionChanged?.Invoke(); e.Handled = true;
        };
    }
    internal static bool IsInteractive(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is Button or TextBox or ComboBox) return true;
        return false;
    }
    public static void SetTransparency(double value, double? floating = null)
    {
        Transparency = Math.Clamp(value, 0, 100);
        if (floating is double floatingValue) FloatingTransparency = Math.Clamp(floatingValue, 0, 100);
        foreach (var window in Application.Current.Windows.OfType<SurfaceWindow>().ToArray()) window.RefreshGlass();
    }
    public virtual void RefreshGlass() { imageDirty = true; if (GlassEnabled && new WindowInteropHelper(this).Handle != IntPtr.Zero) Native.TryGlass(this); }
    internal void EnsureLiveBackdrop()
    { backdrop ??= new LiveBackdrop(new WindowInteropHelper(this).Handle); ApplyGlassTint(); UpdateGlassGeometry(); }
    protected virtual void ApplyGlassTint() => Root.Background = Ui.Brush($"#{(int)Math.Round(255 * (1 - GlassTransparency / 100)):X2}192333");
    internal void UpdateGlassGeometry()
    {
        var bounds = new Rect(0,0,(this is BallSurface ? 370 : Width) + (this is BallSurface { IsEdgeDocked: true } ? GlassRadius : 0),Height);
        if (Root.Clip is not RectangleGeometry old || old.Rect != bounds || old.RadiusX != GlassRadius)
        { Root.Clip = new RectangleGeometry(bounds,GlassRadius,GlassRadius); imageDirty = true; }
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) backdrop?.Update(GlassRegions(), Native.GetDpiForWindow(hwnd) / 96.0,
            this is ColumnSurface ? GlassTransparency : null);
    }
    internal void InvalidateAnimation() => imageDirty = true;
    internal async void SwapContent(Action change, double paneWidth, double selectedSlot)
    {
        if (!IsVisible || scrollRunning || backdrop is null) { change(); return; }
        var preparation = Stopwatch.StartNew();
        var generation = ++motion; CancelHandoff(); animationDeadline?.Stop(); backdrop.CancelAnimation();
        RestoreSwapBounds();
        animating = IsContentSwapping = true; contentSwapStartedAt = 0; ContentSwapCount++;
        var scale = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96.0;
        Root.Opacity = 1; InvalidateAnimation(); WarmAnimation(); var oldWidth = imageWidth; var oldHeight = imageHeight;
        await backdrop.HoldContent(new WindowInteropHelper(this).Handle, animationImage!, imageWidth, imageHeight);
        if (generation != motion || !IsVisible) return;
        contentHost.AnimationHidden = true; contentHost.Visibility = Visibility.Hidden; Native.ShowWindow(ContentHandle, 0);
        var stack = (DraftStackSurface)this; var oldFront = stack.FrontCard;
        var beforeCard = stack.CaptureFrontCard(scale);
        var beforeSelectedCard = stack.CaptureCard(stack.SelectedCard(selectedSlot), scale, includeTint: false);
        change(); Root.Opacity = 1; WarmAnimation();
        var afterCard = stack.CaptureFrontCard(scale); var oldAfterCard = stack.CaptureCard(oldFront, scale, includeTint: false);
        if (oldWidth != imageWidth || oldHeight != imageHeight) { IsContentSwapping = false; animating = false; contentHost.AnimationHidden = false; Native.ShowWindow(ContentHandle, 4); return; }
        var completed = false;
        void Finish()
        {
            if (generation != motion || completed) return; completed = true; ContentSwapFinishTicks = Stopwatch.GetTimestamp(); animationDeadline?.Stop(); animating = IsContentSwapping = false;
            RestoreSwapBounds();
            contentHost.AnimationHidden = true; contentHost.Visibility = Visibility.Visible; Root.Opacity = 1; Native.ShowWindow(ContentHandle, 0);
            var frames = 0; handoff = (_, _) => { if (++frames < 2) return; CancelHandoff(); if (generation == motion) CompleteHandoff(generation, () => ContentSwapCompleted?.Invoke()); };
            CompositionTarget.Rendering += handoff;
        }
        try
        {
            var padding = DraftSwitchMotion.Padding(paneWidth);
            await backdrop.AnimateSwap(new WindowInteropHelper(this).Handle, beforeCard, beforeSelectedCard, oldAfterCard, afterCard, animationImage!, imageWidth, imageHeight,
                paneWidth * scale, selectedSlot * scale, 0, 0,
                () => {
                    swapBounds = new Rect(Left, Top, Width, Height); SwapInputOffset = new Point();
                    contentHost.AnimationHidden = true; Root.Opacity = 0; Native.ShowWindow(ContentHandle, 0);
                    Width += padding;
                    Native.ClipWindow(this, Height);
                },
                () => Dispatcher.BeginInvoke(new Action(Finish), DispatcherPriority.Render),
                () => contentSwapStartedAt = Environment.TickCount64);
            if (generation != motion || completed) return;
            contentHost.AnimationHidden = true; Root.Opacity = 0; Native.ShowWindow(ContentHandle, 0); UsesIndependentAnimation = true;
            animationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(DraftSwitchMotion.DurationMs + 60) };
            animationDeadline.Tick += (_, _) => Finish(); animationDeadline.Start();
        }
        catch (Exception e) when (e is COMException or NotSupportedException or InvalidOperationException)
        { if (generation != motion || completed) return; backdrop.CancelAnimation(); UsesIndependentAnimation = false; Finish(); }
        LastContentSwapPreparationMs = preparation.Elapsed.TotalMilliseconds;
    }
    public WindowPosition Position() => new() { X = Left, Y = Top, Width = Width, Height = Height };
    public void Place(WindowPosition position)
    { Width = position.Width; Height = position.Height; Left = position.X; Top = position.Y; }

    public void Reveal(bool visible, bool animate)
    {
        if (this is ColumnSurface && animate) { RevealLiveList(visible); return; }
        if (ScrollEnabled && animate) { RevealScroll(visible); return; }
        var generation = ++motion;
        CancelHandoff();
        animationDeadline?.Stop();
        backdrop?.CancelAnimation();
        RestoreSwapBounds();
        if (!animate) { animating = false; scrollRunning = false; if (visible) ShowImmediately(); else HideImmediately(); Native.ClipWindow(this, Height); IsHitTestVisible = true; return; }
        var from = IsVisible ? Height : 0;
        animating = true; UpdateGlassGeometry();
        new WindowInteropHelper(this).EnsureHandle();
        IsHitTestVisible = visible;
        var target = visible ? Height : 0;
        RunMotion(generation,false,from,target,() =>
        {
            animating = false;
            if (!visible) Hide();
            Native.ClipWindow(this, Height);
            IsHitTestVisible = true;
        });
    }
    private async void RevealLiveList(bool visible)
    {
        using var participant = WorkspaceRevealBatch.Current?.Enroll();
        WorkspaceRevealGroup = participant?.GroupId ?? 0;
        var from = IsVisible ? (animating ? liveListHeight : Height) : 0;
        var generation = ++motion;
        CancelHandoff(); animationDeadline?.Stop(); backdrop?.CancelAnimation();
        animating = true; scrollRunning = false; IsContentSwapping = false; LastAnimationError = null;
        new WindowInteropHelper(this).EnsureHandle();
        contentHost.AnimationHidden = false; contentHost.Visibility = Visibility.Visible; Root.Opacity = 1;
        Root.Measure(new Size(Width, Height)); Root.Arrange(new Rect(0, 0, Width, Height)); Root.UpdateLayout();
        liveListHeight = from; Native.ClipLiveList(this, from);
        if (!IsVisible && visible) ShowImmediately();
        if (!IsVisible) { animating = false; return; }
        Native.ShowWindow(ContentHandle, 4); IsHitTestVisible = visible;
        // Keep real controls and the same glass/tint visible throughout. Ending
        // a list reveal never swaps a texture for a differently rasterized UI.
        if (participant is not null) await participant.Ready();
        await Dispatcher.InvokeAsync(() => Root.UpdateLayout(), DispatcherPriority.Render);
        if (generation != motion) return;
        MotionDisplayStartTicks = Stopwatch.GetTimestamp(); LiveListRevealCount++;
        var clock = Stopwatch.StartNew(); var target = visible ? Height : 0;
        void Finish()
        {
            if (generation != motion || !animating) return;
            CancelHandoff(); animationDeadline?.Stop(); animating = false; liveListHeight = target;
            if (!visible) HideImmediately(); else Native.ClipWindow(this, Height);
            IsHitTestVisible = true;
        }
        liveListFrame = (_, _) =>
        {
            if (generation != motion || !animating) return;
            var fraction = Math.Clamp(clock.Elapsed.TotalMilliseconds / 100, 0, 1);
            liveListHeight = from + (target - from) * fraction;
            Native.ClipLiveList(this, liveListHeight);
            if (fraction >= 1) Finish();
        };
        CompositionTarget.Rendering += liveListFrame;
        animationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(110) };
        animationDeadline.Tick += (_, _) => Finish(); animationDeadline.Start();
        UsesIndependentAnimation = false;
    }
    private void RevealScroll(bool visible) => RevealLiveScroll(visible);
    protected void RevealToLeft(bool expanded, Action prepare, Action complete) => RevealLiveBall(expanded, prepare, complete);
    internal void WarmAnimation()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        Root.Measure(new Size(Width,Height)); Root.Arrange(new Rect(0,0,Width,Height)); Root.UpdateLayout();
        var scale = Native.GetDpiForWindow(hwnd)/96.0;
        var width = (int)Math.Ceiling(Width*scale); var height = (int)Math.Ceiling(Height*scale);
        if (!imageDirty && animationImage is not null && imageWidth == width && imageHeight == height) return;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96*scale,96*scale,PixelFormats.Pbgra32);
        bitmap.Render(Root); animationImage = new byte[width*height*4]; bitmap.CopyPixels(animationImage,width*4,0);
        imageWidth = width; imageHeight = height; imageDirty = false;
        backdrop?.Prepare(animationImage,width,height);
    }
    internal void PrimeWindow()
    {
        var left=Left; var top=Top;
        Left=SystemParameters.VirtualScreenLeft-Width-100; Top=SystemParameters.VirtualScreenTop-Height-100;
        try { contentHost.AnimationHidden=false; ShowImmediately(); Root.UpdateLayout(); WarmAnimation(); HideImmediately(); }
        finally { Left=left;Top=top; }
    }
    private async void RunMotion(long generation, bool horizontal, double from, double to, Action complete, bool scroll = false, double durationMs = 100)
    {
        using var participant = WorkspaceRevealBatch.Current?.Enroll();
        WorkspaceRevealGroup = participant?.GroupId ?? 0;
        LastAnimationError = null;
        var watch = Stopwatch.StartNew();
        var completed = false;
        void Finish()
        {
            if (generation != motion || completed) return; completed = true; animationDeadline?.Stop();
            var oldWidth=Width; complete(); contentHost.AnimationHidden = true; contentHost.Visibility = Visibility.Visible;
            if(!IsVisible) { Root.Opacity=0;Native.ShowWindow(ContentHandle,0);backdrop?.CancelAnimation();backdrop?.SetVisible(false);return; }
            // Prepare the live child while its native surface stays hidden.
            // The compositor handoff reveals it, without two visible copies.
            Root.Opacity=1;Native.ShowWindow(ContentHandle,0);
            backdrop?.AlignAnimation((Width-oldWidth)*Native.GetDpiForWindow(new WindowInteropHelper(this).Handle)/96.0);
            var rendered=0;
            handoff=(_,_) => { if(++rendered<2) return; CancelHandoff(); if(generation==motion) CompleteHandoff(generation); };
            CompositionTarget.Rendering+=handoff;
        }
        void PrepareDisplay()
        {
            if (generation != motion || completed) return;
            contentHost.AnimationHidden = true; contentHost.Visibility = Visibility.Hidden; Root.Opacity = 0; Native.ShowWindow(ContentHandle, 0);
            var showClock = Stopwatch.StartNew(); if (!IsVisible) ShowImmediately(); LastShowMs = showClock.Elapsed.TotalMilliseconds;
            Native.ClipWindow(this, Height);
        }
        try
        {
            Root.Opacity=1; WarmAnimation(); LastSnapshotMs=watch.Elapsed.TotalMilliseconds; var hwnd = new WindowInteropHelper(this).Handle;
            if (backdrop is null) throw new NotSupportedException("当前系统没有窗口动画合成支持");
            if (horizontal) Native.ClipHorizontal(this, from); else Native.ClipWindow(this,Height);
            if (scroll)
                await backdrop!.AnimateScroll(hwnd, animationImage!, imageWidth, imageHeight, from, to, durationMs, scrollStartLight, PrepareDisplay,
                    () => Dispatcher.BeginInvoke(new Action(Finish), DispatcherPriority.Render),
                    () => { MotionDisplayStartTicks = Stopwatch.GetTimestamp(); scrollClock.Restart(); }, participant is null ? null : participant.Ready);
            else
                await backdrop!.Animate(hwnd,animationImage!,imageWidth,imageHeight,horizontal,from*Native.GetDpiForWindow(hwnd)/96.0,to*Native.GetDpiForWindow(hwnd)/96.0, PrepareDisplay,
                    () => Dispatcher.BeginInvoke(new Action(Finish),DispatcherPriority.Render), participant is null ? null : participant.Ready,
                    () => MotionDisplayStartTicks = Stopwatch.GetTimestamp(), flatRight: this is BallSurface { IsEdgeDocked: true });
            if (generation != motion || completed) return;
            UsesIndependentAnimation = true;
            // Occluded/offscreen targets can defer compositor completion events.
            // This is a single end deadline, never a frame-production timer.
            animationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(durationMs + (scroll ? 60 : 0)) };
            animationDeadline.Tick += (_,_) => Finish(); animationDeadline.Start();
        }
        catch (Exception e) when (e is COMException or NotSupportedException or InvalidOperationException)
        { if (generation != motion || completed) return; LastAnimationError = e.ToString(); backdrop?.CancelAnimation(); if (to > 0 && !IsVisible) ShowImmediately(); UsesIndependentAnimation = false; Finish(); }
        LastAnimationPreparationMs = watch.Elapsed.TotalMilliseconds;
    }
}

internal static class Native
{
    internal static void BeginWorkspaceChange() { ordering = true; WorkspaceRevealBatch.Current = new WorkspaceRevealBatch(); }
    internal static void EndWorkspaceChange() { var batch = WorkspaceRevealBatch.Current; WorkspaceRevealBatch.Current = null; batch?.Seal(); ordering=false;RaiseBall(); }
    private static Window? ball;
    private static bool ordering;
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    internal static bool Cloak(IntPtr hwnd,bool hidden) {var value=hidden?1:0;return DwmSetWindowAttribute(hwnd,13,ref value,4)==0;}
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    internal static void HideFromTaskSwitcher(IntPtr handle)
    {
        // A native tool window stays interactive but has no Alt+Tab/taskbar entry.
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new IntPtr((style | 0x80) & ~0x40000));
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x37);
    }
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern IntPtr GetFocus();
    public static void SetBall(Window window) { ball = window; RaiseBall(); }
    public static void RaiseBall()
    {
        if (ordering || Application.Current is null) return; ordering = true;
        try
        {
            var handles = Application.Current.Windows.OfType<SurfaceWindow>().Where(x => x.IsVisible && x != ball)
                .OrderBy(x => x.WindowLayer).ThenBy(x => x.ActivationOrder).Select(x => new WindowInteropHelper(x).Handle)
                .Concat(Dialogs()).Concat(ball?.IsVisible == true ? new[] { new WindowInteropHelper(ball).Handle } : Array.Empty<IntPtr>())
                .Distinct().ToArray();
            var owned = handles.ToHashSet(); var current = new List<IntPtr>();
            EnumWindows((handle, _) => { if (owned.Contains(handle)) current.Add(handle); return true; }, IntPtr.Zero);
            if (current.SequenceEqual(handles.Reverse())) return;
            // Apply the complete stack in one native transaction; sequential
            // raises briefly put every list above its floating editors.
            var positions = BeginDeferWindowPos(handles.Length);
            foreach (var handle in handles)
            {
                if (positions == IntPtr.Zero) break;
                positions = DeferWindowPos(positions, handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            }
            if (positions != IntPtr.Zero) EndDeferWindowPos(positions);
        }
        finally { ordering = false; }
    }
    public static void Raise(Window window)
    { if (window.IsVisible) SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13); }
    [DllImport("user32.dll")] private static extern IntPtr BeginDeferWindowPos(int count);
    [DllImport("user32.dll")] private static extern IntPtr DeferWindowPos(IntPtr positions, IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool EndDeferWindowPos(IntPtr positions);
    [StructLayout(LayoutKind.Sequential)] private struct Accent { public int State; public int Flags; public uint Color; public int Animation; }
    [StructLayout(LayoutKind.Sequential)] private struct Data { public int Attribute; public IntPtr DataPointer; public int Size; }
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref Data data);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] internal static extern bool EnableWindow(IntPtr hwnd,bool enabled);
    internal static IntPtr[] Dialogs()
    {
        var result = new List<IntPtr>();
        EnumWindows((handle, _) => { GetWindowThreadProcessId(handle, out var process); if (process != Environment.ProcessId || !IsWindowVisible(handle)) return true;
            var name = new StringBuilder(64); GetClassName(handle, name, 64); if (name.ToString() == "#32770") result.Add(handle); return true; }, IntPtr.Zero);
        result.Reverse(); return result.ToArray();
    }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    internal static void ClearWindowRegion(IntPtr handle) => SetWindowRgn(handle, IntPtr.Zero, false);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out NativeRect bounds);
    internal static Rect ScreenBounds(Window window)
        => ScreenBounds(new WindowInteropHelper(window).Handle);
    internal static Rect ScreenBounds(IntPtr handle)
    { GetWindowRect(handle,out var r); return new Rect(r.Left,r.Top,Math.Max(0,r.Right-r.Left),Math.Max(0,r.Bottom-r.Top)); }
    internal static void ClipLiveList(Window window, double height)
    {
        var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
        var scale = GetDpiForWindow(handle) / 96.0;
        var region = CreateRectRgn(0, 0, (int)Math.Ceiling(window.Width * scale), Math.Max(1, (int)Math.Round(height * scale)));
        if (SetWindowRgn(handle, region, false) == 0) DeleteObject(region);
    }
    internal static void ClipLiveScroll(Window window, double progress)
    {
        var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
        var scale = GetDpiForWindow(handle) / 96.0;
        var width = Math.Max(1, (int)Math.Ceiling(window.Width * scale));
        var visible = Math.Clamp((int)Math.Round(width * progress), 1, width);
        var left = (width - visible) / 2;
        var region = CreateRectRgn(left, 0, left + visible, (int)Math.Ceiling(window.Height * scale));
        if (SetWindowRgn(handle, region, false) == 0) DeleteObject(region);
    }
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    public static void ClipWindow(Window window, double height)
    {
        var hwnd = new WindowInteropHelper(window).Handle; if (hwnd == IntPtr.Zero) return;
        if (window is BallSurface ball)
        {
            if (!ball.IsEdgeHidden) { ClipHorizontal(window, window.Width); return; }
            // Clip the part outside this monitor too, so it cannot leak onto an
            // adjacent display in a multi-monitor desktop.
            var dpi = GetDpiForWindow(hwnd) / 96.0;
            var visible = ball.EdgeVisibleClip;
            var edgeRegion = CreateRectRgn((int)Math.Round(visible.Left * dpi), 0,
                (int)Math.Ceiling(visible.Right * dpi), (int)Math.Ceiling(window.Height * dpi));
            var shape = CreateRoundRectRgn(0, 0, (int)Math.Ceiling((window.Width + (ball.IsEdgeDocked ? window.Height / 2 : 0)) * dpi),
                (int)Math.Ceiling(window.Height * dpi), (int)Math.Round(window.Height * dpi), (int)Math.Round(window.Height * dpi));
            const int RegionAnd = 1;
            CombineRgn(edgeRegion, edgeRegion, shape, RegionAnd); DeleteObject(shape);
            if (SetWindowRgn(hwnd, edgeRegion, true) == 0) DeleteObject(edgeRegion);
            return;
        }
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var diameter = window is BallSurface ? 58 : 40;
        var pixelWidth = (int)Math.Ceiling(window.Width * scale); var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
        var region = window is BallSurface && Math.Abs(window.Width - window.Height) < 1 ? CreateEllipticRgn(0, 0, pixelWidth, pixelHeight) : CreateRoundRectRgn(0, 0, pixelWidth + 1, pixelHeight + 1, (int)(diameter * scale), (int)(diameter * scale));
        if (SetWindowRgn(hwnd, region, false) == 0) DeleteObject(region);
    }
    internal static void ClipHorizontal(Window window, double visibleWidth)
    {
        var hwnd = new WindowInteropHelper(window).Handle; var scale = GetDpiForWindow(hwnd) / 96.0;
        var region = CreateRoundRectRgn((int)Math.Round((window.Width - visibleWidth) * scale), 0, (int)Math.Ceiling((window.Width + (window is BallSurface { IsEdgeDocked: true } ? window.Height / 2 : 0)) * scale), (int)Math.Ceiling(window.Height * scale), (int)(window.Height * scale), (int)(window.Height * scale));
        if (SetWindowRgn(hwnd, region, false) == 0) DeleteObject(region);
    }
    public static void TryGlass(SurfaceWindow window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is not null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(handle, ref margins);
        int dark = 1, rounded = 1, hostBrush = 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        // The compositor owns shaped live backdrop visuals within this HWND;
        // no full rectangular accent layer and no capture/Hide/Show cycle.
        try
        {
            if (DwmSetWindowAttribute(handle, 17, ref hostBrush, sizeof(int)) != 0)
            {
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());
                try
                {
                    Marshal.StructureToPtr(new Accent { State = 5 }, pointer, false);
                    var data = new Data { Attribute = 19, DataPointer = pointer, Size = Marshal.SizeOf<Accent>() };
                    if (SetWindowCompositionAttribute(handle, ref data) == 0) throw new NotSupportedException("系统没有实时背景合成支持");
                }
                finally { Marshal.FreeHGlobal(pointer); }
            }
            window.EnsureLiveBackdrop(); window.BackdropMode = "实时系统毛玻璃";
        }
        catch (Exception e) when (e is COMException or NotSupportedException or EntryPointNotFoundException)
        { window.Root.Background = Ui.Surface; window.BackdropMode = "暗色兼容背景"; }
    }
}
