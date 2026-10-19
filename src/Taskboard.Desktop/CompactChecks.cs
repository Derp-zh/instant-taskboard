using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    [DllImport("user32.dll", EntryPoint = "GetDoubleClickTime")] private static extern uint NativeDoubleClickTime();
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
        controller.Engine.AddStage(staged); staged.Working.Stages[1].Text = "白色并行详情"; staged.Working.Stages[1].IsRunning = true; var item = controller.Engine.Submit(staged);
        var card = new TaskCard { Controller = controller, DataContext = item }; Render(card, 260, "formal-card.png");
        var title = Descendants<TextBlock>(card).Single(x => Equals(x.Tag, "TaskTitle"));
        Check(title.Foreground == Ui.Accent && title.FontWeight == FontWeights.SemiBold && title.FontSize == 13, "任务标题青色加粗且字号保持13");
        Check(Descendants<Border>(card).Single(x => Equals(x.Tag, "TaskTitleMarker")).Background == Ui.Accent, "任务标题前有青色细竖条");
        var details = Descendants<TextBlock>(card).Where(x => Equals(x.Tag, "TaskStageDetails")).ToArray();
        Check(details.Length == 2 && details.All(x => x.Foreground == Ui.Text && x.FontSize == 14), "两个运行阶段同时显示白色详情，字号统一为14");
        Check(Descendants<Border>(card).Where(x => Equals(x.Tag, "TaskStageName")).All(x => ((MarqueeText)x.Child).Label.FontSize == 12), "阶段气泡字号增加为12");
        OpenQuick(); var quick = Quick(); var inputs = Descendants<TextBox>(quick).ToArray();
        Check(quick.Padding == card.Padding && quick.CornerRadius == card.CornerRadius && quick.BorderBrush == card.BorderBrush, "快捷新建与正式卡片共用边框圆角和紧凑内边距");
        Check(inputs.Length == 2 && inputs[0].Foreground == Ui.Accent && inputs[1].Foreground == Ui.Text && inputs[1].FontSize == 14, "快捷输入与正式卡片使用相同文字配色和详情字号");
        var finish = Descendants<Button>(quick).Single(x => Equals(x.Tag, "QuickFinish"));
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
        Check(submitted is not null && inspiration.NewEntryIsLast && inputs[1].MinLines == 2, "结束后只保留正式卡片，新建入口仍在末尾；快捷详情保持新的两行下限");
        OpenQuick(); inputs = Descendants<TextBox>(Quick()).ToArray(); inputs[1].Text = "收起前最后输入";
        controller.SetExpanded(false); await Task.Delay(160);
        Check(!inspiration.QuickEntryActive && controller.Engine.State.Tasks.Any(x => x.Stages[0].Text == "收起前最后输入"), "收起保存快捷最后输入并结束编辑");
        controller.SetExpanded(true); await Task.Delay(160); var beforeEmpty = controller.Engine.State.Tasks.Count;
        OpenQuick(); Descendants<Button>(Quick()).Single(x => Equals(x.Tag, "QuickFinish")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(controller.Engine.State.Tasks.Count == beforeEmpty, "空快捷输入结束不创建任务");
        var editor = controller.EditTask(item.Id);
        Check(editor.Host!.ShouldRestore, "首次打开仍在准备白条时已登记悬浮窗口，可收起恢复且不会被重复创建");
        await Settled(editor.Host!); Descendants<TextBox>(editor.Root).First().Text = "暂存未提交修改";
        var host = editor.Host!; var hides = host.NativeHideCount;
        var reminderToggle = Descendants<CheckBox>(editor.Root).Single(x => Equals(x.Tag, "ReminderToggle"));
        Check(reminderToggle.Width == 22 && reminderToggle.Height == 22 && reminderToggle.Content is null && reminderToggle.Clip is EllipseGeometry circle && circle.RadiusX == 11 && circle.RadiusY == 11 && ((Panel)reminderToggle.Parent).Children.OfType<TextBlock>().Any(x => x.Text == "定时提醒"), "定时提醒只有22单位勾选圆命中，文字在独立控件中");
        controller.ObserveOutsidePress(host, false); await Task.Delay(140);
        Check(host.IsVisible && !host.IsScrollAnimating && host.NativeHideCount == hides && controller.Expanded, "一次外点不隐藏、不播放开合，保留看板");
        var gesturePoint = new Point(100, 100);
        controller.ObserveOutsidePress(host, false, gesturePoint, 1000, taskCard: true);
        controller.ObserveOutsidePress(host, false, gesturePoint, 1050, taskCard: true);
        Check(controller.Editors.Contains(editor) && host.ShouldRestore, "任务卡片双击不参与外部关闭，保留已有悬浮窗口");
        controller.ObserveOutsidePress(host, false, gesturePoint, 1100);
        Check(controller.Editors.Contains(editor), "卡片点击打断关闭手势，之后第一次空白点击仍不关闭");
        var slowInterval = checked((int)NativeDoubleClickTime()) + 100;
        controller.ObserveOutsidePress(host, true);
        controller.ObserveOutsidePress(host, false, gesturePoint, 1000);
        controller.ObserveOutsidePress(host, false, gesturePoint, 1000 + slowInterval);
        Check(controller.Editors.Contains(editor), "超过系统双击时间的慢速外点保持编辑打开");
        controller.ObserveOutsidePress(host, false, new Point(300, 100), 1010 + slowInterval);
        Check(controller.Editors.Contains(editor), "快速但相距200像素的外点保持编辑打开");
        controller.ObserveOutsidePress(host, false, new Point(300, 100), 1020 + slowInterval, leftButton: false);
        controller.ObserveOutsidePress(host, false, new Point(300, 100), 1030 + slowInterval);
        Check(controller.Editors.Contains(editor), "右键打断双击序列，下次左外点保持打开");
        controller.ObserveOutsidePress(host, true); controller.ObserveOutsidePress(host, false);
        Check(controller.Editors.Contains(editor), "窗口内点击清零，下一次外点不关闭");
        controller.ObserveOutsidePress(host, false); await Hidden(host);
        Check(!controller.Editors.Contains(editor) && controller.Engine.State.Tasks.Single(x => x.Id == item.Id).Title == "暂存未提交修改", "外部左键快速双击正式保存修改后关闭编辑");
        var resumed = controller.EditTask(item.Id); await Settled(resumed.Host!);
        Check(resumed.Session.Working.Title == "暂存未提交修改" && resumed.Session.Id != editor.Session.Id, "重开从正式保存的内容建立新的编辑副本");
        var resumedHost = resumed.Host!; var resumedHides = resumedHost.NativeHideCount; var resumedMotion = resumedHost.MotionGeneration;
        controller.EditTask(item.Id);
        Check(resumed.Host == resumedHost && resumedHost.NativeHideCount == resumedHides && resumedHost.MotionGeneration == resumedMotion, "重复编辑保持同一窗口及内容，不重播打开动画");
        controller.Settings(); controller.Settings(); await Task.Delay(160); var settings = Application.Current.Windows.OfType<SettingsSurface>().Single();
        await Settled(settings);
        Check(controller.DismissibleWindow() == settings, "设置是当前最上层关闭目标");
        var settingsHides = settings.NativeHideCount; var settingsRoot = settings.Root.Children[1]; var settingsMotion = settings.MotionGeneration;
        controller.Settings();
        Check(settings.Root.Children[1] == settingsRoot && settings.NativeHideCount == settingsHides && settings.MotionGeneration == settingsMotion, "重复设置保持同一内容与窗口，不重播打开动画");
        controller.ObserveOutsidePress(settings, true); Check(settings.IsVisible, "设置内部点击保持可见");
        controller.ObserveOutsidePress(settings, false, gesturePoint, 2000); await Task.Delay(140);
        Check(settings.IsVisible && !settings.IsScrollAnimating && controller.Expanded, "设置外部单击保持可见，不播放关闭或重开动画");
        controller.ObserveOutsidePress(settings, false, gesturePoint, 2000 + Math.Min(70, NativeDoubleClickTime())); await Hidden(settings);
        Check(!settings.IsVisible && resumed.Host!.IsVisible && controller.Expanded, "设置同样由外部快速双击关闭，保留编辑和看板");
        controller.SuppressCollapse = false;
        controller.ObserveWorkspaceOutsidePress(true); await Hidden(resumedHost);
        Check(!controller.Expanded && !resumedHost.IsVisible && controller.Editors.Contains(resumed) && resumed.Session.Visible, "点击其他应用连同悬浮窗口收起整组，编辑会话保持打开");
        controller.SetExpanded(true); await Settled(resumedHost);
        Check(resumed.Host == resumedHost && resumedHost.IsVisible && resumed.Session.Working.Title == "暂存未提交修改" && !settings.IsVisible, "重新展开恢复同一编辑窗口和最后输入，原本已关设置不重开");
        resumed.Suspend(); await Task.Delay(180); await Task.Delay(550);
        Check(controller.Expanded && !controller.HasFloatingWindow, "关闭最后悬浮窗口本身不延迟收起看板");
        controller.ObserveWorkspaceOutsidePress(false);
        Check(controller.Expanded, "没有悬浮窗口时点击本程序也不自动收起");
        controller.ObserveWorkspaceOutsidePress(true);
        Check(!controller.Expanded, "没有悬浮窗口的新一次其他应用点击才收起列表");
        controller.SuppressCollapse = true; controller.SetExpanded(true); await Task.Delay(180);
        var confirm = Ui.Confirm("隔离验证通用悬浮窗口"); await Task.Delay(200);
        var prompt = controller.DismissibleWindow()!;
        await Settled(prompt);
        Check(prompt.Title == "即时任务看板提示", "通用提示窗口也登记为最上层悬浮窗口");
        controller.ObserveOutsidePress(prompt, false, gesturePoint, 4000);
        Check(prompt.ShouldRestore && !confirm.IsCompleted, "通用提示单击外部保持打开");
        controller.ObserveOutsidePress(prompt, false, gesturePoint, 4000 + Math.Min(70, NativeDoubleClickTime())); await Hidden(prompt);
        Check(confirm.IsCompletedSuccessfully && !await confirm && !controller.HasFloatingWindow, "通用提示外部双击关闭，确认流程按取消结束，无悬挂等待");
        var empty = controller.NewTask(Lane.Action); await Task.Delay(180);
        Check(!controller.Engine.State.Sessions.Contains(empty.Session) && empty.Session.StatusSelected && empty.Session.Working.Lane == Lane.Action, "列表新建预选待操作，默认阶段0不暂存");
        var neutral = controller.NewTask(); await Task.Delay(180);
        Check(!controller.Editors.Contains(empty) && !neutral.Session.StatusSelected && Descendants<ComboBox>(neutral.Root).First().SelectedIndex == -1, "共享入口不继承状态且清理空白后台页");
        var sharedHost = neutral.Host!; var sharedHides = sharedHost.NativeHideCount;
        var externalNew = controller.OpenNewEntry(Lane.Action); await Settled(externalNew.Host!);
        Check(externalNew != neutral && externalNew.Host == sharedHost && externalNew.Session.StatusSelected && externalNew.Session.Working.Lane == Lane.Action && sharedHost.NativeHideCount == sharedHides && !sharedHost.IsScrollAnimating, "已有新建窗口时列表按钮直接新建、预选该列，共用宿主且不重播打开动画");
        controller.DeleteDraft(externalNew); await Task.Delay(180);
        neutral = controller.NewTask(); await Task.Delay(180);
        neutral.Session.Working.Title = "未选择状态也可暂存"; controller.Engine.StageSession(neutral.Session);
        await controller.Flush();
        Check(!controller.Store.Load().Sessions.Single(x => x.Id == neutral.Session.Id).StatusSelected, "未选择状态的非空草稿在磁盘中正确保留");
        controller.DeleteDraft(neutral); await Task.Delay(180);
        var first = controller.NewTask(Lane.Paused); await Settled(first.Host!); LogicalInputs(first).First().Text = "暂存草稿一";
        var second = controller.NewTask(Lane.Paused); await Settled(second.Host!); LogicalInputs(second).First().Text = "暂存草稿二";
        Check(Descendants<TextBlock>(second.Root).Any(x => x.Text == "平台（多选）"), "任务多选字段显示平台，保持已有数据结构");
        Check(!first.IsHitTestVisible && second.IsHitTestVisible, "后台模拟卡片控件不接收输入，仅前台可编辑");
        first.Activate(); await Settled(first.Host!);
        Descendants<Button>(first.Root).Single(x => Equals(x.Tag, "EditorClose")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(180);
        Check(!second.Host?.IsVisible ?? true, "新建X关闭整组宿主");
        Check(controller.Editors.All(x => x.Session.TaskId is not null) && controller.Engine.State.Sessions.Count(x => x.TaskId is null && !x.Visible) == 2, "新建X保留全部暂存内容且不露出另一窗口");
        var freshAfterClose = controller.OpenNewEntry(Lane.Action); await Task.Delay(180);
        Check(controller.Editors.Count(x => x.Session.TaskId is null) == 3 && controller.Editors.Count(x => x.Session.Working.HasContent && x.Session.Working.Lane == Lane.Paused) == 2, "从不同列表新建时恢复旧草稿并额外创建独立的当前列表卡片");
        Check(controller.ActiveDraft == freshAfterClose.Session.Id && freshAfterClose.Session.Working.Lane == Lane.Action && !freshAfterClose.Session.Working.HasContent, "列表新建前台是新卡片，旧草稿及状态没有覆盖");
        controller.DeleteDraft(freshAfterClose);
        first = controller.Editors.Single(x => x.Session.Working.Title == "暂存草稿一"); second = controller.Editors.Single(x => x.Session.Working.Title == "暂存草稿二");
        second.Activate(); await Settled(second.Host!);
        host = second.Host!; controller.ObserveOutsidePress(host, false); controller.ObserveOutsidePress(host, false); await Hidden(host);
        Check(!host.IsVisible && controller.Engine.State.Sessions.Count(x => x.TaskId is null && !x.Visible && x.Working.HasContent) == 2, "外部双击关闭整个新建宿主并暂存两份草稿");
        var sameListFresh = controller.OpenNewEntry(Lane.Paused); await Settled(host);
        Check(controller.Editors.Count(x => x.Session.TaskId is null) == 3 && !sameListFresh.Session.Working.HasContent, "同一列表新建也建立新卡片，保留旧草稿组");
        controller.DeleteDraft(sameListFresh); await Settled(host); host.Root.UpdateLayout();
        first = controller.Editors.Single(x => x.Session.Working.Title == "暂存草稿一"); second = controller.Editors.Single(x => x.Session.Working.Title == "暂存草稿二");
        Check(first.DraftNumber == 1 && second.DraftNumber == 2, "现有草稿按创建先后编号，重开不改变次序");
        var badge = Descendants<Border>(first.Root).Single(x => Equals(x.Tag, "DraftOrdinal"));
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "badge-size.json"), JsonSerializer.Serialize(new { badge.Width, badge.Height, Radius = badge.CornerRadius.TopLeft }));
        var frontClose = Descendants<Button>(second.Root).Single(x => Equals(x.Tag, "EditorClose"));
        Check(badge.Visibility == Visibility.Visible && !badge.IsHitTestVisible && frontClose.Visibility == Visibility.Visible, "后台只显示编号气泡，前台保留X");
        Check(frontClose.Width == 48 && frontClose.Height == 40 && frontClose.BorderBrush == Brushes.White && badge.Width < 38 && badge.Width >= 28 && badge.Height == badge.Width && badge.CornerRadius.TopLeft == badge.Width / 2 && badge.BorderBrush == Brushes.White && ((SolidColorBrush)badge.Background).Color == ((SolidColorBrush)frontClose.Background).Color, "编号恢复原始小尺寸圆形，白边与亮度保留，X保持48×40");
        Check(Descendants<Button>(second.Root).Single(x => Equals(x.Tag, "AddStage")).HorizontalAlignment == HorizontalAlignment.Left, "新阶段仅改为左对齐，原纵向布局和按钮尺寸保持");
        var stack = (DraftStackSurface)second.Host!; var moveArea = Descendants<Border>(stack.Root).Single(x => Equals(x.Tag, "DraftStripMoveArea"));
        Check(moveArea.Height == 72 && moveArea.InputHitTest(new Point(10, 20)) == moveArea, "右侧顶部72高度是独立移动区域");
        for (var wait = 0; (stack.IsScrollAnimating || stack.IsContentSwapping) && wait < 100; wait++) await Task.Delay(20);
        Render(stack.Root, stack.Width, "numbered-stack.png", stack.Height);
        var seam = new RenderTargetBitmap((int)Math.Ceiling(stack.Width), (int)Math.Ceiling(stack.Height), 96, 96, PixelFormats.Pbgra32); seam.Render(stack.Root);
        var seamPixel = new byte[4]; seam.CopyPixels(new Int32Rect((int)second.Width - 2, 4, 1, 1), seamPixel, 4, 0);
        Check(seamPixel[3] >= 180, "堆叠顶部接缝有连续卡片底色，不暴露缺口");
        var lowerPixel = new byte[4]; seam.CopyPixels(new Int32Rect((int)second.Width - 2, (int)stack.Height - 5, 1, 1), lowerPixel, 4, 0);
        Check(lowerPixel[3] >= 180, "堆叠底部圆角缺口同样有连续底色");
        controller.SetFloatingTransparency(50); stack.Root.UpdateLayout();
        var half = new RenderTargetBitmap((int)Math.Ceiling(stack.Width), (int)Math.Ceiling(stack.Height), 96, 96, PixelFormats.Pbgra32); half.Render(stack.Root);
        var center = new byte[4]; var overlap = new byte[4];
        half.CopyPixels(new Int32Rect(8, 200, 1, 1), center, 4, 0); half.CopyPixels(new Int32Rect((int)second.Width - 8, 200, 1, 1), overlap, 4, 0);
        Check(Math.Abs(center[3] - overlap[3]) <= 2 && overlap[3] < 140, "50%透明时原补底竖条处与单层区域的透明度一致，不叠深颜色");
        controller.SetFloatingTransparency(10);
        var newHit = Descendants<Grid>(stack.Root).Single(x => Equals(x.Tag, "DraftNewHitArea"));
        var newOutline = Descendants<System.Windows.Shapes.Rectangle>(stack.Root).Single(x => Equals(x.Tag, "DraftNewOutline"));
        Check(Math.Abs(newOutline.TranslatePoint(new Point(second.Width * 4 / 5, 0), stack.Root).X - (newHit.TranslatePoint(new Point(), stack.Root).X - 20)) < .001 && newHit.Width == second.Width / 5 - 20, "虚线左移20对齐圆角起点，新建触发起点保持且右侧裁掉20");
        Check(newHit.InputHitTest(new Point(newHit.Width + 1, 200)) is null && newHit.InputHitTest(new Point(-1, 200)) is null, "新建触发范围不扩到左侧补口或裁掉的右侧");
        var swaps = stack.ContentSwapCount; var stackHides = stack.NativeHideCount;
        first.Activate();
        Check(stack.ContentSwapCount == swaps + 1 && stack.IsContentSwapping && stack.UsesIndependentAnimation, $"草稿切换启动{DraftSwitchMotion.DurationMs:F2}毫秒纯横向抽牌合成衔接");
        var apart = DraftSwitchMotion.Pose(150 / DraftSwitchMotion.BaseDurationMs, first.Width, first.Width / 5);
        Check(apart.OldX == 0 && apart.NewX >= first.Width + 12, "旧卡等待，选中卡向右抽出至完全分开");
        var poses = Enumerable.Range(0, 101).Select(i => DraftSwitchMotion.Pose(i / 100.0, first.Width, first.Width / 5)).ToArray();
        Check(poses.All(x => x.OldX >= 0 && x.NewY == 0) && poses.Zip(poses.Skip(1)).All(x => x.Second.OldX >= x.First.OldX), "旧卡只向右，选中卡抽出后向左，所有画面无纵向位移");
        var end = DraftSwitchMotion.Pose(1, first.Width, first.Width / 5);
        Check(end.NewX == 0 && end.NewY == 0 && Math.Abs(end.OldX - first.Width / 5) < .001, "扑克牌归位后选中卡片盖在前台，旧卡片露出五分之一");
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "swap-timing.json"), JsonSerializer.Serialize(new { stack.LastContentSwapPreparationMs }));
        Check(double.IsFinite(stack.LastContentSwapPreparationMs) && stack.LastContentSwapPreparationMs >= 0, "记录屏幕外准备耗时；交互速度以实机桌面采样验收");
        second.Activate();
        await Settled(stack);
        Check(stack.ContentSwapCount == swaps + 1 && controller.ActiveDraft == first.Session.Id, "动画前2/3的再次点击被忽略，不额外切换");
        second.Activate();
        var earlyPress = Environment.TickCount64;
        for (var wait = 0; !stack.CanQueueContentSwap(Environment.TickCount64) && wait < 150; wait++) await Task.Delay(5);
        Check(stack.IsContentSwapping, "在切换最后1/3期间验证后续点击");
        Check(!stack.CanQueueContentSwap(earlyPress), "按原始按下时间判断，提前点击不会因界面延迟变成有效点击");
        first.Activate(); second.Activate();
        await Settled(stack);
        Check(stack.ContentSwapCount == swaps + 3 && controller.ActiveDraft == first.Session.Id && stack.NativeHideCount == stackHides, "动画最后1/3仅登记一次后续切换，多次点击不会替换已登记目标");
        controller.ShowEditor(second, true, earlyPress); await Settled(stack);
        Check(stack.ContentSwapCount == swaps + 3 && controller.ActiveDraft == first.Session.Id, "动画完成后的迟到投递仍丢弃原先过早的点击");
        first.Activate(); controller.SetExpanded(false); await Hidden(stack); controller.SetExpanded(true); await Settled(stack);
        Check(!stack.IsContentSwapping && stack.IsVisible && first.Session.Working.Title == "暂存草稿一", "切换途中球收起恢复后输入和宿主保持正常");
        controller.DeleteDraft(first); await Task.Delay(180);
        Check(controller.Engine.State.Sessions.All(x => x.Id != first.Session.Id) && second.DraftNumber == 1, "删除草稿清理暂存并对现有草稿连续重新编号");
        Check(Descendants<Button>(second.Root).Any(x => Equals(x.Content, "删除任务")), "新建左下提供删除任务胶囊");
        var deleteEditor = controller.EditTask(item.Id); await Task.Delay(180);
        var columnOpacity = controller.Columns[0].GlassTransparency; var ballOpacity = controller.Ball.GlassTransparency;
        controller.SetFloatingTransparency(33);
        Check(deleteEditor.Host!.GlassTransparency == 33 && second.Host!.GlassTransparency == 33 && settings.GlassTransparency == 33 && controller.Columns[0].GlassTransparency == columnOpacity && controller.Ball.GlassTransparency == ballOpacity, "调悬浮窗口透明度同时更新新建、编辑、设置，不改变多列表及球");
        controller.SetGlassTransparency(44);
        Check(controller.Columns[0].GlassTransparency == 44 && settings.GlassTransparency == 33 && controller.Ball.GlassTransparency == ballOpacity, "调多列表透明度不改变悬浮窗口及球");
        controller.SetGlassTransparency(20); controller.SetFloatingTransparency(10);
        Descendants<Button>(deleteEditor.Root).Single(x => Equals(x.Content, "删除任务")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(controller.Engine.State.Tasks.All(x => x.Id != item.Id), "编辑左下删除正式任务并关闭编辑");
        controller.Undo(); Check(controller.Engine.State.Tasks.Any(x => x.Id == item.Id), "编辑删除正式任务可以撤销");
        for (var i = 0; i < 9; i++) { var pagedDraft = controller.NewTask(Lane.Action); await Task.Delay(180); LogicalInputs(pagedDraft).First().Text = "跨页共享草稿" + i; controller.Engine.StageSession(pagedDraft.Session); }
        controller.CloseDraftWindow(); await Task.Delay(180); controller.OpenNewEntry(Lane.Action); await Task.Delay(180);
        Check(controller.Editors.Any(x => x.Session.TaskId is null && x.Host is null), "共享池超过可见槽位时包含未显示的翻页草稿");
        var pagedDraftCount = controller.Engine.State.Sessions.Count(x => x.TaskId is null && x.Working.HasContent);
        controller.CloseDraftWindow(); await Task.Delay(180);
        Check(controller.Editors.All(x => x.Session.TaskId is not null) && controller.Engine.State.Sessions.Count(x => x.TaskId is null && !x.Visible && x.Working.HasContent) == pagedDraftCount, "整组关闭同时清理所有页的界面、保留全部非空草稿");
        await controller.Flush();
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "compact-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, switchSpeed = DraftSwitchMotion.Speed, switchDurationMs = DraftSwitchMotion.DurationMs, desktopShown = false, mouseMoved = false, realRegistryChanged = false, lastSwapPreparationMs = stack.LastContentSwapPreparationMs }, new JsonSerializerOptions { WriteIndented = true }));
        void OpenQuick() => Descendants<Button>(inspiration.Root).Single(x => Equals(x.Content, "＋ 新建任务")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Settled(SurfaceWindow window)
        {
            for (var wait = 0; (!window.IsVisible || window.IsScrollAnimating || window.IsContentSwapHandoff) && wait < 200; wait++) await Task.Delay(20);
            if (!window.IsVisible || window.IsScrollAnimating || window.IsContentSwapHandoff) throw new InvalidOperationException("首次开合未完成：" + window.Title);
        }
        async Task Hidden(SurfaceWindow window)
        {
            for (var wait = 0; window.IsVisible && wait < 200; wait++) await Task.Delay(20);
            if (window.IsVisible) throw new InvalidOperationException("收起未完成：" + window.Title);
        }
        Border Quick() => Descendants<Border>(inspiration.Root).Single(x => Equals(x.Tag, "QuickTaskEntry"));
        void Render(FrameworkElement view, double width, string filename, double? height = null)
        {
            view.Measure(new Size(width, height ?? double.PositiveInfinity)); var renderHeight = height ?? view.DesiredSize.Height;
            view.Arrange(new Rect(0, 0, width, renderHeight)); view.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), Math.Max(1, (int)Math.Ceiling(renderHeight)), 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(controller.Store.DirectoryPath, filename)); png.Save(stream);
        }
    }
    private static IEnumerable<TextBox> LogicalInputs(DependencyObject root)
    {
        if (root is TextBox input) yield return input;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in LogicalInputs(child)) yield return nested;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        // Offscreen HwndHost controls can release their template visuals.
        // Include logical controls and de-duplicate the two tree views.
        var seen = new HashSet<DependencyObject>(); var pending = new Stack<DependencyObject>(); pending.Push(root);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current)) continue;
            if (current is T value) yield return value;
            var children = new List<DependencyObject>();
            if (current is Visual)
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++) children.Add(VisualTreeHelper.GetChild(current, i));
            children.AddRange(LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>());
            foreach (var child in children.AsEnumerable().Reverse()) pending.Push(child);
        }
    }
}
