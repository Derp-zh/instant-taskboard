using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class StageConversationPicker : IDisposable
{
    private readonly BoardController controller;
    private readonly Stage stage;
    private readonly Action changed;
    private readonly bool snapshot;
    private readonly Popup popup;
    private readonly ListBox results = new() { MaxHeight = 240, Background = Ui.Surface, Foreground = Ui.Text, BorderThickness = new Thickness(0) };
    private readonly TextBlock note = Ui.Label("", 11, Ui.Muted);
    private readonly TextBlock empty = Ui.Label("没有匹配的会话名称", 12, Ui.Muted);
    private CodexThread[] catalog = [];
    private bool disposed, loading;
    internal StackPanel Pane { get; } = new();
    internal TextBox Input { get; }
    internal Button Trigger { get; }
    internal FrameworkElement? StageScope { get; set; }
    internal TextBlock Placeholder { get; } = new() { Foreground = Ui.Muted, Margin = new Thickness(10, 12, 36, 8), IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis };
    internal Button DropdownToggle { get; }
    internal CodexThread[] Matches => catalog.Where(x => x.DisplayName.Contains(Input.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    internal StageConversationPicker(BoardController controller, Stage stage, Action changed, bool snapshot)
    {
        this.controller = controller; this.stage = stage; this.changed = changed; this.snapshot = snapshot;
        Pane.Tag = "StageBinding-" + stage.Id; Pane.Visibility = Visibility.Collapsed; Pane.Margin = new Thickness(10, 0, 10, 4);
        Trigger = Ui.Button("", () => { Pane.Visibility = Pane.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; if (Pane.Visibility == Visibility.Collapsed) Close(); });
        Trigger.Tag = "StageCodex";
        Input = Ui.Input(); Input.Tag = "StageConversationSearch"; Input.ToolTip = "选择已有会话；只搜索会话名称，不搜索编号";
        var field = new Grid(); field.Children.Add(Input);
        Input.Padding = new Thickness(9, 9, 32, 9);
        field.Children.Add(Placeholder);
        var caretPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        caretPresenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        caretPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var caretArea = new FrameworkElementFactory(typeof(Border)); caretArea.SetValue(Border.BackgroundProperty, Ui.Brush("#01192333")); caretArea.AppendChild(caretPresenter);
        DropdownToggle = new Button { Content = "⌄", Tag = "StageConversationDropdownToggle", Width = 28, Margin = new Thickness(0, 5, 4, 9),
            HorizontalAlignment = HorizontalAlignment.Right, Background = System.Windows.Media.Brushes.Transparent, Foreground = Ui.Muted,
            BorderThickness = new Thickness(0), Padding = new Thickness(0), Focusable = false,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = caretArea } };
        DropdownToggle.HorizontalContentAlignment = HorizontalAlignment.Center;
        DropdownToggle.VerticalContentAlignment = VerticalAlignment.Center;
        field.Children.Add(DropdownToggle);
        var row = new FrameworkElementFactory(typeof(StackPanel));
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding(nameof(CodexThread.DisplayName))); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); row.AppendChild(name);
        var number = new FrameworkElementFactory(typeof(TextBlock)); number.SetBinding(TextBlock.TextProperty, new Binding(nameof(CodexThread.Id))); number.SetValue(TextBlock.FontSizeProperty, 10.0); number.SetValue(TextBlock.ForegroundProperty, Ui.Muted); row.AppendChild(number);
        results.ItemTemplate = new DataTemplate { VisualTree = row }; VirtualizingPanel.SetIsVirtualizing(results, true);
        var dropdown = new Grid(); dropdown.Children.Add(results); dropdown.Children.Add(empty);
        // A search box keeps its own selection capture until mouse-up. The
        // popup must not steal that capture and immediately dismiss itself.
        popup = new Popup { PlacementTarget = Input, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = true,
            Child = new Border { Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(5), Child = dropdown } };
        popup.Opened += (_, _) => DropdownToggle.Content = "⌃";
        popup.Closed += (_, _) => DropdownToggle.Content = "⌄";
        DropdownToggle.Click += async (_, _) => { if (popup.IsOpen) Close(); else await Open(); };
        Present();
        Input.GotKeyboardFocus += async (_, _) => await Open();
        Input.PreviewMouseLeftButtonDown += async (_, _) => { if (!popup.IsOpen) await Open(); };
        Input.TextChanged += (_, _) => { Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Filter(); if (!snapshot && Input.IsKeyboardFocusWithin && Input.IsVisible) popup.IsOpen = true; };
        Input.IsVisibleChanged += (_, _) => { if (!Input.IsVisible) Close(); };
        Input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Down && results.Items.Count > 0) { results.SelectedIndex = 0; results.Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter && Matches.FirstOrDefault() is { } first) { Select(first); e.Handled = true; }
        };
        results.PreviewMouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(results, source) is ListBoxItem { DataContext: CodexThread selected }) Select(selected); };
        results.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && results.SelectedItem is CodexThread selected) { Select(selected); e.Handled = true; } else if (e.Key == Key.Escape) { Close(); Input.Focus(); e.Handled = true; } };
        var unlink = Ui.Button("解除关联", () => { stage.Codex = null; Present(); changed(); Close(); }); unlink.Tag = "StageConversationUnlink";
        Pane.Children.Add(field); Pane.Children.Add(unlink); Pane.Children.Add(note);
        if (!snapshot && !controller.Smoke) _ = Load();
    }
    private void Present()
    {
        var name = stage.Codex is null ? "关联会话" : string.IsNullOrWhiteSpace(stage.Codex.ThreadName) ? "未命名会话" : stage.Codex.ThreadName;
        Trigger.Content = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        Trigger.ToolTip = stage.Codex is null ? "选择此阶段的已有会话；保存后生效" : name + " · " + stage.Codex.ThreadId;
        Placeholder.Text = stage.Codex is null ? "搜索或选择已有会话" : name;
    }
    private async Task Open()
    {
        if (snapshot || disposed || !Input.IsVisible) return;
        results.MinWidth = results.MaxWidth = Math.Max(180, Input.ActualWidth); Filter(); popup.IsOpen = true; await Load();
    }
    private async Task Load()
    {
        if (loading || disposed) return; loading = true;
        try
        {
            var values = await controller.LoadConversations(); if (disposed) return;
            catalog = values;
            if (stage.Codex is { } binding && values.FirstOrDefault(x => x.Id == binding.ThreadId) is { } known && known.Name.Length > 0 && known.Name != binding.ThreadName)
            { binding.ThreadName = known.Name; Present(); changed(); }
            note.Text = values.Length == 0 ? "暂无可选会话，请确认Codex连接后再次点击输入框。" : ""; Filter();
        }
        catch { if (!disposed) note.Text = "读取会话失败，请稍后再次点击输入框重试。"; }
        finally { loading = false; }
    }
    internal void SetCatalog(CodexThread[] values) { catalog = values; Filter(); }
    private void Filter()
    {
        var values = Matches; results.ItemsSource = values; empty.Visibility = values.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void Select(CodexThread thread)
    {
        if (stage.Codex?.ThreadId != thread.Id) stage.Codex = new CodexBinding { ThreadId = thread.Id, ThreadName = thread.Name };
        else stage.Codex.ThreadName = thread.Name;
        Present(); Input.Text = ""; note.Text = "保存任务后关联 · " + (thread.Name.Length == 0 ? "未命名会话" : thread.Name) + " · " + thread.Id;
        changed(); Collapse();
    }
    internal void Close() { popup.IsOpen = false; DropdownToggle.Content = "⌄"; }
    internal void Collapse() { Pane.Visibility = Visibility.Collapsed; Close(); }
    internal bool IsDropdownOpen => popup.IsOpen;
    internal ListBox Results => results;
    internal void CollapseUnlessContains(DependencyObject? source)
    {
        if (Within(source, popup.Child)) return;
        if (!Within(source, StageScope) && !Within(source, Pane) && !Within(source, Trigger)) { Collapse(); return; }
        if (!Within(source, Input) && !Within(source, DropdownToggle)) Close();
    }
    private static bool Within(DependencyObject? child, DependencyObject? parent)
    {
        if (parent is null) return false;
        for (var current = child; current is not null;)
        {
            if (ReferenceEquals(current, parent)) return true;
            current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
    public void Dispose() { disposed = true; Close(); }
}
