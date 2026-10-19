using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class Ux099Checks
{
    internal static async Task Run(BoardController controller)
    {
        var checks = new List<string>(); var root = controller.Store.DirectoryPath;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message); File.WriteAllText(Path.Combine(root, "ux099-progress.json"), JsonSerializer.Serialize(checks));
        }
        controller.SuppressCollapse = true;
        // Populate the loaded state without Notify: initial display must not
        // depend on an edit, an event, or opening the workspace.
        var initial = new WorkItem { Title = "启动计数", Lane = Lane.Action };
        controller.Engine.State.Tasks.Add(initial);
        Check(controller.Ball.CountBubble.Visibility == Visibility.Collapsed, "初始夹具尚未触发任何通知或展开");
        await controller.Start();
        Check(!controller.Expanded && controller.Ball.CountText == "1" && controller.Ball.CountBubble.Visibility == Visibility.Visible, "启动首帧前主动载入待操作计数，无需点击悬浮球");
        controller.SetExpanded(true); await Task.Delay(450);
        var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "Long title for scrolling check";
        var first = session.Working.Stages[0]; first.Name = "这是很长的阶段名称需要滚动"; first.Text = "阶段内容";
        first.Codex = new CodexBinding { ThreadId = "thread-a", ThreadName = "ConversationLongName" };
        var second = controller.Engine.AddStage(session); second.Text = "第二阶段"; second.IsRunning = true;
        var item = controller.Engine.Submit(session); await Task.Delay(100);
        var column = controller.Columns.Single(x => x.Lane == Lane.Action); var card = column.Locate(item.Id)!; card.UpdateLayout();
        var separator = Children<CardSeparator>(card).Single(x => Equals(x.Tag, "TaskTitleSeparator"));
        var middle = Children<CardSeparator>(card).Single(x => Equals(x.Tag, "TaskStageSeparator"));
        var heightWithLine = card.ActualHeight; separator.Visibility = Visibility.Collapsed; card.UpdateLayout();
        Check(Math.Abs(card.ActualHeight - heightWithLine) < .01 && separator.Height == 0, "标题横线不增加卡片高度或改变原有内容站位");
        separator.Visibility = Visibility.Visible; card.UpdateLayout();
        Check(Math.Abs(separator.ActualWidth - middle.ActualWidth) < .01, "标题与阶段横线宽度一致");
        Check(PaintedRows(Ui.Accent, true) == 1 && PaintedRows(Ui.Line, false) == 1, "青色和灰色横线均只绘制一行物理像素");
        var conversation = Children<MarqueeText>(card).Single(x => Equals(x.Tag, "TaskConversationName") && x.DataContext is Stage stage && stage.Id == first.Id);
        Check(conversation.Label.Text == first.Codex.ThreadName && Math.Abs(conversation.ActualWidth - MarqueeText.ConversationWidth(conversation.Label)) < .01, "关联会话以无框名称显示，按6至10个英文字符宽度伸缩");
        Check(Children<Button>(card).Count(x => Equals(x.Tag, "TaskStageRun")) == 2, "普通列表保留各阶段状态图标");
        var namePill = Children<Border>(card).First(x => Equals(x.Tag, "TaskStageName"));
        Check(namePill.Child is MarqueeText name && name.Overflow > 0 && namePill.ActualWidth < card.ActualWidth - conversation.ActualWidth, "阶段名称使用名称与图标之外的剩余空间，长名称可滚动");
        Check(conversation.Overflow > 0 && MarqueeText.InitialPauseSeconds == .5 && MarqueeText.EndPauseSeconds == .3, "长会话名称按首停0.5秒、末停0.3秒循环");
        var restartCount = conversation.RestartCount;
        await Task.Delay(850); Check(conversation.Offset < 0, "超长会话名称会发生实际横向滚动");
        controller.SetExpanded(false); await Task.Delay(200); controller.SetExpanded(true);
        var currentCard = column.RenderedCard(item.Id) ?? column.Locate(item.Id)!;
        var currentConversation = Children<MarqueeText>(currentCard).Single(x => Equals(x.Tag, "TaskConversationName") && x.DataContext is Stage value && value.Id == first.Id);
        var resetCount = currentConversation.RestartCount; var resetOffset = currentConversation.Offset;
        Check((ReferenceEquals(currentConversation, conversation) ? resetCount > restartCount : resetCount > 0) && Math.Abs(resetOffset) < .01, "再次展开看板当前可见会话从初始停顿重新滚动，不继承偏移");
        card = currentCard; conversation = currentConversation; await Task.Delay(160);
        var title = Children<MarqueeText>(card).Single(x => Equals(x.Label.Tag, "TaskTitle"));
        Check(title.Label.Text == item.Title, "任务名称也采用剩余空间与溢出滚动");
        var inspirationSession = controller.Engine.NewSession(Lane.Inspiration); inspirationSession.Working.Title = "灵感例外";
        inspirationSession.Working.Stages[0].Codex = Copies.Of(first.Codex);
        var inspirationItem = controller.Engine.Submit(inspirationSession); await Task.Delay(80);
        var inspirationCard = controller.Columns.Single(x => x.Lane == Lane.Inspiration).Locate(inspirationItem.Id)!;
        Check(!Children<MarqueeText>(inspirationCard).Any(x => Equals(x.Tag, "TaskConversationName")) && !Children<Button>(inspirationCard).Any(x => Equals(x.Tag, "TaskStageRun")), "灵感库不显示会话名称或运行状态图标");
        var editor = controller.EditTask(item.Id); await Settled(editor.Host!);
        var picker = editor.StagePickers[first.Id];
        picker.Trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); editor.UpdateLayout();
        picker.SetCatalog([new CodexThread("one", "名字甲", 0), new CodexThread("two", "名字乙", 0)]);
        picker.Input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        Check(picker.IsDropdownOpen, "点击搜索框后展开会话下拉");
        picker.Input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        Check(picker.IsDropdownOpen, "松开搜索框后下拉保持打开，不依赖按住鼠标");
        Check(!Children<TextBlock>(picker.Pane).Any(x => x.Text.StartsWith("按名称搜索") || x.Text.StartsWith("点击输入框选择")), "删除搜索说明行，目录不可用时仍可显示错误提示");
        picker.CollapseUnlessContains(picker.Input);
        Check(picker.Pane.Visibility == Visibility.Visible && picker.IsDropdownOpen, "搜索框内部操作不会收起选择区域");
        var titleInput = Children<TextBox>(editor).First();
        titleInput.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        Check(picker.Pane.Visibility == Visibility.Collapsed && !picker.IsDropdownOpen, "点击任务名称会自动收起关联展开部分");
        picker.Trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); picker.Trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(picker.Pane.Visibility == Visibility.Collapsed, "关联气泡再次点击仍可收起");
        var close = Children<Button>(editor).Single(x => Equals(x.Tag, "EditorClose"));
        var saveButton = Children<Button>(editor).Single(x => Equals(x.Content, "保存任务"));
        Check(Equals(close.Content, "取消保存") && Math.Abs(close.ActualWidth - saveButton.ActualWidth) < .1 && close.Height == 40, "编辑取消保存与保存任务尺寸一致且保留文字");
        editor.Session.Working.Stages.Single(x => x.Id == second.Id).IsRunning = false;
        controller.Engine.StageSession(editor.Session);
        Check(await editor.SaveAndClose(), "保存按钮公共路径可以保存阶段标记");
        editor = controller.EditTask(item.Id); await Settled(editor.Host!);
        editor.Session.Working.Stages.Single(x => x.Id == second.Id).IsRunning = true;
        titleInput = Children<TextBox>(editor).First(); titleInput.Text = "外部双击保存";
        var editHost = editor.Host!;
        controller.ObserveOutsidePress(editHost, true);
        controller.ObserveOutsidePress(editHost, false, new Point(30, 30), 1000);
        Check(controller.Editors.Contains(editor), "窗口外单击保持编辑，不提前保存关闭");
        controller.ObserveOutsidePress(editHost, false, new Point(30, 30), 1100);
        for (var i = 0; i < 100 && controller.Editors.Contains(editor); i++) await Task.Delay(20);
        var saved = controller.Engine.State.Tasks.Single(x => x.Id == item.Id);
        Check(!controller.Editors.Contains(editor) && saved.Title == "外部双击保存" && saved.Stages.All(x => x.IsRunning == true), "外部双击正式保存全部字段及新运行阶段后关闭");
        var persisted = controller.Store.Load().Tasks.Single(x => x.Id == item.Id);
        card = column.Locate(item.Id)!;
        Check(persisted.Stages.All(x => x.IsRunning == true) && Children<StackPanel>(card).Count(x => Equals(x.Tag, "TaskStageSection")) == 2, "保存落盘且列表立即展示新增运行阶段");
        editor = controller.EditTask(item.Id); await Settled(editor.Host!); Children<TextBox>(editor).First().Text = "取消的名称";
        Children<Button>(editor).Single(x => Equals(x.Tag, "EditorClose")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(220);
        Check(controller.Engine.State.Tasks.Single(x => x.Id == item.Id).Title == "外部双击保存" && controller.Engine.State.Sessions.All(x => x.TaskId != item.Id), "取消保存丢弃编辑副本，正式任务保持已保存内容");
        var draft = controller.NewTask(Lane.Action); await Settled(draft.Host!); Children<TextBox>(draft).First().Text = "保留草稿";
        var draftHost = draft.Host!; controller.ObserveOutsidePress(draftHost, true); controller.ObserveOutsidePress(draftHost, false, pressedAt: 2000); controller.ObserveOutsidePress(draftHost, false, pressedAt: 2100);
        await Task.Delay(220); await controller.Flush();
        Check(controller.Engine.State.Tasks.All(x => x.Title != "保留草稿") && controller.Store.Load().Sessions.Any(x => x.TaskId is null && x.Working.Title == "保留草稿"), "新建外部双击仍只暂存全部字段，不变成正式保存");
        Check(Ui.HintDuration == TimeSpan.FromMilliseconds(1200), "至少一运行阶段提示缩短为原时长3/5，即1.2秒");
        var hintContent = new Border { Child = Ui.Label("至少保留一个运行中的阶段") };
        var hint = new ToolTip { Template = Ui.HintTemplate(), Content = hintContent };
        hint.ApplyTemplate(); hint.Measure(new Size(400, 100)); hint.Arrange(new Rect(hint.DesiredSize));
        Check(Children<Border>(hint).Count() == 1 && Children<ContentPresenter>(hint).Any(x => ReferenceEquals(x.Content, hintContent)), "提示模板真实绑定内容，只有自绘一层边框，无默认套框");
        Check(column.Root.ContextMenu!.Items.OfType<MenuItem>().Any(x => Equals(x.Header, "本窗口恢复到默认")), "单列菜单显示本窗口恢复到默认");
        var motionWindows = controller.VisibleColumns.Cast<SurfaceWindow>().ToArray();
        for (var n = 0; n < 3; n++) { controller.SetExpanded(false); await Task.Delay(180); controller.SetExpanded(true); await Task.Delay(350); }
        for (var i = 0; i < 150 && motionWindows.Any(x => x.IsRevealAnimating || x.IsContentSwapHandoff); i++) await Task.Delay(20);
        File.WriteAllText(Path.Combine(root, "handoff-state.json"), JsonSerializer.Serialize(motionWindows.Select(x => new { x.Title, x.IsRevealAnimating, x.LiveContentHidden, x.IsContentSwapHandoff, x.LastAnimationError, x.RestingHandoffCount })));
        Check(motionWindows.All(x => !x.LiveContentHidden && !x.IsContentSwapHandoff && x.LastAnimationError is null && x.LiveListRevealCount >= 3 && x.RestingHandoffCount == 0), "连续开合全程真实控件，不再发生列表截图交接且无动画异常");
        File.WriteAllText(Path.Combine(root, "ux099-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks,
            desktopShown = false, mouseMoved = false, keyboardInjected = false, desktopVisualVerified = false,
            handoffs = motionWindows.Select(x => new { x.Title, x.RestingHandoffCount, x.LastAnimationError }) }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<T> Children<T>(DependencyObject element) where T : DependencyObject => StageFeatureChecks.Children<T>(element);
    private static async Task Settled(SurfaceWindow window)
    {
        for (var i = 0; (!window.IsVisible || window.IsScrollAnimating || window.IsContentSwapHandoff) && i < 150; i++) await Task.Delay(20);
        if (!window.IsVisible || window.IsScrollAnimating || window.IsContentSwapHandoff) throw new InvalidOperationException("动画未结束：" + window.Title);
    }
    private static int PaintedRows(Brush color, bool overlay)
    {
        var grid = new Grid { Width = 80, Height = 12 }; grid.Children.Add(new CardSeparator(color, overlay) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) });
        grid.Measure(new Size(80, 12)); grid.Arrange(new Rect(0, 0, 80, 12)); grid.UpdateLayout();
        var image = new RenderTargetBitmap(80, 12, 96, 96, PixelFormats.Pbgra32); image.Render(grid);
        var bytes = new byte[80 * 12 * 4]; image.CopyPixels(bytes, 80 * 4, 0);
        return Enumerable.Range(0, 12).Count(y => bytes[(y * 80 + 40) * 4 + 3] != 0);
    }
}
