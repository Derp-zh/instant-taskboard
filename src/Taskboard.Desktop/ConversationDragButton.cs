using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class ConversationDragButton : Button
{
    private readonly BoardController controller;
    private readonly Guid taskId, stageId;
    private readonly string caption;
    private Point pressedAt;
    private bool pressed;
    private ConversationBindingDrag? drag;
    // List controls live in a separate child HwndSource, so GetWindow is null.
    internal ColumnSurface? SourceColumn => controller.Columns.FirstOrDefault(x =>
        PresentationSource.FromVisual(this) is { } source && ReferenceEquals(source, PresentationSource.FromVisual(x.Root)));
    internal ConversationDragButton(BoardController controller, WorkItem item, Stage stage, MarqueeText name)
    {
        this.controller = controller; taskId = item.Id; stageId = stage.Id; caption = name.Label.Text;
        Tag = "TaskConversationDrag"; DataContext = stage;
        Margin = name.Margin; name.Margin = new Thickness(0); Content = name;
        Padding = new Thickness(0); BorderThickness = new Thickness(0); Background = Ui.Brush("#01192333");
        HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Center;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; Cursor = Cursors.Hand;
        ToolTip = "按住拖出列表，放到 Codex 会话后松开绑定。会短暂读取会话链接并恢复剪贴板。Esc 取消。";
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        System.Windows.Automation.AutomationProperties.SetName(this, "拖动绑定会话：" + caption);
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "DragBinding." + taskId + "." + stageId);
        Unloaded += (_, _) => Cancel();
    }
    internal static bool Contains(DependencyObject? node)
    {
        for (; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is ConversationDragButton) return true;
        return false;
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        pressedAt = e.GetPosition(this); pressed = true; Focus();
        if (!CaptureMouse()) pressed = false;
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
        if (drag is null && (e.GetPosition(this) - pressedAt).Length >= 7 && SourceColumn is { } column)
        {
            var live = controller.Engine.State.Tasks.Find(x => x.Id == taskId)?.Stages.Find(x => x.Id == stageId);
            if (live is null) { Cancel(); return; }
            drag = new ConversationBindingDrag(controller, column, taskId, stageId, live.Codex?.BindingId, caption);
        }
        e.Handled = true;
    }
    protected override async void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!pressed) return;
        pressed = false; var finished = drag; drag = null;
        var point = System.Windows.Forms.Cursor.Position;
        ReleaseMouseCapture();
        if (finished is null) return; // A click never opens the task editor or starts a card drag.
        try { await finished.Complete(new Point(point.X, point.Y)); }
        catch (Exception) { finished.Dispose(); Ui.Hint(this, "拖动未完成，请检查当前绑定及保存状态", !controller.Smoke, SourceColumn?.Root); }
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); Cancel(); }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    { if (e.Key == Key.Escape && pressed) { Cancel(); e.Handled = true; } else base.OnPreviewKeyDown(e); }
    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    { if (pressed || drag is not null) { Cancel(); e.Handled = true; } else base.OnPreviewMouseRightButtonDown(e); }
    private void Cancel()
    {
        pressed = false; var previous = drag; drag = null; previous?.Dispose();
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
}
