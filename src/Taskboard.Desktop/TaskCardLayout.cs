using System.Windows;
using System.Windows.Controls;
using System;
using System.Globalization;
using System.Windows.Media;

namespace Taskboard.Desktop;

// Shared geometry keeps the quick entry and its saved card in the same footprint.
internal static class TaskCardLayout
{
    internal static double DetailsInkTop(TextBlock details)
    {
        var end = details.Text.IndexOfAny(['\r', '\n']);
        var line = details.Text[..Math.Min(128, end < 0 ? details.Text.Length : end)];
        if (string.IsNullOrWhiteSpace(line)) line = "Ag";
        var formatted = new FormattedText(line, CultureInfo.CurrentUICulture, details.FlowDirection,
            new Typeface(details.FontFamily, details.FontStyle, details.FontWeight, details.FontStretch), details.FontSize,
            details.Foreground, VisualTreeHelper.GetDpi(details).PixelsPerDip);
        var ink = formatted.BuildGeometry(new Point()).Bounds;
        return ink.IsEmpty ? 0 : Math.Max(0, ink.Top);
    }
    internal static void MatchTitleGap(FrameworkElement heading, Border pill, TextBlock details)
    {
        // Match the visible gap under the pill, including the font's leading.
        // The line still has zero layout height; only this requested spacing changes.
        var linePixel = 1 / VisualTreeHelper.GetDpi(heading).DpiScaleY;
        var gap = details.Margin.Top + DetailsInkTop(details);
        var top = Math.Max(0, linePixel + gap - pill.Margin.Top);
        if (Math.Abs(heading.Margin.Top - top) > .01) heading.Margin = new Thickness(0, top, 0, 0);
    }
    internal static void Frame(Border card)
    {
        card.Margin = new Thickness(0, 0, 0, 8);
        card.Padding = new Thickness(11, 6.6, 11, 6.6);
        card.CornerRadius = new CornerRadius(12);
        card.Background = Ui.Card; card.BorderBrush = Ui.Line; card.BorderThickness = new Thickness(1);
    }
    internal static Grid Title(UIElement label)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new Border { Width = 3, Height = 14, CornerRadius = new CornerRadius(1.5), Background = Ui.Accent,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4.4, 0, 0), Tag = "TaskTitleMarker", IsHitTestVisible = false });
        Grid.SetColumn(label, 1); row.Children.Add(label); return row;
    }
}
