using System.Windows;
using System.Windows.Controls;

namespace Taskboard.Desktop;

// Shared geometry keeps the quick entry and its saved card in the same footprint.
internal static class TaskCardLayout
{
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
