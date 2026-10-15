using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
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

internal readonly record struct GlassRegion(Rect Bounds, double VisibleFrom = 0, double Radius = 20, bool FillCornerGaps = false);

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
    private readonly ContainerVisual displayRoot;
    private readonly ContainerVisual root;
    private ContainerVisual? shownAnimation;
    private CompositionBackdropBrush brush;
    private readonly List<IDisposable> shapes = new();
    private readonly Dictionary<(float Radius, bool Bottom, bool Left), CompositionDrawingSurface> cornerMasks = new();
    private GlassRegion[] previous = [];
    private double previousScale;
    private DesktopWindowTarget? animationTarget;
    private ContainerVisual? animationRoot;
    private CompositionDrawingSurface? animationSurface;
    private byte[]? preparedImage;
    private readonly List<IDisposable> animationParts = new();
    private readonly List<IDisposable> heldParts = new();
    private CompositionScopedBatch? batch;
    private CompositionPropertySet? scrollMotion;
    private long animationGeneration;
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
        displayRoot = compositor.CreateContainerVisual(); root = compositor.CreateContainerVisual();
        displayRoot.RelativeSizeAdjustment = Vector2.One;
        displayRoot.Children.InsertAtBottom(root); target.Root = displayRoot; brush = compositor.CreateHostBackdropBrush();
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
            AddRegionBackdrop(holder, region, scale, shapes); root.Children.InsertAtTop(holder); shapes.Add(holder);
        }
    }
    internal async Task Animate(IntPtr hwnd, byte[] image, int width, int height, bool horizontal, double from, double to, Action ready, Action finished)
    {
        CreateAnimation(hwnd, image, width, height, attach: false);
        if (horizontal)
        {
            // Morph a pill anchored at the right edge. A rectangular reveal
            // exposes square corners outside the original orb on frame one.
            var pill = compositor!.CreateRoundedRectangleGeometry();
            pill.Size = new Vector2((float)from, height); pill.Offset = new Vector2((float)(width - from), 0);
            pill.CornerRadius = new Vector2(height / 2f);
            var rounded = compositor.CreateGeometricClip(pill); animationRoot!.Clip = rounded;
            var size = compositor.CreateVector2KeyFrameAnimation(); var offset = compositor.CreateVector2KeyFrameAnimation();
            size.Duration = offset.Duration = TimeSpan.FromMilliseconds(100);
            var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(.25f, .1f), new Vector2(.25f, 1));
            size.InsertKeyFrame(0, new Vector2((float)from, height)); size.InsertKeyFrame(1, new Vector2((float)to, height), ease);
            offset.InsertKeyFrame(0, new Vector2((float)(width - from), 0)); offset.InsertKeyFrame(1, new Vector2((float)(width - to), 0), ease);
            animationParts.Add(pill); animationParts.Add(rounded); animationParts.Add(size); animationParts.Add(offset); animationParts.Add(ease);
            var generation = animationGeneration;
            // Commit the detached clipping resources before making the new
            // scene visible, so its first frame cannot be the full capsule.
            await compositor.RequestCommitAsync();
            if (generation != animationGeneration) return;
            root.Opacity = 0; AttachAnimation();
            // Present the initial circular silhouette before removing the
            // temporary native guard and starting the independent timeline.
            await compositor.RequestCommitAsync(); if (generation != animationGeneration) return;
            ready();
            batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation); batch.Completed += (_, _) => finished();
            pill.StartAnimation("Size", size); pill.StartAnimation("Offset", offset); batch.End(); _ = compositor.RequestCommitAsync(); return;
        }
        var clip = compositor!.CreateInsetClip(); animationRoot!.Clip = clip;
        var property = horizontal ? "LeftInset" : "BottomInset";
        var extent = horizontal ? width : height;
        if (horizontal) clip.LeftInset = (float)(extent-from); else clip.BottomInset = (float)(extent-from);
        var animation = compositor.CreateScalarKeyFrameAnimation(); animation.Duration = TimeSpan.FromMilliseconds(100);
        animationParts.Add(clip); animationParts.Add(animation);
        animation.InsertKeyFrame(0,(float)(extent-from)); animation.InsertKeyFrame(1,(float)(extent-to),compositor.CreateLinearEasingFunction());
        root.Opacity = 0; AttachAnimation();
        ready();
        batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_,_) => finished(); clip.StartAnimation(property,animation); batch.End();
        _ = compositor.RequestCommitAsync();
    }
    private void CreateAnimation(IntPtr hwnd, byte[] image, int width, int height, bool attach = true)
    {
        CancelAnimation(keepDisplay: true); Prepare(image,width,height);
        // Keep the backdrop in the same native composition target as the
        // resting window; a second topmost target changes its sampled background.
        animationTarget = target;
        animationRoot = compositor!.CreateContainerVisual(); animationRoot.Size = new Vector2(width,height);
        foreach (var region in previous)
        {
            var holder = compositor!.CreateContainerVisual(); var bounds = region.Bounds;
            holder.Offset = new Vector3((float)(bounds.X*previousScale),(float)(bounds.Y*previousScale),0);
            holder.Size = new Vector2((float)(bounds.Width*previousScale),(float)(bounds.Height*previousScale));
            AddRegionBackdrop(holder, region, previousScale, animationParts); animationRoot.Children.InsertAtTop(holder); animationParts.Add(holder);
        }
        var ui = compositor!.CreateSpriteVisual(); ui.Size = new Vector2(width,height); ui.Brush = compositor.CreateSurfaceBrush(animationSurface); animationParts.Add(ui.Brush); animationParts.Add(ui);
        animationRoot.Children.InsertAtTop(ui);
        if (attach) AttachAnimation();
    }
    internal async Task AnimateScroll(IntPtr hwnd, byte[] image, int width, int height, double from, double to, double durationMs, double initialLight, Action ready, Action finished, Action started)
    {
        CreateAnimation(hwnd, image, width, height, attach: false);
        var content = animationRoot!;
        var scene = compositor!.CreateContainerVisual(); scene.Size = content.Size;
        scene.Children.InsertAtBottom(content); animationRoot = scene;
        animationParts.Add(content);
        var clip = compositor.CreateInsetClip(); content.Clip = clip; animationParts.Add(clip);
        clip.LeftInset = clip.RightInset = (float)(width * (1 - ScrollMotion.Aperture(from)) / 2);

        // A single animated property controls width and glow. There is no
        // separate flash phase or independently scheduled opacity animation.
        var state = compositor.CreatePropertySet(); state.InsertScalar("Frame", 0); scrollMotion = state; animationParts.Add(state);
        var inset = compositor.CreateExpressionAnimation($"(1-(FromValue+(ToValue-FromValue)*{ScrollMotion.EaseExpression("motion.Frame")}))*HalfWidth");
        inset.SetReferenceParameter("motion", state); inset.SetScalarParameter("FromValue", (float)from);
        inset.SetScalarParameter("ToValue", (float)to); inset.SetScalarParameter("HalfWidth", width / 2f);
        clip.StartAnimation("LeftInset", inset); clip.StartAnimation("RightInset", inset); animationParts.Add(inset);
        var flare = compositor.CreateContainerVisual(); flare.Size = scene.Size;
        var opening = to > from || to == 1;
        flare.Opacity = (float)initialLight; scene.Children.InsertAtTop(flare); animationParts.Add(flare);
        var light = compositor.CreateExpressionAnimation(ScrollMotion.LightExpression(opening));
        light.SetReferenceParameter("motion", state); light.SetScalarParameter("InitialLight", (float)initialLight);
        flare.StartAnimation("Opacity", light); animationParts.Add(light);
        AddLight(28, 46); AddLight(8, 100); AddLight((float)ScrollMotion.CoreWidth, 255);
        void AddLight(float lineWidth, byte alpha)
        {
            var sprite = compositor!.CreateSpriteVisual();
            var pixelsWide = alpha == 255 ? Math.Max(1, Math.Floor(lineWidth * previousScale)) : lineWidth * previousScale;
            sprite.Size = new Vector2((float)pixelsWide, height);
            sprite.Offset = new Vector3((float)(alpha == 255 ? Math.Round((width - pixelsWide) / 2) : (width - pixelsWide) / 2), 0, 0);
            if (alpha == 255)
            {
                sprite.Brush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            }
            else
            {
                var glow = compositor.CreateLinearGradientBrush(); glow.StartPoint = new Vector2(0, .5f); glow.EndPoint = new Vector2(1, .5f);
                glow.ColorStops.Add(compositor.CreateColorGradientStop(0, Windows.UI.Color.FromArgb(0, 235, 246, 255)));
                glow.ColorStops.Add(compositor.CreateColorGradientStop(.5f, Windows.UI.Color.FromArgb(alpha, 255, 255, 255)));
                glow.ColorStops.Add(compositor.CreateColorGradientStop(1, Windows.UI.Color.FromArgb(0, 235, 246, 255))); sprite.Brush = glow;
            }
            var shape = CreateShape(sprite.Size, sprite.Size.X / 2);
            sprite.Clip = compositor.CreateGeometricClip(shape); flare.Children.InsertAtTop(sprite);
            animationParts.Add(sprite.Brush); animationParts.Add(sprite.Clip); animationParts.Add(shape); animationParts.Add(sprite);
        }
        var timeline = compositor.CreateScalarKeyFrameAnimation(); timeline.Duration = TimeSpan.FromMilliseconds(durationMs);
        var linear = compositor.CreateLinearEasingFunction(); timeline.InsertKeyFrame(0, 0, linear); timeline.InsertKeyFrame(1, 1, linear);
        animationParts.Add(timeline); animationParts.Add(linear);
        var generation = animationGeneration;
        // Keep the previous resting/animated scene until resources are ready.
        // Flush the shown target before starting: commit completion alone did
        // not guarantee a cold/reopened native target had presented a frame.
        await compositor.RequestCommitAsync(); if (generation != animationGeneration) return;
        root.Opacity = 0; AttachAnimation(); await compositor.RequestCommitAsync(); if (generation != animationGeneration) return;
        ready(); await compositor.RequestCommitAsync(); if (generation != animationGeneration) return;
        Native.DwmFlush(); if (generation != animationGeneration) return;
        batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_,_) => finished();
        started(); state.StartAnimation("Frame", timeline); batch.End();
        await compositor.RequestCommitAsync();
    }
    internal void FreezeScroll(double fraction)
    { if (scrollMotion is null) return; scrollMotion.StopAnimation("Frame"); scrollMotion.InsertScalar("Frame", (float)Math.Clamp(fraction, 0, 1)); }
    internal void CancelAnimation(bool keepDisplay = false)
    {
        animationGeneration++;
        scrollMotion = null;
        batch?.Dispose(); batch = null;
        if (!keepDisplay)
        {
            root.Opacity = 1;
            if (shownAnimation is not null) displayRoot.Children.Remove(shownAnimation);
            shownAnimation = null; animationTarget=null;
        }
        if (animationRoot is not null) heldParts.Add(animationRoot);
        animationRoot = null; heldParts.AddRange(animationParts); animationParts.Clear();
        if (!keepDisplay) { ReleaseHeld(); root.Opacity = 1; }
    }
    private void ReleaseHeld() { foreach (var part in heldParts) part.Dispose(); heldParts.Clear(); }
    private void AttachAnimation()
    {
        // The native target's root stays fixed. Replacing it can invalidate the
        // transparent input child for one frame during the final handoff.
        displayRoot.Children.InsertAtTop(animationRoot!);
        if (shownAnimation is not null) displayRoot.Children.Remove(shownAnimation);
        shownAnimation = animationRoot; ReleaseHeld();
    }
    internal async Task HoldContent(IntPtr hwnd, byte[] image, int width, int height, bool rightAnchored = false)
    {
        CreateAnimation(hwnd, image, width, height, attach: false);
        if (rightAnchored)
        {
            animationRoot!.AnchorPoint = new Vector2(1, 0);
            animationRoot.RelativeOffsetAdjustment = new Vector3(1, 0, 0);
        }
        root.Opacity = 0; AttachAnimation(); await compositor!.RequestCommitAsync();
    }
    internal async Task CommitRestingFrame()
    { root.Opacity = 1; await compositor!.RequestCommitAsync(); }
    internal async Task AnimateSwap(IntPtr hwnd, byte[] beforeCard, byte[] beforeSelectedCard, byte[] oldAfterCard, byte[] afterCard, byte[] after, int width, int height,
        double paneWidth, double selectedSlot, double paddingX, double paddingY, Action ready, Action finished, Action started)
    {
        CreateAnimation(hwnd, after, width, height, attach: false);
        animationRoot!.Children.RemoveAll();
        animationRoot.Size = new Vector2((float)(width + paneWidth + 12 * previousScale), height);
        // The ghost and other draft strips stay in place. Only two card visuals
        // move; each carries its own shaped live backdrop, never a desktop photo.
        var rest = compositor!.CreateContainerVisual(); rest.Offset = new Vector3((float)paddingX, (float)paddingY, 0);
        rest.Size = new Vector2(width, height); animationParts.Add(rest);
        foreach (var region in previous.Where(x => x.Bounds.X * previousScale >= paneWidth * .39))
        {
            var holder = compositor.CreateContainerVisual(); holder.Offset = new Vector3((float)(region.Bounds.X * previousScale), 0, 0);
            holder.Size = new Vector2((float)paneWidth, height);
            AddRegionBackdrop(holder, region, previousScale, animationParts, cornerTint: true);
            var source = compositor.CreateSurfaceBrush(animationSurface); source.Stretch = CompositionStretch.None;
            source.HorizontalAlignmentRatio = source.VerticalAlignmentRatio = 0; source.Offset = new Vector2((float)(-region.Bounds.X * previousScale), 0);
            var ui = compositor.CreateSpriteVisual(); ui.Size = holder.Size; ui.Brush = source;
            ui.Clip = compositor.CreateInsetClip((float)(region.VisibleFrom * previousScale), 0, 0, 0);
            holder.Children.InsertAtTop(ui); rest.Children.InsertAtTop(holder); animationParts.Add(holder); animationParts.Add(source); animationParts.Add(ui); animationParts.Add(ui.Clip);
        }
        var unchanged = compositor.CreateSpriteVisual(); unchanged.Size = new Vector2(width, height); unchanged.Brush = compositor.CreateSurfaceBrush(animationSurface);
        var lastRight = (float)(previous.Max(x => x.Bounds.Right) * previousScale);
        unchanged.Clip = compositor.CreateInsetClip(lastRight, 0, 0, 0); animationParts.Add(unchanged.Clip);
        rest.Children.InsertAtTop(unchanged); animationRoot.Children.InsertAtBottom(rest); animationParts.Add(unchanged); animationParts.Add(unchanged.Brush);
        // The shifted dashed outline is visible in the last card's two corner
        // gaps, while its hit area still starts at the original right edge.
        var outlineCorners = AddCornerPair(rest, (float)(20 * previousScale), left: false, tint: false, animationParts);
        outlineCorners.Offset = new Vector3(lastRight - (float)(20 * previousScale), 0, 0);
        foreach (var patch in outlineCorners.Children.OfType<ContainerVisual>())
        {
            var source = compositor.CreateSurfaceBrush(animationSurface); source.Stretch = CompositionStretch.None;
            source.HorizontalAlignmentRatio = source.VerticalAlignmentRatio = 0;
            source.Offset = new Vector2(-outlineCorners.Offset.X, -patch.Offset.Y); animationParts.Add(source);
            var mask = compositor.CreateSurfaceBrush(cornerMasks[((float)(20 * previousScale), patch.Offset.Y > 0, false)]); animationParts.Add(mask);
            var masked = compositor.CreateMaskBrush(); masked.Source = source; masked.Mask = mask; animationParts.Add(masked);
            patch.Children.OfType<SpriteVisual>().Single().Brush = masked;
        }
        var cardPixels = Math.Min(width, (int)Math.Ceiling(paneWidth));
        var roleFades = new List<(SpriteVisual Visual, ScalarKeyFrameAnimation Animation)>();
        var outgoing = Card(beforeCard, oldAfterCard); var incoming = Card(beforeSelectedCard, afterCard);
        animationRoot.Children.InsertAtTop(outgoing.Holder); animationRoot.Children.InsertAtTop(incoming.Holder);
        var oldMove = compositor.CreateVector3KeyFrameAnimation(); var newMove = compositor.CreateVector3KeyFrameAnimation();
        var oldInset = compositor.CreateScalarKeyFrameAnimation(); var oldRightInset = compositor.CreateScalarKeyFrameAnimation(); var newInset = compositor.CreateScalarKeyFrameAnimation();
        var oldLeftCornerMove = compositor.CreateVector3KeyFrameAnimation(); var oldRightCornerMove = compositor.CreateVector3KeyFrameAnimation(); var newCornerMove = compositor.CreateVector3KeyFrameAnimation();
        var oldLeftCornerOpacity = compositor.CreateScalarKeyFrameAnimation(); var oldRightCornerOpacity = compositor.CreateScalarKeyFrameAnimation();
        foreach (var animation in new KeyFrameAnimation[] { oldMove, newMove, oldInset, oldRightInset, newInset, oldLeftCornerMove, oldRightCornerMove, newCornerMove, oldLeftCornerOpacity, oldRightCornerOpacity })
        { animation.Duration = TimeSpan.FromMilliseconds(DraftSwitchMotion.DurationMs); animationParts.Add(animation); }
        var linear = compositor.CreateLinearEasingFunction(); animationParts.Add(linear);
        // Keyframes are uploaded once; the compositor presents the movement at
        // the display refresh rate independently of input/layout/persistence.
        foreach (var fraction in Enumerable.Range(0, 81).Select(i => i / 80f).Concat(new[] { 150f / 360, 190f / 360, 250f / 360 }).Distinct().Order())
        {
            var pose = DraftSwitchMotion.Pose(fraction, paneWidth / previousScale, selectedSlot / previousScale);
            var oldX = pose.OldX * previousScale; var newX = pose.NewX * previousScale;
            var oldOffset = new Vector3((float)(paddingX + oldX), (float)paddingY, 0);
            var newOffset = new Vector3((float)(paddingX + newX), (float)(paddingY + pose.NewY * previousScale), 0);
            var frontOffset = newX - oldX; var extracted = fraction >= 150f / 360;
            var oldLeft = extracted && frontOffset < 0 ? Math.Clamp(paneWidth + frontOffset, 0, paneWidth) : 0;
            var oldRight = extracted && frontOffset >= 0 ? Math.Clamp(paneWidth - frontOffset, 0, paneWidth) : 0;
            // After extraction the selected card is in the front layer. Its
            // old preview clipping must not follow it on the return stroke.
            var newLeft = fraction < 150f / 360 && newX > oldX ? Math.Clamp(paneWidth + oldX - newX, 0, paneWidth) : 0;
            oldMove.InsertKeyFrame(fraction, oldOffset, linear); newMove.InsertKeyFrame(fraction, newOffset, linear);
            oldInset.InsertKeyFrame(fraction, (float)oldLeft, linear); oldRightInset.InsertKeyFrame(fraction, (float)oldRight, linear); newInset.InsertKeyFrame(fraction, (float)newLeft, linear);
            var radius = (float)(20 * previousScale);
            oldLeftCornerMove.InsertKeyFrame(fraction, new Vector3((float)frontOffset, 0, 0), linear);
            oldRightCornerMove.InsertKeyFrame(fraction, new Vector3((float)(paneWidth + frontOffset - radius), 0, 0), linear);
            newCornerMove.InsertKeyFrame(fraction, new Vector3((float)newLeft - radius, 0, 0), linear);
            oldLeftCornerOpacity.InsertKeyFrame(fraction, extracted && frontOffset >= 0 ? 1 : 0, linear);
            oldRightCornerOpacity.InsertKeyFrame(fraction, extracted && frontOffset < 0 ? 1 : 0, linear);
            if (fraction == 0)
            {
                outgoing.Holder.Offset = oldOffset; incoming.Holder.Offset = newOffset;
                outgoing.Main.LeftInset = (float)oldLeft; outgoing.Main.RightInset = (float)oldRight; incoming.Main.LeftInset = (float)newLeft;
                outgoing.Left.Opacity = outgoing.Right.Opacity = 0; incoming.Left.Opacity = 0;
                incoming.Right.Offset = new Vector3((float)newLeft - radius, 0, 0);
            }
        }
        var generation = animationGeneration;
        ready(); root.Opacity = 0; AttachAnimation();
        await compositor.RequestCommitAsync(); if (generation != animationGeneration) return;
        batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation); batch.Completed += (_, _) => finished();
        started(); outgoing.Holder.StartAnimation("Offset", oldMove); incoming.Holder.StartAnimation("Offset", newMove);
        outgoing.Main.StartAnimation("LeftInset", oldInset); outgoing.Main.StartAnimation("RightInset", oldRightInset); incoming.Main.StartAnimation("LeftInset", newInset);
        outgoing.Left.StartAnimation("Offset", oldLeftCornerMove); outgoing.Right.StartAnimation("Offset", oldRightCornerMove); incoming.Right.StartAnimation("Offset", newCornerMove);
        outgoing.Left.StartAnimation("Opacity", oldLeftCornerOpacity); outgoing.Right.StartAnimation("Opacity", oldRightCornerOpacity);
        foreach (var (visual, animation) in roleFades) visual.StartAnimation("Opacity", animation);
        batch.End(); await compositor.RequestCommitAsync();
        (ContainerVisual Holder, InsetClip Main, ContainerVisual Left, ContainerVisual Right) Card(byte[] source, byte[] settled)
        {
            var holder = compositor!.CreateContainerVisual(); holder.Size = new Vector2((float)paneWidth, height); ClipRounded(holder, (float)(20 * previousScale), animationParts);
            var content = compositor.CreateContainerVisual(); content.Size = holder.Size; content.Clip = compositor.CreateInsetClip();
            var glass = compositor.CreateSpriteVisual(); glass.Size = holder.Size; glass.Brush = brush; content.Children.InsertAtBottom(glass); animationParts.Add(glass);
            var tint = compositor.CreateSpriteVisual(); tint.Size = holder.Size; tint.Brush = CardTint(animationParts); content.Children.InsertAtTop(tint); animationParts.Add(tint);
            var oldUi = Image(source); var newUi = Image(settled); newUi.Opacity = 0;
            content.Children.InsertAtTop(oldUi); content.Children.InsertAtTop(newUi); holder.Children.InsertAtTop(content);
            var fadeOut = compositor.CreateScalarKeyFrameAnimation(); var fadeIn = compositor.CreateScalarKeyFrameAnimation();
            fadeOut.Duration = fadeIn.Duration = TimeSpan.FromMilliseconds(DraftSwitchMotion.DurationMs);
            fadeOut.InsertKeyFrame(0, 1); fadeOut.InsertKeyFrame(150f / 360, 1); fadeOut.InsertKeyFrame(190f / 360, 0); fadeOut.InsertKeyFrame(1, 0);
            fadeIn.InsertKeyFrame(0, 0); fadeIn.InsertKeyFrame(150f / 360, 0); fadeIn.InsertKeyFrame(190f / 360, 1); fadeIn.InsertKeyFrame(1, 1);
            roleFades.Add((oldUi, fadeOut)); roleFades.Add((newUi, fadeIn));
            animationParts.Add(fadeOut); animationParts.Add(fadeIn); animationParts.Add(content.Clip); animationParts.Add(content); animationParts.Add(holder);
            var left = AddCornerPair(holder, (float)(20 * previousScale), left: true, tint: true, animationParts);
            var right = AddCornerPair(holder, (float)(20 * previousScale), left: false, tint: true, animationParts);
            return (holder, (InsetClip)content.Clip, left, right);
            SpriteVisual Image(byte[] bytes)
            {
                var surface = pixels!.Upload(bytes, cardPixels, height); var paint = compositor!.CreateSurfaceBrush(surface);
                var ui = compositor.CreateSpriteVisual(); ui.Size = holder.Size; ui.Brush = paint;
                animationParts.Add(surface); animationParts.Add(paint); animationParts.Add(ui); return ui;
            }
        }
    }
    private void ClipRounded(ContainerVisual holder, float radius, List<IDisposable> parts)
    {
        var geometry = CreateShape(holder.Size, radius); var clip = compositor!.CreateGeometricClip(geometry); holder.Clip = clip;
        parts.Add(geometry); parts.Add(clip);
    }
    private void AddRegionBackdrop(ContainerVisual holder, GlassRegion region, double scale, List<IDisposable> parts, bool cornerTint = false)
    {
        var radius = (float)(region.Radius * scale); var cut = (float)(region.VisibleFrom * scale);
        ClipRounded(holder, radius, parts);
        var glass = compositor!.CreateSpriteVisual(); glass.Size = holder.Size; glass.Brush = brush;
        var clip = compositor.CreateInsetClip(cut, 0, 0, 0); glass.Clip = clip;
        holder.Children.InsertAtBottom(glass); parts.Add(clip); parts.Add(glass);
        if (region.FillCornerGaps && cut > 0)
        {
            var corners = AddCornerPair(holder, radius, left: false, tint: cornerTint, parts);
            corners.Offset = new Vector3(cut - radius, 0, 0);
        }
    }
    private CompositionColorBrush CardTint(List<IDisposable> parts)
    {
        var color = Windows.UI.Color.FromArgb((byte)Math.Round(255 * (1 - SurfaceWindow.FloatingTransparency / 100)), 25, 35, 51);
        var tint = compositor!.CreateColorBrush(color); parts.Add(tint); return tint;
    }
    private ContainerVisual AddCornerPair(ContainerVisual holder, float radius, bool left, bool tint, List<IDisposable> parts)
    {
        var factory = compositor!;
        var pair = factory.CreateContainerVisual(); pair.Size = new Vector2(radius, holder.Size.Y); parts.Add(pair);
        var tintBrush = tint ? CardTint(parts) : null;
        for (var i = 0; i < 2; i++)
        {
            var patch = factory.CreateContainerVisual(); patch.Size = new Vector2(radius); patch.Offset = new Vector3(0, i == 0 ? 0 : holder.Size.Y - radius, 0); parts.Add(patch);
            var key = (radius, i != 0, left);
            if (!cornerMasks.TryGetValue(key, out var surface))
            {
                var size = Math.Max(1, (int)Math.Ceiling(radius));
                var corner = new System.Windows.Media.CombinedGeometry(System.Windows.Media.GeometryCombineMode.Exclude,
                    new System.Windows.Media.RectangleGeometry(new Rect(0, 0, radius, radius)),
                    new System.Windows.Media.EllipseGeometry(new Point(left ? radius : 0, i == 0 ? radius : 0), radius, radius));
                var drawing = new System.Windows.Media.DrawingVisual();
                using (var paint = drawing.RenderOpen()) paint.DrawGeometry(System.Windows.Media.Brushes.White, null, corner);
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); image.Render(drawing);
                var bytes = new byte[size * size * 4]; image.CopyPixels(bytes, size * 4, 0);
                pixels ??= new CompositorPixels(factory); surface = pixels.Upload(bytes, size, size); cornerMasks.Add(key, surface);
            }
            var mask = factory.CreateSurfaceBrush(surface); parts.Add(mask);
            // BackdropBrush is not a supported MaskBrush source. Its opaque
            // live sample can cover this tiny corner square under the front
            // card; only the dark tint needs the exact non-overlapping mask.
            var glass = factory.CreateSpriteVisual(); glass.Size = patch.Size; glass.Brush = brush; patch.Children.InsertAtTop(glass); parts.Add(glass);
            if (tintBrush is not null) AddMasked(tintBrush);
            pair.Children.InsertAtTop(patch);
            void AddMasked(CompositionBrush source)
            {
                var paint = factory.CreateMaskBrush(); paint.Source = source; paint.Mask = mask; parts.Add(paint);
                var sprite = factory.CreateSpriteVisual(); sprite.Size = patch.Size; sprite.Brush = paint; patch.Children.InsertAtTop(sprite); parts.Add(sprite);
            }
        }
        holder.Children.InsertAtTop(pair); return pair;
    }
    internal void AlignAnimation(double x) { if(animationRoot is not null && animationRoot.RelativeOffsetAdjustment.X == 0) animationRoot.Offset=new Vector3((float)x,0,0); }
    internal void SetVisible(bool visible) {root.Opacity=visible?1:0;_=compositor!.RequestCommitAsync();}
    internal void RefreshSource()
    {
        var regions=previous; var scale=previousScale; previous=[]; previousScale=0;
        brush.Dispose(); brush=compositor!.CreateHostBackdropBrush(); Update(regions,scale); _=compositor.RequestCommitAsync();
    }
    internal void Prepare(byte[] image, int width, int height)
    {
        if (ReferenceEquals(preparedImage,image)) return;
        pixels ??= new CompositorPixels(compositor!);
        if (animationSurface is not null) { if (animationTarget is not null) heldParts.Add(animationSurface); else animationSurface.Dispose(); }
        animationSurface = pixels.Upload(image,width,height); preparedImage = image;
    }
    private static CompositionGeometry CreateShape(Vector2 size,float radius)
    {
        if (Math.Abs(size.X-size.Y)<.01f && radius>=size.X/2-.01f)
        { var circle=compositor!.CreateEllipseGeometry(); circle.Center=size/2; circle.Radius=size/2; return circle; }
        var rectangle=compositor!.CreateRoundedRectangleGeometry(); rectangle.Size=size; rectangle.CornerRadius=new Vector2(radius); return rectangle;
    }
    public void Dispose()
    { CancelAnimation(); animationSurface?.Dispose(); root.Children.RemoveAll(); foreach (var shape in shapes) shape.Dispose(); shapes.Clear(); foreach (var mask in cornerMasks.Values) mask.Dispose(); cornerMasks.Clear(); brush.Dispose(); root.Dispose(); displayRoot.Dispose(); target.Dispose(); }
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
