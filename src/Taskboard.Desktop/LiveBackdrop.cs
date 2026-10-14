using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Interop;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace Taskboard.Desktop;

internal readonly record struct GlassRegion(Rect Bounds, double VisibleFrom = 0, double Radius = 20);

// A single top-level HWND contains the compositor backdrop and one transparent
// WPF child surface. The child is an internal drawing/input surface, never an
// independently positioned background window. Both obey the parent's region.
internal sealed class LiveBackdrop : IDisposable
{
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface DesktopInterop
    { void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr result); }
    [StructLayout(LayoutKind.Sequential)] private struct QueueOptions { public int Size, Thread, Apartment; }
    [DllImport("coremessaging.dll")] private static extern int CreateDispatcherQueueController(QueueOptions options, out IntPtr controller);
    private static IntPtr queue;
    private static Compositor? compositor;
    private readonly DesktopWindowTarget target;
    private readonly ContainerVisual root;
    private CompositionBackdropBrush brush;
    private readonly List<IDisposable> shapes = new();
    private GlassRegion[] previous = [];
    private double previousScale;
    private DesktopWindowTarget? animationTarget;
    private ContainerVisual? animationRoot;
    private CompositionDrawingSurface? animationSurface;
    private byte[]? preparedImage;
    private readonly List<IDisposable> animationParts = new();
    private CompositionScopedBatch? batch;
    private static CompositorPixels? pixels;
    internal static Compositor Compositor => compositor!;
    internal LiveBackdrop(IntPtr hwnd)
    {
        if (compositor is null)
        {
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(new QueueOptions { Size = 12, Thread = 2, Apartment = 2 }, out queue));
            compositor = new Compositor();
            // Resolve the vector animation projection during startup rather
            // than on the first draft-switch click.
            using var warmVector = compositor.CreateVector3KeyFrameAnimation();
            using var warmScalar = compositor.CreateScalarKeyFrameAnimation();
            using var warmEase = compositor.CreateLinearEasingFunction();
            warmVector.InsertKeyFrame(0, Vector3.Zero, warmEase); warmVector.InsertKeyFrame(1, Vector3.One, warmEase);
            warmScalar.InsertKeyFrame(0, 0, warmEase); warmScalar.InsertKeyFrame(1, 1, warmEase);
        }
        compositor.As<DesktopInterop>().CreateDesktopWindowTarget(hwnd, false, out var raw);
        try { target = MarshalInterface<DesktopWindowTarget>.FromAbi(raw); }
        finally { Marshal.Release(raw); }
        root = compositor.CreateContainerVisual(); target.Root = root; brush = compositor.CreateHostBackdropBrush();
    }
    internal void Update(IEnumerable<GlassRegion> regions, double scale)
    {
        var current = regions.ToArray(); if (scale == previousScale && current.SequenceEqual(previous)) return;
        previous = current; previousScale = scale;
        root.Children.RemoveAll(); foreach (var shape in shapes) shape.Dispose(); shapes.Clear();
        foreach (var region in current)
        {
            var bounds = region.Bounds; if (bounds.Width <= 0 || bounds.Height <= 0) continue;
            var holder = compositor!.CreateContainerVisual(); holder.Offset = new Vector3((float)(bounds.X * scale), (float)(bounds.Y * scale), 0);
            holder.Size = new Vector2((float)(bounds.Width * scale), (float)(bounds.Height * scale));
            var strip = compositor.CreateInsetClip((float)(region.VisibleFrom * scale), 0, 0, 0); holder.Clip = strip;
            var visual = compositor.CreateSpriteVisual(); visual.Size = holder.Size; visual.Brush = brush;
            var geometry = CreateShape(visual.Size,(float)(region.Radius*scale));
            var rounded = compositor.CreateGeometricClip(geometry); visual.Clip = rounded;
            holder.Children.InsertAtTop(visual); root.Children.InsertAtTop(holder);
            shapes.Add(holder); shapes.Add(strip); shapes.Add(visual); shapes.Add(rounded); shapes.Add(geometry);
        }
    }
    internal void Animate(IntPtr hwnd, byte[] image, int width, int height, bool horizontal, double from, double to, Action finished)
    {
        CreateAnimation(hwnd, image, width, height);
        var clip = compositor!.CreateInsetClip(); animationRoot!.Clip = clip;
        var property = horizontal ? "LeftInset" : "BottomInset";
        var extent = horizontal ? width : height;
        if (horizontal) clip.LeftInset = (float)(extent-from); else clip.BottomInset = (float)(extent-from);
        var animation = compositor.CreateScalarKeyFrameAnimation(); animation.Duration = TimeSpan.FromMilliseconds(100);
        animationParts.Add(clip); animationParts.Add(animation);
        animation.InsertKeyFrame(0,(float)(extent-from)); animation.InsertKeyFrame(1,(float)(extent-to),compositor.CreateLinearEasingFunction());
        root.Opacity = 0;
        batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_,_) => finished(); clip.StartAnimation(property,animation); batch.End();
        _ = compositor.RequestCommitAsync();
    }
    private void CreateAnimation(IntPtr hwnd, byte[] image, int width, int height, bool attach = true)
    {
        CancelAnimation(); Prepare(image,width,height);
        compositor!.As<DesktopInterop>().CreateDesktopWindowTarget(hwnd,true,out var raw);
        try { animationTarget = MarshalInterface<DesktopWindowTarget>.FromAbi(raw); } finally { Marshal.Release(raw); }
        animationRoot = compositor!.CreateContainerVisual(); animationRoot.Size = new Vector2(width,height);
        if (attach) animationTarget.Root = animationRoot;
        foreach (var region in previous)
        {
            var holder = compositor!.CreateContainerVisual(); var bounds = region.Bounds;
            holder.Offset = new Vector3((float)(bounds.X*previousScale),(float)(bounds.Y*previousScale),0);
            holder.Size = new Vector2((float)(bounds.Width*previousScale),(float)(bounds.Height*previousScale));
            holder.Clip = compositor.CreateInsetClip((float)(region.VisibleFrom*previousScale),0,0,0);
            var sprite = compositor.CreateSpriteVisual(); sprite.Size = holder.Size; sprite.Brush = brush;
            var shape = CreateShape(sprite.Size,(float)(region.Radius*previousScale));
            sprite.Clip = compositor.CreateGeometricClip(shape); holder.Children.InsertAtTop(sprite); animationRoot.Children.InsertAtTop(holder);
            animationParts.Add(holder.Clip); animationParts.Add(sprite.Clip); animationParts.Add(shape); animationParts.Add(sprite); animationParts.Add(holder);
        }
        var ui = compositor!.CreateSpriteVisual(); ui.Size = new Vector2(width,height); ui.Brush = compositor.CreateSurfaceBrush(animationSurface); animationParts.Add(ui.Brush); animationParts.Add(ui);
        animationRoot.Children.InsertAtTop(ui);
    }
    internal void AnimateScroll(IntPtr hwnd, byte[] image, int width, int height, double from, double to, double durationMs, Action finished)
    {
        CreateAnimation(hwnd, image, width, height);
        var content = animationRoot!;
        var scene = compositor!.CreateContainerVisual(); scene.Size = content.Size;
        animationTarget!.Root = scene; scene.Children.InsertAtBottom(content); animationRoot = scene;
        animationParts.Add(content);
        var clip = compositor.CreateInsetClip(); content.Clip = clip; animationParts.Add(clip);
        clip.LeftInset = clip.RightInset = (float)(width * (1 - ScrollMotion.Aperture(from)) / 2);

        // The white core and broad halo share the same compositor timeline.
        // They stay outside the content aperture, so the closed window has no panel.
        var flare = compositor.CreateContainerVisual(); flare.Size = scene.Size;
        flare.Opacity = (float)ScrollMotion.Light(from); scene.Children.InsertAtTop(flare); animationParts.Add(flare);
        AddLight(28, 46); AddLight(8, 100); AddLight(2, 255);
        void AddLight(float lineWidth, byte alpha)
        {
            var sprite = compositor!.CreateSpriteVisual();
            sprite.Size = new Vector2((float)(lineWidth * previousScale), height);
            sprite.Offset = new Vector3((width - sprite.Size.X) / 2, 0, 0);
            var glow = compositor.CreateLinearGradientBrush(); glow.StartPoint = new Vector2(0, .5f); glow.EndPoint = new Vector2(1, .5f);
            glow.ColorStops.Add(compositor.CreateColorGradientStop(0, Windows.UI.Color.FromArgb(0, 235, 246, 255)));
            glow.ColorStops.Add(compositor.CreateColorGradientStop(.5f, Windows.UI.Color.FromArgb(alpha, 255, 255, 255)));
            if (alpha == 255)
            {
                glow.ColorStops.Add(compositor.CreateColorGradientStop(.25f, Windows.UI.Color.FromArgb(alpha, 255, 255, 255)));
                glow.ColorStops.Add(compositor.CreateColorGradientStop(.75f, Windows.UI.Color.FromArgb(alpha, 255, 255, 255)));
            }
            glow.ColorStops.Add(compositor.CreateColorGradientStop(1, Windows.UI.Color.FromArgb(0, 235, 246, 255)));
            sprite.Brush = glow;
            var shape = CreateShape(sprite.Size, sprite.Size.X / 2);
            sprite.Clip = compositor.CreateGeometricClip(shape); flare.Children.InsertAtTop(sprite);
            animationParts.Add(glow); animationParts.Add(sprite.Clip); animationParts.Add(shape); animationParts.Add(sprite);
        }
        var inset = compositor.CreateScalarKeyFrameAnimation(); inset.Duration = TimeSpan.FromMilliseconds(durationMs);
        var light = compositor.CreateScalarKeyFrameAnimation(); light.Duration = inset.Duration;
        var linear = compositor.CreateLinearEasingFunction(); animationParts.Add(linear); animationParts.Add(inset); animationParts.Add(light);
        // Keyframes are produced once; Windows owns every displayed animation frame.
        for (var i = 0; i <= 64; i++)
        {
            var fraction = i / 64f; var progress = from + (to - from) * fraction;
            inset.InsertKeyFrame(fraction, (float)(width * (1 - ScrollMotion.Aperture(progress)) / 2), linear);
            light.InsertKeyFrame(fraction, (float)ScrollMotion.Light(progress), linear);
        }
        root.Opacity = 0;
        batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_,_) => finished();
        clip.StartAnimation("LeftInset", inset); clip.StartAnimation("RightInset", inset); flare.StartAnimation("Opacity", light); batch.End();
        _ = compositor.RequestCommitAsync();
    }
    internal void CancelAnimation()
    {
        batch?.Dispose(); batch = null; animationTarget?.Dispose(); animationTarget=null;
        animationRoot?.Dispose(); animationRoot = null; foreach (var part in animationParts) part.Dispose(); animationParts.Clear(); root.Opacity = 1;
    }
    internal void AnimateSwap(IntPtr hwnd, byte[] beforeCard, byte[] afterCard, byte[] after, int width, int height,
        double paneWidth, double selectedSlot, double paddingX, double paddingY, Action ready, Action finished)
    {
        CreateAnimation(hwnd, after, width, height, attach: false);
        animationRoot!.Children.RemoveAll();
        animationRoot.Size = new Vector2((float)(width + 2 * paddingX), (float)(height + paddingY));
        // The ghost and other draft strips stay in place. Only two card visuals
        // move; each carries its own shaped live backdrop, never a desktop photo.
        var rest = compositor!.CreateContainerVisual(); rest.Offset = new Vector3((float)paddingX, (float)paddingY, 0);
        rest.Size = new Vector2(width, height); rest.Clip = compositor.CreateInsetClip((float)(paneWidth * 1.2), 0, 0, 0);
        animationParts.Add(rest); animationParts.Add(rest.Clip);
        foreach (var region in previous.Where(x => x.Bounds.X * previousScale >= paneWidth * .39))
        {
            var holder = compositor.CreateContainerVisual(); holder.Offset = new Vector3((float)(region.Bounds.X * previousScale), 0, 0);
            holder.Size = new Vector2((float)paneWidth, height); holder.Clip = compositor.CreateInsetClip((float)(region.VisibleFrom * previousScale), 0, 0, 0);
            AddBackdrop(holder); rest.Children.InsertAtTop(holder); animationParts.Add(holder); animationParts.Add(holder.Clip);
        }
        var unchanged = compositor.CreateSpriteVisual(); unchanged.Size = new Vector2(width, height); unchanged.Brush = compositor.CreateSurfaceBrush(animationSurface);
        rest.Children.InsertAtTop(unchanged); animationRoot.Children.InsertAtBottom(rest); animationParts.Add(unchanged); animationParts.Add(unchanged.Brush);
        var cardPixels = Math.Min(width, (int)Math.Ceiling(paneWidth));
        var outgoing = Card(beforeCard); var incoming = Card(afterCard);
        animationRoot.Children.InsertAtTop(outgoing); animationRoot.Children.InsertAtTop(incoming);
        var oldClip = compositor.CreateInsetClip(); var newClip = compositor.CreateInsetClip(); outgoing.Clip = oldClip; incoming.Clip = newClip;
        var oldMove = compositor.CreateVector3KeyFrameAnimation(); var newMove = compositor.CreateVector3KeyFrameAnimation();
        var oldInset = compositor.CreateScalarKeyFrameAnimation(); var newInset = compositor.CreateScalarKeyFrameAnimation();
        foreach (var animation in new CompositionAnimation[] { oldMove, newMove, oldInset, newInset }) animationParts.Add(animation);
        oldMove.Duration = newMove.Duration = oldInset.Duration = newInset.Duration = TimeSpan.FromMilliseconds(DraftSwitchMotion.DurationMs);
        var linear = compositor.CreateLinearEasingFunction(); animationParts.Add(linear); animationParts.Add(oldClip); animationParts.Add(newClip);
        // Keyframes are uploaded once; the compositor presents the movement at
        // the display refresh rate independently of input/layout/persistence.
        for (var i = 0; i <= 40; i++)
        {
            var fraction = i / 40f;
            var pose = DraftSwitchMotion.Pose(fraction, paneWidth / previousScale, selectedSlot / previousScale);
            var oldX = pose.OldX * previousScale; var newX = pose.NewX * previousScale;
            var oldOffset = new Vector3((float)(paddingX + oldX), (float)paddingY, 0);
            var newOffset = new Vector3((float)(paddingX + newX), (float)(paddingY + pose.NewY * previousScale), 0);
            var oldLeft = oldX > newX ? Math.Clamp(paneWidth + newX - oldX, 0, paneWidth) : 0;
            var newLeft = newX > oldX ? Math.Clamp(paneWidth + oldX - newX, 0, paneWidth) : 0;
            oldMove.InsertKeyFrame(fraction, oldOffset, linear); newMove.InsertKeyFrame(fraction, newOffset, linear);
            oldInset.InsertKeyFrame(fraction, (float)oldLeft, linear); newInset.InsertKeyFrame(fraction, (float)newLeft, linear);
            if (i == 0) { outgoing.Offset = oldOffset; incoming.Offset = newOffset; oldClip.LeftInset = (float)oldLeft; newClip.LeftInset = (float)newLeft; }
        }
        ready(); root.Opacity = 0; animationTarget!.Root = animationRoot;
        batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation); batch.Completed += (_, _) => finished();
        outgoing.StartAnimation("Offset", oldMove); incoming.StartAnimation("Offset", newMove);
        oldClip.StartAnimation("LeftInset", oldInset); newClip.StartAnimation("LeftInset", newInset);
        batch.End(); _ = compositor.RequestCommitAsync();
        ContainerVisual Card(byte[] source)
        {
            var holder = compositor!.CreateContainerVisual(); holder.Size = new Vector2((float)paneWidth, height); AddBackdrop(holder);
            var surface = pixels!.Upload(source, cardPixels, height); var paint = compositor.CreateSurfaceBrush(surface);
            var ui = compositor.CreateSpriteVisual(); ui.Size = holder.Size; ui.Brush = paint;
            holder.Children.InsertAtTop(ui); animationParts.Add(surface); animationParts.Add(paint); animationParts.Add(ui); animationParts.Add(holder);
            return holder;
        }
        void AddBackdrop(ContainerVisual holder)
        {
            var geometry = CreateShape(holder.Size, (float)(20 * previousScale)); var rounded = compositor!.CreateGeometricClip(geometry);
            var glass = compositor.CreateSpriteVisual(); glass.Size = holder.Size; glass.Brush = brush; glass.Clip = rounded;
            holder.Children.InsertAtBottom(glass); animationParts.Add(geometry); animationParts.Add(rounded); animationParts.Add(glass);
        }
    }
    internal void AlignAnimation(double x) { if(animationRoot is not null) animationRoot.Offset=new Vector3((float)x,0,0); }
    internal void SetVisible(bool visible) {root.Opacity=visible?1:0;_=compositor!.RequestCommitAsync();}
    internal void RefreshSource()
    {
        var regions=previous; var scale=previousScale; previous=[]; previousScale=0;
        brush.Dispose(); brush=compositor!.CreateHostBackdropBrush(); Update(regions,scale); _=compositor.RequestCommitAsync();
    }
    internal void Prepare(byte[] image, int width, int height)
    {
        if (ReferenceEquals(preparedImage,image)) return;
        pixels ??= new CompositorPixels(compositor!); animationSurface?.Dispose(); animationSurface = pixels.Upload(image,width,height); preparedImage = image;
    }
    private static CompositionGeometry CreateShape(Vector2 size,float radius)
    {
        if (Math.Abs(size.X-size.Y)<.01f && radius>=size.X/2-.01f)
        { var circle=compositor!.CreateEllipseGeometry(); circle.Center=size/2; circle.Radius=size/2; return circle; }
        var rectangle=compositor!.CreateRoundedRectangleGeometry(); rectangle.Size=size; rectangle.CornerRadius=new Vector2(radius); return rectangle;
    }
    public void Dispose()
    { CancelAnimation(); animationTarget?.Dispose(); animationSurface?.Dispose(); root.Children.RemoveAll(); foreach (var shape in shapes) shape.Dispose(); shapes.Clear(); brush.Dispose(); root.Dispose(); target.Dispose(); }
}

internal sealed class GlassContentHost : HwndHost
{
    internal bool AnimationHidden { get; set; }
    private readonly FrameworkElement root;
    private HwndSource? source;
    internal IntPtr ContentHandle => source?.Handle ?? IntPtr.Zero;
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd,int command);
    internal GlassContentHost(FrameworkElement root) { this.root = root; }
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var parameters = new HwndSourceParameters("Taskboard content") { ParentWindow = parent.Handle,
            WindowStyle = 0x40000000 | (AnimationHidden ? 0 : 0x10000000), UsesPerPixelTransparency = true, TreatAsInputRoot = true,
            Width = Math.Max(1, (int)ActualWidth), Height = Math.Max(1, (int)ActualHeight) };
        source = new HwndSource(parameters); source.RootVisual = root;
        return new HandleRef(this, source.Handle);
    }
    protected override void DestroyWindowCore(HandleRef hwnd) { source?.Dispose(); source = null; }
    protected override void OnWindowPositionChanged(Rect rectangle)
    { base.OnWindowPositionChanged(rectangle); if (AnimationHidden && ContentHandle != IntPtr.Zero) ShowWindow(ContentHandle,0); }
    protected override IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x003D && lParam.ToInt64() == AutomationInteropProvider.RootObjectId)
        {
            if (root is SurfaceContentRoot contentRoot)
            {
                handled = true;
                return AutomationInteropProvider.ReturnRawElementProvider(hwnd, wParam, lParam, contentRoot.NativeProvider());
            }
        }
        return base.WndProc(hwnd, message, wParam, lParam, ref handled);
    }
    protected override bool TabIntoCore(TraversalRequest request) => root.MoveFocus(request);
    protected override bool TranslateAcceleratorCore(ref MSG message, ModifierKeys modifiers)
        => source is not null && ((IKeyboardInputSink)source).TranslateAccelerator(ref message, modifiers);
    // The child is a WPF input root, so it processes its own keyboard/IME queue.
    // Letting the parent's source claim this focus would consume WM_CHAR with
    // the parent's input source before the focused child's provider can see it.
    protected override bool HasFocusWithinCore() => false;
}
