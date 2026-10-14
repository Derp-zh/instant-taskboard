using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Taskboard.Desktop;

// The clip limits hit testing as well as painting. Padding remains clickable,
// while the four empty corners outside the capsule do not receive input.
public static class RoundedInput
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(RoundedInput), new PropertyMetadata(false, Enable));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(RoundedInput), new PropertyMetadata(18.0, (value, _) => { if (value is TextBox input) Update(input); }));
    public static double GetRadius(DependencyObject value) => (double)value.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject value, double radius) => value.SetValue(RadiusProperty, radius);
    private static void Enable(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is not TextBox input) return;
        if ((bool)args.OldValue) { input.Loaded -= Loaded; input.SizeChanged -= Resized; input.PreviewMouseLeftButtonDown -= Pressed; }
        if ((bool)args.NewValue) { input.Loaded += Loaded; input.SizeChanged += Resized; input.PreviewMouseLeftButtonDown += Pressed; Update(input); }
    }
    private static void Loaded(object sender, RoutedEventArgs args) => Update((TextBox)sender);
    private static void Resized(object sender, SizeChangedEventArgs args) => Update((TextBox)sender);
    private static void Update(TextBox input)
    {
        var radius = Math.Max(0, Math.Min(GetRadius(input), Math.Min(input.ActualWidth, input.ActualHeight) / 2));
        var clip = new RectangleGeometry(new Rect(0, 0, input.ActualWidth, input.ActualHeight), radius, radius);
        clip.Freeze(); input.Clip = clip;
    }
    private static void Pressed(object sender, MouseButtonEventArgs args)
    {
        var input = (TextBox)sender;
        if (!input.IsEnabled || input.Clip?.FillContains(args.GetPosition(input)) != true) return;
        var presenter = ColumnSurface.FindChild<ScrollContentPresenter>(input);
        if (presenter is not null && new Rect(presenter.RenderSize).Contains(args.GetPosition(presenter))) return;
        input.Focus();
        var index = input.GetCharacterIndexFromPoint(args.GetPosition(input), true);
        input.CaretIndex = index < 0 ? (args.GetPosition(input).X < input.ActualWidth / 2 ? 0 : input.Text.Length) : index;
        input.SelectionLength = 0; args.Handled = true;
    }
}
