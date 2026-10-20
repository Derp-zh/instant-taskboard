using System;
using System.Windows;
using System.Windows.Controls;

namespace Taskboard.Desktop;

// Reserve the right-hand controls before measuring the name. Both its border
// and its text viewport must fit the same remaining space.
internal sealed class CardHeading : Panel
{
    private readonly FrameworkElement label;
    private FrameworkElement? conversation, state;
    internal CardHeading(FrameworkElement label)
    { this.label = label; Children.Add(label); ClipToBounds = true; HorizontalAlignment = HorizontalAlignment.Stretch; }
    internal void SetConversation(FrameworkElement value) { conversation = value; Children.Add(value); }
    internal void SetStateButton(FrameworkElement value) { state = value; Children.Add(value); }
    protected override Size MeasureOverride(Size available)
    {
        var right = 0.0; var height = 0.0;
        foreach (var element in new[] { state, conversation })
        {
            if (element is null) continue;
            element.Measure(new Size(double.PositiveInfinity, available.Height));
            right += element.DesiredSize.Width; height = Math.Max(height, element.DesiredSize.Height);
        }
        MeasureLabel(double.IsInfinity(available.Width) ? double.PositiveInfinity : Math.Max(0, available.Width - right), available.Height);
        return new Size(double.IsInfinity(available.Width) ? label.DesiredSize.Width + right : available.Width, Math.Max(height, label.DesiredSize.Height));
    }
    private void MeasureLabel(double width, double height)
    {
        var limit = double.IsInfinity(width) ? double.PositiveInfinity : Math.Max(0, width - label.Margin.Left - label.Margin.Right);
        if (label.MaxWidth != limit) label.MaxWidth = limit;
        label.Measure(new Size(width, height));
    }
    protected override Size ArrangeOverride(Size final)
    {
        var end = final.Width;
        foreach (var element in new[] { state, conversation })
        {
            if (element is null) continue;
            var width = element.DesiredSize.Width; end -= width;
            // Preserve right anchoring even when a very narrow column clips the
            // left side of the conversation. Clamping x would push it into state.
            element.Arrange(new Rect(end, 0, width, final.Height));
        }
        var remaining = Math.Max(0, end);
        // Measure can be offered more width than the final parent slot. Do not
        // reuse a name viewport or MaxWidth calculated for that earlier width.
        MeasureLabel(remaining, final.Height);
        label.Arrange(new Rect(0, 0, remaining, final.Height));
        // Border padding still has a natural minimum in WPF. Clip that padding
        // as well as the text when no label space remains; never overlap right.
        var visible = Math.Max(0, remaining - label.Margin.Left - label.Margin.Right);
        label.Clip = label.RenderSize.Width > visible
            ? new System.Windows.Media.RectangleGeometry(new Rect(0, 0, visible, label.RenderSize.Height)) : null;
        return final;
    }
}
