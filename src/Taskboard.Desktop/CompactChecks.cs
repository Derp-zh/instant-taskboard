using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class CompactChecks
{
    internal static async Task Run(BoardController controller)
    {
        var checks = new List<string>();
        void Check(bool value, string description)
        {
            if (!value) throw new InvalidOperationException(description); checks.Add(description);
            File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "compact-progress.json"), JsonSerializer.Serialize(checks));
        }
        controller.VerifyOutsideObserverRegistration(); Check(true, "原生外部点击观察可注册并立即释放，不移动鼠标");
        controller.SuppressCollapse = true; controller.SetExpanded(true); await Task.Delay(200);
        var inspiration = controller.Columns.First(x => x.Lane == Lane.Inspiration); inspiration.UpdateLayout();
        Check(controller.Columns.All(x => x.Root.RowDefinitions[0].Height.Value == 42), "所有列表标题区为42，保留独立窗口尺寸");
        var staged = controller.Engine.NewSession(Lane.Running); staged.Working.Title = "青色任务名称"; staged.Working.Stages[0].Text = "白色当前详情";
        controller.Engine.AddStage(staged); staged.Working.Stages[1].Text = "白色相邻详情"; var item = controller.Engine.Submit(staged);
        var card = new TaskCard { Controller = controller, DataContext = item }; Render(card, 260, "formal-card.png");
        var title = Descendants<TextBlock>(card).Single(x => Equals(x.Tag, "TaskTitle"));
        Check(title.Foreground == Ui.Accent && title.FontWeight == FontWeights.SemiBold && title.FontSize == 13, "任务标题青色加粗且字号保持13");
        Check(Descendants<Border>(card).Single(x => Equals(x.Tag, "TaskTitleMarker")).Background == Ui.Accent, "任务标题前有青色细竖条");
        var details = Descendants<TextBlock>(card).Where(x => Equals(x.Tag, "TaskStageDetails")).ToArray();
        Check(details.All(x => x.Foreground == Ui.Text) && details[0].FontSize == 14 && details[1].FontSize == 12, "阶段详情白色且当前/相邻字号为14/12");
        Check(Descendants<Border>(card).Where(x => Equals(x.Tag, "TaskStageName")).All(x => ((TextBlock)x.Child).FontSize == 12), "阶段气泡字号增加为12");
        OpenQuick(); var quick = Quick(); var inputs = Descendants<TextBox>(quick).ToArray();
        Check(quick.Padding == card.Padding && quick.CornerRadius == card.CornerRadius && quick.BorderBrush == card.BorderBrush, "快捷新建与正式卡片共用边框圆角和紧凑内边距");
        Check(inputs.Length == 2 && inputs[0].Foreground == Ui.Accent && inputs[1].Foreground == Ui.Text && inputs[1].FontSize == 14, "快捷输入与正式卡片使用相同文字配色和详情字号");
        var finish = Descendants<Button>(quick).Single();
        Check(Equals(finish.Content, "✓") && finish.Width == 20 && finish.Height == 20 && finish.Parent == inputs[1].Parent, "20单位圆形✓置于详情输入区域");
        var origin = quick.TranslatePoint(new Point(), inspiration.Root);
        inputs[0].Text = "快捷保存样本"; inputs[1].Text = "详情样本"; await Task.Delay(850); inspiration.UpdateLayout();
        var saved = controller.Engine.State.Tasks.Single(x => x.Title == "快捷保存样本");
        Check(inspiration.QuickEntryActive && Quick() == quick && inputs[1].Text == "详情样本", "自动保存保留同一个输入卡片及当前内容");
        Check(Descendants<TaskCard>(inspiration.Root).All(x => x.DataContext is not WorkItem task || task.Id != saved.Id), "录入期间没有重复显示正式卡片");
        Check((quick.TranslatePoint(new Point(), inspiration.Root) - origin).Length < 1, "自动保存不移动快捷卡片的位置");
        Render(quick, 260, "quick-entry.png");
        inputs[1].Text = "点击✓前最后字符"; finish.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await controller.Flush(); inspiration.UpdateLayout();
        var submitted = inspiration.Locate(saved.Id)!;
        Check(!inspiration.QuickEntryActive && controller.Engine.State.Tasks.Single(x => x.Id == saved.Id).Stages[0].Text == "点击✓前最后字符", "✓立即保存最后字符并结束录入");
        Check(submitted is not null && inspiration.NewEntryIsLast && Math.Abs(submitted.ActualHeight - quick.ActualHeight) < 20, "结束后同位置正式卡片高度变化小于20，新建入口仍在末尾");
        OpenQuick(); inputs = Descendants<TextBox>(Quick()).ToArray(); inputs[1].Text = "收起前最后输入";
        controller.SetExpanded(false); await Task.Delay(160);
        Check(!inspiration.QuickEntryActive && controller.Engine.State.Tasks.Any(x => x.Stages[0].Text == "收起前最后输入"), "收起保存快捷最后输入并结束编辑");
        controller.SetExpanded(true); await Task.Delay(160); var beforeEmpty = controller.Engine.State.Tasks.Count;
        OpenQuick(); Descendants<Button>(Quick()).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(controller.Engine.State.Tasks.Count == beforeEmpty, "空快捷输入结束不创建任务");
        var editor = controller.EditTask(item.Id); await Task.Delay(160); Descendants<TextBox>(editor.Root).First().Text = "暂存未提交修改";
        var host = editor.Host!; var hides = host.NativeHideCount;
        controller.ObserveOutsidePress(host, false); await Task.Delay(140);
        Check(host.IsVisible && !host.IsScrollAnimating && host.NativeHideCount == hides && controller.Expanded, "一次外点不隐藏、不播放开合，保留看板");
        controller.ObserveOutsidePress(host, true); controller.ObserveOutsidePress(host, false);
        Check(controller.Editors.Contains(editor), "窗口内点击清零，下一次外点不关闭");
        controller.ObserveOutsidePress(host, false); await Task.Delay(160);
        Check(!controller.Editors.Contains(editor) && controller.Engine.State.Tasks.Single(x => x.Id == item.Id).Title == "青色任务名称", "两次外点关闭编辑且不提交正式任务");
        var resumed = controller.EditTask(item.Id); await Task.Delay(160);
        Check(resumed.Session.Working.Title == "暂存未提交修改" && resumed.Session.Id == editor.Session.Id, "重开恢复同一编辑会话和未提交内容");
        controller.Settings(); await Task.Delay(160); var settings = Application.Current.Windows.OfType<SettingsSurface>().Single();
        Check(controller.DismissibleWindow() == settings, "设置是当前最上层关闭目标");
        controller.ObserveOutsidePress(settings, false); Check(settings.IsVisible, "设置第一次外点保持可见");
        controller.ObserveOutsidePress(settings, false); await Task.Delay(160);
        Check(!settings.IsVisible && resumed.Host!.IsVisible && controller.Expanded, "设置第二次外点只关闭设置，保留编辑和看板");
        resumed.Suspend();
        var first = controller.NewTask(Lane.Paused); await Task.Delay(160); Descendants<TextBox>(first.Root).First().Text = "暂存草稿一";
        var second = controller.NewTask(Lane.Paused); await Task.Delay(160); Descendants<TextBox>(second.Root).First().Text = "暂存草稿二";
        Check(Descendants<TextBlock>(second.Root).Any(x => x.Text == "平台（多选）"), "任务多选字段显示平台，保持已有数据结构");
        Check(!first.IsHitTestVisible && second.IsHitTestVisible, "后台模拟卡片控件不接收输入，仅前台可编辑");
        first.Activate(); await Task.Delay(340);
        Descendants<Button>(first.Root).Single(x => Equals(x.Tag, "EditorClose")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(180);
        Check(!second.Host?.IsVisible ?? true, "新建X关闭整组宿主");
        Check(controller.Editors.All(x => x.Session.TaskId is not null) && controller.Engine.State.Sessions.Count(x => x.TaskId is null && !x.Visible) == 2, "新建X保留全部暂存内容且不露出另一窗口");
        controller.OpenNewEntry(Lane.Paused); await Task.Delay(180);
        Check(controller.ActiveDraft == first.Session.Id, "整组X重开恢复关闭前的前台草稿");
        first = controller.Editors.Single(x => x.Session.Working.Title == "暂存草稿一"); second = controller.Editors.Single(x => x.Session.Working.Title == "暂存草稿二");
        second.Activate(); await Task.Delay(340);
        host = second.Host!; controller.ObserveOutsidePress(host, false); controller.ObserveOutsidePress(host, false); await Task.Delay(160);
        Check(!host.IsVisible && controller.Engine.State.Sessions.Count(x => x.TaskId is null && !x.Visible && x.Working.HasContent) == 2, "两次外点关闭整个新建宿主并暂存两份草稿");
        controller.OpenNewEntry(Lane.Paused); await Task.Delay(160);
        Check(controller.Editors.Count(x => x.Session.TaskId is null) == 2, "重开原列新建恢复原草稿组");
        first = controller.Editors.Single(x => x.Session.Working.Title == "暂存草稿一"); second = controller.Editors.Single(x => x.Session.Working.Title == "暂存草稿二");
        Check(first.DraftNumber == 1 && second.DraftNumber == 2, "现有草稿按创建先后编号，重开不改变次序");
        var badge = Descendants<Border>(first.Root).Single(x => Equals(x.Tag, "DraftOrdinal"));
        var frontClose = Descendants<Button>(second.Root).Single(x => Equals(x.Tag, "EditorClose"));
        Check(badge.Visibility == Visibility.Visible && !badge.IsHitTestVisible && frontClose.Visibility == Visibility.Visible, "后台只显示编号气泡，前台保留X");
        Check(frontClose.Width == 40 && frontClose.Height == 40 && badge.Width < 40 && badge.Height < 40, "前台X扩大为40，编号维持旧按钮尺寸");
        var stack = (DraftStackSurface)second.Host!; var moveArea = Descendants<Border>(stack.Root).Single(x => Equals(x.Tag, "DraftStripMoveArea"));
        Check(moveArea.Height == 72 && moveArea.InputHitTest(new Point(10, 20)) == moveArea, "右侧顶部72高度是独立移动区域");
        for (var wait = 0; (stack.IsScrollAnimating || stack.IsContentSwapping) && wait < 100; wait++) await Task.Delay(20);
        Render(stack.Root, stack.Width, "numbered-stack.png", stack.Height);
        var seam = new RenderTargetBitmap((int)Math.Ceiling(stack.Width), (int)Math.Ceiling(stack.Height), 96, 96, PixelFormats.Pbgra32); seam.Render(stack.Root);
        var seamPixel = new byte[4]; seam.CopyPixels(new Int32Rect((int)second.Width - 2, 4, 1, 1), seamPixel, 4, 0);
        Check(seamPixel[3] >= 180, "堆叠顶部接缝有连续卡片底色，不暴露缺口");
        var swaps = stack.ContentSwapCount; var stackHides = stack.NativeHideCount;
        first.Activate();
        Check(stack.ContentSwapCount == swaps + 1 && stack.IsContentSwapping && stack.UsesIndependentAnimation, "草稿切换启动200毫秒扑克牌系统合成衔接");
        var apart = DraftSwitchMotion.Pose(.45, first.Width, first.Width / 5);
        Check(apart.NewX - apart.OldX >= first.Width + 12 && DraftSwitchMotion.Pose(.6, first.Width, first.Width / 5).NewY < 0, "两张卡片完全分开后选中卡片抬起");
        var end = DraftSwitchMotion.Pose(1, first.Width, first.Width / 5);
        Check(end.NewX == 0 && end.NewY == 0 && Math.Abs(end.OldX - first.Width / 5) < .001, "扑克牌归位后选中卡片盖在前台，旧卡片露出五分之一");
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "swap-timing.json"), JsonSerializer.Serialize(new { stack.LastContentSwapPreparationMs }));
        Check(double.IsFinite(stack.LastContentSwapPreparationMs) && stack.LastContentSwapPreparationMs >= 0, "记录屏幕外准备耗时；交互速度以实机桌面采样验收");
        second.Activate(); await Task.Delay(340);
        Check(!stack.IsContentSwapping && controller.ActiveDraft == second.Session.Id && stack.NativeHideCount == stackHides, "快速切换以最后选择为准，无宿主隐藏和动画残留");
        first.Activate(); controller.SetExpanded(false); await Task.Delay(180); controller.SetExpanded(true); await Task.Delay(180);
        Check(!stack.IsContentSwapping && stack.IsVisible && first.Session.Working.Title == "暂存草稿一", "切换途中球收起恢复后输入和宿主保持正常");
        controller.DeleteDraft(first); await Task.Delay(180);
        Check(controller.Engine.State.Sessions.All(x => x.Id != first.Session.Id) && second.DraftNumber == 1, "删除草稿清理暂存并对现有草稿连续重新编号");
        Check(Descendants<Button>(second.Root).Any(x => Equals(x.Content, "删除任务")), "新建左下提供删除任务胶囊");
        var deleteEditor = controller.EditTask(item.Id); await Task.Delay(180);
        Descendants<Button>(deleteEditor.Root).Single(x => Equals(x.Content, "删除任务")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(controller.Engine.State.Tasks.All(x => x.Id != item.Id), "编辑左下删除正式任务并关闭编辑");
        controller.Undo(); Check(controller.Engine.State.Tasks.Any(x => x.Id == item.Id), "编辑删除正式任务可以撤销");
        await controller.Flush();
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "compact-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, desktopShown = false, mouseMoved = false, realRegistryChanged = false, lastSwapPreparationMs = stack.LastContentSwapPreparationMs }, new JsonSerializerOptions { WriteIndented = true }));
        void OpenQuick() => Descendants<Button>(inspiration.Root).Single(x => Equals(x.Content, "＋ 新建任务")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Border Quick() => Descendants<Border>(inspiration.Root).Single(x => Equals(x.Tag, "QuickTaskEntry"));
        void Render(FrameworkElement view, double width, string filename, double? height = null)
        {
            view.Measure(new Size(width, height ?? double.PositiveInfinity)); var renderHeight = height ?? view.DesiredSize.Height;
            view.Arrange(new Rect(0, 0, width, renderHeight)); view.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), Math.Max(1, (int)Math.Ceiling(renderHeight)), 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(controller.Store.DirectoryPath, filename)); png.Save(stream);
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
