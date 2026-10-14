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

internal class SurfaceWindow : Window
{
    public readonly Grid Root = new SurfaceContentRoot();
    private bool allowClose;
    private bool animating;
    protected virtual bool ScrollEnabled => false;
    private bool scrollRunning, destroyAfterScroll;
    private double scrollFrom, scrollTo, scrollDuration;
    private readonly Stopwatch scrollClock = new();
    internal bool IsScrollAnimating => scrollRunning;
    internal bool IsContentSwapping { get; private set; }
    internal long ContentSwapCount { get; private set; }
    internal double LastContentSwapPreparationMs { get; private set; }
    private Rect? swapBounds;
    internal Point SwapInputOffset { get; private set; }
    private void RestoreSwapBounds()
    {
        if (swapBounds is not Rect original) return;
        swapBounds = null; SwapInputOffset = new Point();
        Left = original.Left; Top = original.Top; Width = original.Width; Height = original.Height;
        Root.UpdateLayout(); UpdateGlassGeometry(); Native.ClipWindow(this, Height); imageDirty = true;
    }
    internal bool ShouldRestore => IsVisible && (!scrollRunning || scrollTo > 0);
    internal double ScrollProgress => scrollRunning ? ScrollMotion.Progress(scrollFrom, scrollTo, scrollClock.Elapsed.TotalMilliseconds, scrollDuration) : IsVisible ? 1 : 0;
    public new void Show()
    {
        if (destroyAfterScroll) return;
        if (ScrollEnabled && (!IsVisible || scrollRunning)) { RevealScroll(true); return; }
        ShowImmediately();
    }
    private void ShowImmediately() {if(!animating) {Root.Opacity=1;backdrop?.SetVisible(true);} base.Show();if(!animating)Native.ShowWindow(ContentHandle,4);}
    public new void Hide()
    {
        if (ScrollEnabled && IsVisible) { RevealScroll(false); return; }
        HideImmediately();
    }
    private void HideImmediately()
    {motion++;animating=false;scrollRunning=false;IsContentSwapping=false;contentHost.AnimationHidden=false;animationDeadline?.Stop();CancelHandoff();backdrop?.CancelAnimation();RestoreSwapBounds();backdrop?.SetVisible(false);Root.Opacity=0;Native.ShowWindow(ContentHandle,0);base.Hide();}
    public bool GlassEnabled { get; set; } = true;
    public int WindowLayer { get; set; } = 2;
    public static double Transparency { get; private set; } = 20;
    public string BackdropMode { get; internal set; } = "未初始化";
    internal int NativeHideCount { get; private set; }
    public long ActivationOrder { get; private set; }
    private static long activationSequence;
    private long motion;
    private DispatcherTimer? animationDeadline;
    private EventHandler? handoff;
    private void CancelHandoff() { if(handoff is not null) CompositionTarget.Rendering-=handoff; handoff=null; }
    private byte[]? animationImage;
    private int imageWidth, imageHeight;
    private bool imageDirty = true;
    internal double LastAnimationPreparationMs { get; private set; }
    internal double LastSnapshotMs { get; private set; }
    internal double LastShowMs { get; private set; }
    internal bool UsesIndependentAnimation { get; private set; }
    private LiveBackdrop? backdrop;
    private readonly GlassContentHost contentHost;
    internal IntPtr ContentHandle => contentHost.ContentHandle;
    internal virtual double GlassTransparency => Transparency;
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
        Root.Background = Ui.Surface;
        Root.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 16, 16);
        contentHost = new GlassContentHost(Root); Content = contentHost;
        SourceInitialized += (_, _) =>
        {
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
        Closed += (_, _) => { CancelHandoff(); animationDeadline?.Stop(); backdrop?.Dispose(); backdrop = null; };
        Closing += (_, e) => { if (!allowClose) { e.Cancel = true; Hide(); } };
    }
    private IntPtr CompositionChanged(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x031E && GlassEnabled) Native.TryGlass(this);
        if (message == 0x0018 && wParam == IntPtr.Zero) NativeHideCount++;
        if (message == 0x0014) { handled = true; return new IntPtr(1); }
        if (message == 0x0202 && animating && this is BallSurface ball)
        {
            var packed = lParam.ToInt64(); var scale = Native.GetDpiForWindow(hwnd)/96.0;
            var x = (short)(packed & 0xffff)/scale; var y = (short)((packed >> 16)&0xffff)/scale;
            if (Math.Pow(x-(Width-29),2)+Math.Pow(y-29,2)<=29*29) { ball.ClickDuringAnimation(); handled=true; }
        }
        if (message == 0x0084 && this is BallSurface)
        {
            var packed = lParam.ToInt64(); var p = new Point((short)(packed & 0xffff),(short)((packed >> 16)&0xffff));
            var bounds = Native.ScreenBounds(this); var radius = bounds.Height/2;
            var x = Math.Clamp(p.X,bounds.Left+radius,bounds.Right-radius); var y = bounds.Top+radius;
            if ((p.X-x)*(p.X-x)+(p.Y-y)*(p.Y-y)>radius*radius) { handled = true; return new IntPtr(-1); }
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
    public static void SetTransparency(double value)
    {
        Transparency = Math.Clamp(value, 0, 100);
        foreach (var window in Application.Current.Windows.OfType<SurfaceWindow>().ToArray()) window.RefreshGlass();
    }
    public virtual void RefreshGlass() { imageDirty = true; if (GlassEnabled && new WindowInteropHelper(this).Handle != IntPtr.Zero) Native.TryGlass(this); }
    internal void EnsureLiveBackdrop()
    { backdrop ??= new LiveBackdrop(new WindowInteropHelper(this).Handle); ApplyGlassTint(); UpdateGlassGeometry(); }
    protected virtual void ApplyGlassTint() => Root.Background = Ui.Brush($"#{(int)Math.Round(255 * (1 - GlassTransparency / 100)):X2}192333");
    internal void UpdateGlassGeometry()
    {
        var bounds = new Rect(0,0,Width,Height);
        if (Root.Clip is not RectangleGeometry old || old.Rect != bounds || old.RadiusX != GlassRadius)
        { Root.Clip = new RectangleGeometry(bounds,GlassRadius,GlassRadius); imageDirty = true; }
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) backdrop?.Update(GlassRegions(), Native.GetDpiForWindow(hwnd) / 96.0);
    }
    internal void InvalidateAnimation() => imageDirty = true;
    internal void SwapContent(Action change, double paneWidth, double selectedSlot)
    {
        if (!IsVisible || scrollRunning || backdrop is null) { change(); return; }
        var preparation = Stopwatch.StartNew();
        var generation = ++motion; CancelHandoff(); animationDeadline?.Stop(); backdrop.CancelAnimation();
        RestoreSwapBounds();
        var scale = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96.0;
        Root.Opacity = 1; WarmAnimation(); var oldWidth = imageWidth; var oldHeight = imageHeight;
        var beforeCard = ((DraftStackSurface)this).CaptureFrontCard(scale);
        change(); Root.Opacity = 1; WarmAnimation();
        var afterCard = ((DraftStackSurface)this).CaptureFrontCard(scale);
        if (oldWidth != imageWidth || oldHeight != imageHeight) { IsContentSwapping = false; animating = false; contentHost.AnimationHidden = false; Native.ShowWindow(ContentHandle, 4); return; }
        animating = IsContentSwapping = true; ContentSwapCount++; var completed = false;
        void Finish()
        {
            if (generation != motion || completed) return; completed = true; animationDeadline?.Stop(); animating = IsContentSwapping = false;
            RestoreSwapBounds();
            contentHost.AnimationHidden = false; Root.Opacity = 1; Native.ShowWindow(ContentHandle, 4);
            var frames = 0; handoff = (_, _) => { if (++frames < 2) return; CancelHandoff(); if (generation == motion) backdrop?.CancelAnimation(); };
            CompositionTarget.Rendering += handoff;
        }
        try
        {
            var padding = DraftSwitchMotion.Padding(paneWidth);
            backdrop.AnimateSwap(new WindowInteropHelper(this).Handle, beforeCard, afterCard, animationImage!, imageWidth, imageHeight,
                paneWidth * scale, selectedSlot * scale, padding * scale, DraftSwitchMotion.Lift * scale,
                () => {
                    swapBounds = new Rect(Left, Top, Width, Height); SwapInputOffset = new Point(padding, DraftSwitchMotion.Lift);
                    contentHost.AnimationHidden = true; Root.Opacity = 0; Native.ShowWindow(ContentHandle, 0);
                    Left -= padding; Top -= DraftSwitchMotion.Lift; Width += 2 * padding; Height += DraftSwitchMotion.Lift;
                    Native.ClipWindow(this, Height);
                },
                () => Dispatcher.BeginInvoke(new Action(Finish), DispatcherPriority.Render));
            contentHost.AnimationHidden = true; Root.Opacity = 0; Native.ShowWindow(ContentHandle, 0); UsesIndependentAnimation = true;
            animationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(DraftSwitchMotion.DurationMs + 60) };
            animationDeadline.Tick += (_, _) => Finish(); animationDeadline.Start();
        }
        catch (Exception e) when (e is COMException or NotSupportedException or InvalidOperationException)
        { backdrop.CancelAnimation(); UsesIndependentAnimation = false; Finish(); }
        LastContentSwapPreparationMs = preparation.Elapsed.TotalMilliseconds;
    }
    public WindowPosition Position() => new() { X = Left, Y = Top, Width = Width, Height = Height };
    public void Place(WindowPosition position)
    { Width = position.Width; Height = position.Height; Left = position.X; Top = position.Y; }

    public void Reveal(bool visible, bool animate)
    {
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
    private void RevealScroll(bool visible)
    {
        if (scrollRunning && scrollTo == (visible ? 1 : 0)) return;
        var from = ScrollProgress;
        if (!visible && from <= 0) { HideImmediately(); if (destroyAfterScroll) Destroy(false); return; }
        var generation = ++motion; IsContentSwapping = false;
        CancelHandoff(); animationDeadline?.Stop(); backdrop?.CancelAnimation();
        RestoreSwapBounds();
        // A fresh snapshot on each new transition includes section/option changes.
        if (!scrollRunning) imageDirty = true;
        scrollRunning = true; animating = true;
        scrollFrom = from; scrollTo = visible ? 1 : 0;
        scrollDuration = Math.Max(1, ScrollMotion.DurationMs * Math.Abs(scrollTo - from));
        UpdateGlassGeometry(); new WindowInteropHelper(this).EnsureHandle();
        IsHitTestVisible = visible;
        RunMotion(generation, false, from, scrollTo, () =>
        {
            scrollRunning = false; animating = false;
            if (!visible) { HideImmediately(); if (destroyAfterScroll) Destroy(false); }
            Native.ClipWindow(this, Height); IsHitTestVisible = true;
        }, scroll: true, durationMs: scrollDuration);
    }
    private double revealedWidth = 58;
    protected void RevealToLeft(bool expanded, Action prepare, Action complete)
    {
        var generation = ++motion; CancelHandoff(); animationDeadline?.Stop(); backdrop?.CancelAnimation(); animating = true;
        prepare(); UpdateGlassGeometry();
        var from = Math.Clamp(revealedWidth, 58, Width); var target = expanded ? Width : 58;
        RunMotion(generation,true,from,target,() =>
        { revealedWidth = target; complete(); animating = false; UpdateGlassGeometry(); Native.ClipWindow(this, Height); });
    }
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
    private void RunMotion(long generation, bool horizontal, double from, double to, Action complete, bool scroll = false, double durationMs = 100)
    {
        var watch = Stopwatch.StartNew();
        var completed = false;
        void Finish()
        {
            if (generation != motion || completed) return; completed = true; animationDeadline?.Stop();
            var oldWidth=Width; complete(); contentHost.AnimationHidden = false;
            if(!IsVisible) { Root.Opacity=0;Native.ShowWindow(ContentHandle,0);backdrop?.CancelAnimation();backdrop?.SetVisible(false);return; }
            Root.Opacity=1;Native.ShowWindow(ContentHandle,4);
            backdrop?.AlignAnimation((Width-oldWidth)*Native.GetDpiForWindow(new WindowInteropHelper(this).Handle)/96.0);
            var rendered=0;
            handoff=(_,_) => { if(++rendered<2) return; CancelHandoff(); if(generation==motion) backdrop?.CancelAnimation(); };
            CompositionTarget.Rendering+=handoff;
        }
        try
        {
            Root.Opacity=1; WarmAnimation(); LastSnapshotMs=watch.Elapsed.TotalMilliseconds; var hwnd = new WindowInteropHelper(this).Handle;
            if (backdrop is null) throw new NotSupportedException("当前系统没有窗口动画合成支持");
            Native.ClipWindow(this,Height);
            if (scroll)
                backdrop!.AnimateScroll(hwnd, animationImage!, imageWidth, imageHeight, from, to, durationMs,
                    () => Dispatcher.BeginInvoke(new Action(Finish), DispatcherPriority.Render));
            else
                backdrop!.Animate(hwnd,animationImage!,imageWidth,imageHeight,horizontal,from*Native.GetDpiForWindow(hwnd)/96.0,to*Native.GetDpiForWindow(hwnd)/96.0,
                    () => Dispatcher.BeginInvoke(new Action(Finish),DispatcherPriority.Render));
            contentHost.AnimationHidden = true; Root.Opacity=0; Native.ShowWindow(ContentHandle,0); var showClock=Stopwatch.StartNew(); if (!IsVisible) ShowImmediately(); LastShowMs=showClock.Elapsed.TotalMilliseconds;
            if (scroll) scrollClock.Restart();
            UsesIndependentAnimation = true;
            // Occluded/offscreen targets can defer compositor completion events.
            // This is a single end deadline, never a frame-production timer.
            animationDeadline = new DispatcherTimer(DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(durationMs + (scroll ? 60 : 0)) };
            animationDeadline.Tick += (_,_) => Finish(); animationDeadline.Start();
        }
        catch (Exception e) when (e is COMException or NotSupportedException or InvalidOperationException)
        { backdrop?.CancelAnimation(); if (to > 0 && !IsVisible) ShowImmediately(); UsesIndependentAnimation = false; Finish(); }
        LastAnimationPreparationMs = watch.Elapsed.TotalMilliseconds;
    }
}

internal static class Native
{
    internal static void BeginWorkspaceChange() => ordering=true;
    internal static void EndWorkspaceChange() {ordering=false;RaiseBall();}
    private static Window? ball;
    private static bool ordering;
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    internal static bool Cloak(IntPtr hwnd,bool hidden) {var value=hidden?1:0;return DwmSetWindowAttribute(hwnd,13,ref value,4)==0;}
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern IntPtr GetFocus();
    public static void SetBall(Window window) { ball = window; RaiseBall(); }
    public static void RaiseBall()
    {
        if (ordering || Application.Current is null) return; ordering = true;
        try
        {
            foreach (var window in Application.Current.Windows.OfType<SurfaceWindow>().Where(x => x.IsVisible).OrderBy(x => x.WindowLayer).ThenBy(x => x.ActivationOrder)) Raise(window);
            foreach (var dialog in Dialogs()) SetWindowPos(dialog, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            if (ball?.IsVisible == true) Raise(ball);
        }
        finally { ordering = false; }
    }
    public static void Raise(Window window)
    { if (window.IsVisible) SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13); }
    [StructLayout(LayoutKind.Sequential)] private struct Accent { public int State; public int Flags; public uint Color; public int Animation; }
    [StructLayout(LayoutKind.Sequential)] private struct Data { public int Attribute; public IntPtr DataPointer; public int Size; }
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref Data data);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
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
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out NativeRect bounds);
    internal static Rect ScreenBounds(Window window)
    { GetWindowRect(new WindowInteropHelper(window).Handle,out var r); return new Rect(r.Left,r.Top,Math.Max(0,r.Right-r.Left),Math.Max(0,r.Bottom-r.Top)); }
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    public static void ClipWindow(Window window, double height)
    {
        var hwnd = new WindowInteropHelper(window).Handle; if (hwnd == IntPtr.Zero) return;
        if (window is BallSurface) { SetWindowRgn(hwnd,IntPtr.Zero,false); return; }
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var diameter = window is BallSurface ? 58 : 40;
        var pixelWidth = (int)Math.Ceiling(window.Width * scale); var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
        var region = window is BallSurface && Math.Abs(window.Width - window.Height) < 1 ? CreateEllipticRgn(0, 0, pixelWidth, pixelHeight) : CreateRoundRectRgn(0, 0, pixelWidth + 1, pixelHeight + 1, (int)(diameter * scale), (int)(diameter * scale));
        if (SetWindowRgn(hwnd, region, false) == 0) DeleteObject(region);
    }
    internal static void ClipHorizontal(Window window, double visibleWidth)
    {
        var hwnd = new WindowInteropHelper(window).Handle; var scale = GetDpiForWindow(hwnd) / 96.0;
        var region = CreateRoundRectRgn((int)Math.Round((window.Width - visibleWidth) * scale), 0, (int)Math.Ceiling(window.Width * scale), (int)Math.Ceiling(window.Height * scale), (int)(window.Height * scale), (int)(window.Height * scale));
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
