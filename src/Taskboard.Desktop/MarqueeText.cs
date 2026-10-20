using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Taskboard.Desktop;

// Only the overflowing part moves. Short labels keep their normal footprint.
internal sealed class MarqueeText : Decorator
{
    internal TextBlock Label { get; }
    private readonly TranslateTransform shift = new();
    private double textWidth;
    internal double Overflow => Math.Max(0, textWidth - ActualWidth);
    internal int RestartCount { get; private set; }
    internal const double InitialPauseSeconds = .5, EndPauseSeconds = .3;
    internal const double PixelsPerSecond = 24;
    private readonly bool conversation;
    internal MarqueeText(TextBlock label, bool conversation = false)
    {
        this.conversation = conversation;
        Label = label; Child = label; ClipToBounds = true;
        label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.None; label.RenderTransform = shift;
        Loaded += (_, _) => Restart(); Unloaded += (_, _) => Stop(); IsVisibleChanged += (_, _) => { if (IsVisible) Restart(); else Stop(); };
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        Label.Measure(new Size(double.PositiveInfinity, availableSize.Height)); textWidth = Label.DesiredSize.Width;
        // Font inheritance is established only after attaching to the card.
        // Measure the character limit here, using the actual rendered font.
        var desired = conversation ? ConversationWidth(Label) : textWidth;
        return new Size(Math.Min(desired, availableSize.Width), Label.DesiredSize.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var oldOverflow = Overflow;
        Label.Arrange(new Rect(0, 0, Math.Max(finalSize.Width, textWidth), finalSize.Height));
        if (Math.Abs(oldOverflow - Math.Max(0, textWidth - finalSize.Width)) > .1) Restart(finalSize.Width);
        return finalSize;
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { base.OnRenderSizeChanged(sizeInfo); Restart(); }
    internal void Restart() => Restart(ActualWidth);
    internal void Pause() { var current = shift.X; shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = current; }
    private void Restart(double width)
    {
        Stop(); RestartCount++;
        var overflow = Math.Max(0, textWidth - width);
        if (!IsLoaded || !IsVisible || overflow < .5 || width <= 0) return;
        var end = InitialPauseSeconds + overflow / PixelsPerSecond;
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(end + EndPauseSeconds), RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(InitialPauseSeconds))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(end))));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(end + EndPauseSeconds))));
        shift.BeginAnimation(TranslateTransform.XProperty, animation);
    }
    private void Stop() { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = 0; }
    internal double Offset => shift.X;
    // The requested 6–10 half-width units mean 3–5 Chinese glyph widths.
    // Use the rendered CJK font, rather than wide Latin M glyphs or encoded bytes.
    internal static double ConversationMinimumWidth(TextBlock label) => TextWidth(label, "中中中");
    internal static double ConversationWidth(TextBlock label) => Math.Clamp(TextWidth(label, label.Text), ConversationMinimumWidth(label), TextWidth(label, "中中中中中"));
    private static double TextWidth(TextBlock label, string text) => new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
        label.FlowDirection, new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize,
        label.Foreground, VisualTreeHelper.GetDpi(label).PixelsPerDip).WidthIncludingTrailingWhitespace;
}
