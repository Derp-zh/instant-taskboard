using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Taskboard.Desktop;

public static class QuickScroll
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(QuickScroll), new PropertyMetadata(false, Enable));
    public static readonly DependencyProperty VerticalBarWidthProperty = DependencyProperty.RegisterAttached("VerticalBarWidth", typeof(double), typeof(QuickScroll), new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty ThumbMarginProperty = DependencyProperty.RegisterAttached("ThumbMargin", typeof(Thickness), typeof(QuickScroll), new FrameworkPropertyMetadata(new Thickness(1), FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    public static double GetVerticalBarWidth(DependencyObject value) => (double)value.GetValue(VerticalBarWidthProperty);
    public static void SetVerticalBarWidth(DependencyObject value, double width) => value.SetValue(VerticalBarWidthProperty, width);
    public static Thickness GetThumbMargin(DependencyObject value) => (Thickness)value.GetValue(ThumbMarginProperty);
    public static void SetThumbMargin(DependencyObject value, Thickness margin) => value.SetValue(ThumbMarginProperty, margin);
    internal const double DurationMs = 90;
    private static readonly ConditionalWeakTable<ScrollViewer, Motion> motions = new();
    internal static bool IsAnimating(ScrollViewer viewer) => motions.TryGetValue(viewer, out var motion) && motion.Active;
    private static void Enable(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is not ScrollViewer viewer) return;
        viewer.Loaded -= Loaded; viewer.Unloaded -= Unloaded;
        if ((bool)args.NewValue) { viewer.Loaded += Loaded; viewer.Unloaded += Unloaded; if (viewer.IsLoaded) Attach(viewer); }
        else { Detach(viewer); motions.Remove(viewer); }
    }
    private static void Loaded(object sender, RoutedEventArgs args) => Attach((ScrollViewer)sender);
    private static void Unloaded(object sender, RoutedEventArgs args) => Detach((ScrollViewer)sender);
    private static void Attach(ScrollViewer viewer)
    {
        Detach(viewer); motions.GetValue(viewer, x => new Motion(x));
        viewer.PreviewMouseWheel += Wheel;
        viewer.PreviewMouseDown += DirectInput;
        viewer.PreviewKeyDown += KeyInput;
        viewer.RequestBringIntoView += BringIntoView;
    }
    private static void Detach(ScrollViewer viewer)
    {
        if (motions.TryGetValue(viewer, out var motion)) motion.Stop();
        viewer.PreviewMouseWheel -= Wheel; viewer.PreviewMouseDown -= DirectInput;
        viewer.PreviewKeyDown -= KeyInput; viewer.RequestBringIntoView -= BringIntoView;
    }
    private static void DirectInput(object sender, MouseButtonEventArgs args)
    { if (motions.TryGetValue((ScrollViewer)sender, out var motion)) motion.Stop(); }
    private static void KeyInput(object sender, KeyEventArgs args)
    { if (motions.TryGetValue((ScrollViewer)sender, out var motion)) motion.Stop(); }
    private static void BringIntoView(object sender, RequestBringIntoViewEventArgs args)
    { if (motions.TryGetValue((ScrollViewer)sender, out var motion)) motion.Stop(); }
    private static void Wheel(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || args.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None) return;
        var viewer = (ScrollViewer)sender;
        // Let an inner text field/list consume its own scroll first. At its end,
        // continue with the next scrollable ancestor as normal wheel routing does.
        for (var node = args.OriginalSource as DependencyObject; node is not null; node = Parent(node))
        {
            if (node is not ScrollViewer candidate || !CanMove(candidate, args.Delta)) continue;
            if (!ReferenceEquals(candidate, viewer)) return;
            break;
        }
        if (!CanMove(viewer, args.Delta)) return;
        var motion = motions.GetValue(viewer, x => new Motion(x));
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0) return;
        var from = motion.Active ? motion.Target : viewer.VerticalOffset;
        var delta = lines < 0 ? viewer.ViewportHeight : Math.Max(16, viewer.FontSize * 1.2) * lines;
        if (lines > 0 && viewer.TemplatedParent is ItemsControl items && items.Items.Count > 0)
        {
            // Preserve the existing item-based wheel distance while rendering
            // fractional pixel positions; virtualization stays enabled.
            var realized = ItemsPanel(viewer)?.Children.OfType<FrameworkElement>().Where(x => x is { IsVisible: true, ActualHeight: > 0 }).ToArray() ?? [];
            if (realized.Length > 0) delta = realized.Average(x => x.ActualHeight + x.Margin.Top + x.Margin.Bottom) * lines;
        }
        motion.Start(Math.Clamp(from - args.Delta / 120.0 * delta, 0, viewer.ScrollableHeight));
        args.Handled = true;
    }
    private static bool CanMove(ScrollViewer viewer, int delta)
    {
        var offset = motions.TryGetValue(viewer, out var motion) && motion.Active ? motion.Target : viewer.VerticalOffset;
        return viewer.IsVisible && viewer.ScrollableHeight > 0 && (delta < 0 ? offset < viewer.ScrollableHeight - .1 : offset > .1);
    }
    private static VirtualizingStackPanel? ItemsPanel(DependencyObject node)
    {
        if (node is VirtualizingStackPanel panel) return panel;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (ItemsPanel(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }
    internal static void StepItem(ScrollViewer viewer, int direction)
    {
        if (motions.TryGetValue(viewer, out var motion)) motion.Stop();
        var presenter = viewer.Template.FindName("PART_ScrollContentPresenter", viewer) as FrameworkElement;
        var origin = presenter?.TransformToAncestor(viewer).Transform(new Point()).Y ?? 0;
        var starts = ItemsPanel(viewer)?.Children.OfType<FrameworkElement>().Where(x => x.ActualHeight > 0)
            .Select(x => x.TransformToAncestor(viewer).Transform(new Point()).Y - origin).ToArray() ?? [];
        var delta = direction > 0 ? starts.Where(y => y > .5).DefaultIfEmpty(48).Min() : starts.Where(y => y < -.5).DefaultIfEmpty(-48).Max();
        viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset + delta, 0, viewer.ScrollableHeight));
    }
    private static DependencyObject? Parent(DependencyObject node) => node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
    private sealed class Motion(ScrollViewer viewer)
    {
        private readonly Stopwatch clock = new();
        private double from;
        internal double Target { get; private set; }
        internal bool Active { get; private set; }
        internal void Start(double target)
        {
            Stop(); from = viewer.VerticalOffset; Target = target;
            if (Math.Abs(Target - from) < .1) return;
            clock.Restart(); Active = true; CompositionTarget.Rendering += Frame;
        }
        internal void Stop()
        { if (Active) CompositionTarget.Rendering -= Frame; Active = false; clock.Stop(); }
        private void Frame(object? sender, EventArgs args)
        {
            if (!viewer.IsLoaded || !viewer.IsVisible) { Stop(); return; }
            var fraction = Math.Clamp(clock.Elapsed.TotalMilliseconds / DurationMs, 0, 1);
            viewer.ScrollToVerticalOffset(Math.Clamp(from + (Target - from) * (1 - Math.Pow(1 - fraction, 3)), 0, viewer.ScrollableHeight));
            if (fraction >= 1) Stop();
        }
    }
}
