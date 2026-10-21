using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class Ux0922Checks
{
    internal static async Task Run(BoardController controller)
    {
        var root = controller.Store.DirectoryPath; var checks = new List<string>();
        void Check(bool value, string text)
        {
            if (!value) throw new InvalidOperationException(text);
            checks.Add(text); File.WriteAllText(Path.Combine(root, "ux0922-progress.json"), JsonSerializer.Serialize(checks));
        }
        var session = controller.Engine.NewSession(Lane.Running); session.Working.Title = "阶段删除界面隔离样本";
        var second = controller.Engine.AddStage(session); second.Text = "保留的详情";
        var view = new EditorSurface(controller, session);
        using var source = new HwndSource(new HwndSourceParameters("隔离阶段删除检查") { PositionX = -12000, PositionY = -12000,
            Width = 560, Height = (int)Math.Ceiling(EditorSurface.DefaultHeight), WindowStyle = unchecked((int)0x90000000), ExtendedWindowStyle = 0x08000080 });
        source.RootVisual = view; Layout(view, 560, EditorSurface.DefaultHeight);
        Button Remove(Guid id) => Visual<Button>(view).Single(x => Equals(x.Tag, "StageRemove") && x.DataContext is Stage s && s.Id == id);
        var first = session.Working.Stages[0];
        var remove = Remove(first.Id); var hintCount = Ui.HintCount;
        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(session.Working.Stages.Count == 2 && Ui.HintCount == hintCount + 1 && Ui.LastHint == BoardEngine.LastRunningStageMessage, "新建删除唯一运行阶段复用同一提示模块");
        second.IsRunning = true;
        view.StagePickers[second.Id].Input.Text = "保留搜索"; view.StagePickers[second.Id].Pane.Visibility = Visibility.Visible;
        Remove(first.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(view, 560, EditorSurface.DefaultHeight);
        Check(session.Working.Stages.Single().Id == second.Id && view.StagePickers.Count == 1 && view.StagePickers[second.Id].Input.Text == "保留搜索" && view.StagePickers[second.Id].Pane.Visibility == Visibility.Visible, "删除阶段后其余输入与展开关联区保留，删除的选择器已释放");
        hintCount = Ui.HintCount; Remove(second.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Ui.HintCount == hintCount + 1 && Ui.LastHint == BoardEngine.LastStageMessage && session.Working.Stages.Count == 1, "仅剩一个阶段时点击X不删除且只提示一次");
        var title = Visual<TextBox>(view).Single(x => Equals(x.ToolTip, "阶段名称")); remove = Remove(second.Id);
        var textBounds = title.TransformToAncestor(view).TransformBounds(new Rect(title.RenderSize));
        var xBounds = remove.TransformToAncestor(view).TransformBounds(new Rect(remove.RenderSize));
        Check(xBounds.Left >= textBounds.Right && Math.Abs(remove.ActualWidth - 24) < .1 && Math.Abs(remove.ActualHeight - 24) < .1, "圆形24×24删除按钮紧邻阶段名称右侧");
        Check(remove.InputHitTest(new Point(12, 12)) is not null, "新增X按钮内部命中有效（离屏WPF）");
        Snapshot(view, 560, EditorSurface.DefaultHeight, Path.Combine(root, "editor-stage-delete.png"));
        Check(await view.SaveAndClose(), "新建阶段删除后通过真实保存任务控件链提交");
        var item = controller.Engine.State.Tasks.Single();
        Check(controller.Store.Load().Tasks.Single().Stages.Single().Id == second.Id, "真实保存链落盘后仅保留未删阶段");
        var edit = controller.Engine.Edit(item.Id); var third = controller.Engine.AddStage(edit); third.Text = "删除测试";
        var editor = new EditorSurface(controller, edit); Layout(editor, 560, EditorSurface.DefaultHeight);
        Visual<Button>(editor).Single(x => Equals(x.Tag, "StageRemove") && x.DataContext is Stage s && s.Id == third.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(edit.Working.Stages.Count == 1 && controller.Engine.State.Tasks.Single().Stages.Count == 1, "编辑X删除未提交阶段不改变正式任务");
        Check(await editor.SaveAndClose() && controller.Store.Load().Tasks.Single().Stages.Count == 1, "编辑删除同样经正式保存和独立回读");

        var settings = new SettingsSurface(controller);
        var methods = new[] { ("主设置", "MainSettings"), ("窗口", "WindowSettings"), ("监听", "ListeningSettings"), ("归档", "ArchiveSettings"), ("数据与备份", "DataSettings") };
        foreach (var (name, method) in methods)
        {
            var raw = (StackPanel)typeof(SettingsSurface).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, null)!;
            var original = Logical<Control>(raw).ToArray();
            var options = original.OfType<System.Windows.Controls.Primitives.Selector>().ToDictionary(x => x, x => x.Items.Count);
            SettingsSurface.OrganizeSettingsPage(name, raw);
            Check(original.All(x => Logical<Control>(raw).Contains(x)) && options.All(x => x.Key.Items.Count == x.Value), "设置排版保留全部原控件事件与可选项：" + name);
        }
        foreach (var name in SettingsSurface.Sections)
        {
            settings.RefreshSection(); settings.SelectSection(name); Layout(settings.Root, 760, 680);
            Check(Visual<Button>(settings.Root).Any(x => Equals(x.Tag, "SettingsSave")), "所有设置页保留统一保存入口：" + name);
            Snapshot(settings.Root, 760, 680, Path.Combine(root, "settings-" + Array.IndexOf(SettingsSurface.Sections, name) + ".png"));
        }
        settings.SelectSection("窗口"); Layout(settings.Root, 760, 680);
        var deletions = Visual<Button>(settings.Root).Where(x => Equals(x.Tag, "ListDelete")).ToArray();
        Check(deletions.Length == 6 && deletions.All(x => !x.IsEnabled && x.DataContext is BoardColumn { Protected: true }), "窗口设置六个内置列表的删除动作均禁用");
        var custom = new BoardColumn { Name = "可删除自定义窗口" }; controller.Engine.State.Preferences.Columns.Add(custom); controller.Engine.Touch();
        settings.RefreshSection(); Layout(settings.Root, 760, 680);
        var deleteCustom = Visual<Button>(settings.Root).Single(x => Equals(x.Tag, "ListDelete") && ReferenceEquals(x.DataContext, custom));
        Check(deleteCustom.IsEnabled, "自定义窗口仍提供可操作删除按钮"); deleteCustom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(custom.Deleted, "设置删除空自定义窗口实际生效");
        controller.Engine.State.Preferences.GlassTransparency = 33; controller.Engine.Touch();
        Visual<Button>(settings.Root).Single(x => Equals(x.Tag, "SettingsSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await controller.Flush();
        Check(controller.Store.Load().Preferences.GlassTransparency == 33, "手动保存入口写入现有配置路径，保留自动保存逻辑");
        settings.Destroy(false);

        controller.Engine.Move(item.Id, Lane.Action, null);
        var ball = controller.Ball; ball.Width = ball.Height = 58; ball.UpdateBadge();
        ball.HideAtEdge(new Rect(-12000, -12000, 4000, 4000), animate: false); Layout(ball.Root, 370, 58);
        Check(ball.IsEdgeHidden && ball.CountBubble.Visibility == Visibility.Visible && ball.CountBubble.Opacity == 0 && ball.CountBubble.ActualWidth > 0, "边缘隐藏时数字不可见但已布局，免去结束帧重新布局");
        var frames = new List<object>();
        foreach (var progress in new[] { 0.0, .1, .25, .5, .75, .9, .99, .99999, 1.0 })
        {
            ball.ApplyEdgeBadgeFrame(progress, revealing: true);
            Check(progress < 1 ? ball.EdgeDot.Opacity > 0 && ball.CountBubble.Opacity == 0 : ball.EdgeDot.Opacity == 0 && ball.CountBubble.Opacity == 1,
                "真实红点与数字控件同帧交接，无重叠或结束后空帧：" + progress);
            frames.Add(new { progress, dot = ball.EdgeDot.Opacity, count = ball.CountBubble.Opacity, width = ball.CountBubble.ActualWidth });
        }
        ball.ApplyEdgeBadgeFrame(0, revealing: true, dotFrom: 0);
        Check(ball.EdgeDot.Opacity == 0 && ball.CountBubble.Opacity == 1, "快速反向时红点已消失则同帧显示数字，不空等100毫秒");
        ball.UpdateBadge(); Check(ball.CountText == "1", "隐藏期间后台数量更新仍准确");
        ball.RestoreFromEdge();
        Check(ball.CountBubble.Opacity == 1 && ball.EdgeDot.Visibility == Visibility.Collapsed, "完整恢复后的数字与红点终态正确");
        await controller.Flush();
        File.WriteAllText(Path.Combine(root, "ux0922-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, frames, scope = "离屏控件、真实保存链及确定性动画帧；无物理鼠标、前台操作或原Codex", mouseMoved = false, originalDesktopLifecycle = false, productionDataChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Layout(FrameworkElement element, double width, double height)
    { element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); }
    private static void Snapshot(FrameworkElement element, double width, double height, string path)
    {
        Layout(element, width, height); var image = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32); image.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); png.Save(stream);
    }
    private static IEnumerable<T> Visual<T>(DependencyObject root) where T : DependencyObject
    { if (root is T found) yield return found; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Visual<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject
    { if (root is T found) yield return found; foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var value in Logical<T>(child)) yield return value; }
}
