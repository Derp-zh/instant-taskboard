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
        var width = double.IsInfinity(available.Width) ? double.PositiveInfinity : Math.Max(0, available.Width - right);
        var limit = double.IsInfinity(width) ? double.PositiveInfinity : Math.Max(0, width - label.Margin.Left - label.Margin.Right);
        if (label.MaxWidth != limit) label.MaxWidth = limit;
        label.Measure(new Size(width, available.Height));
        return new Size(double.IsInfinity(available.Width) ? label.DesiredSize.Width + right : available.Width, Math.Max(height, label.DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size final)
    {
        var end = final.Width;
        foreach (var element in new[] { state, conversation })
        {
            if (element is null) continue;
            var width = element.DesiredSize.Width; end -= width;
            element.Arrange(new Rect(Math.Max(0, end), 0, width, final.Height));
        }
        label.Arrange(new Rect(0, 0, Math.Max(0, end), final.Height));
        return final;
    }
}
