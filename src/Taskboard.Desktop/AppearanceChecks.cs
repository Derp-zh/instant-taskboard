using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class AppearanceChecks
{
    internal static void Run(BoardController controller)
    {
        var checks = new List<string>();
        void Assert(bool result, string text) { if (!result) throw new InvalidOperationException(text); checks.Add(text); }
        var engine = controller.Engine; var ball = controller.Ball;
        ball.UpdateBadge(); Assert(ball.CountText == "" && ball.CountBubble.Visibility == Visibility.Collapsed, "零待操作任务不显示空气泡");
        var running = engine.NewSession(Lane.Running); running.Working.Title = "进行中不计数"; engine.Submit(running);
        var draft = engine.NewSession(Lane.Action); draft.Working.Title = "暂存不计数"; engine.StageSession(draft);
        Assert(ball.CountText == "", "其他列表任务和未提交草稿不计入数字");
        var action = engine.NewSession(Lane.Action); action.Working.Title = "待操作计数"; engine.Submit(action);
        Assert(ball.CountText == "1", "正式待操作任务实时计入球内数字");
        engine.Move(action.Working.Id, Lane.Running, null);
        Assert(ball.CountText == "", "移出待操作实时清除数字");
        engine.Undo(); Assert(ball.CountText == "1", "撤销移动实时恢复待操作数字");
        var definition = engine.State.Preferences.Columns.Single(x => x.BuiltinLane == Lane.Action);
        definition.Name = "用户自定义名称"; definition.Visible = false; ball.UpdateBadge();
        Assert(ball.CountText == "1", "列表改名或隐藏不改变待操作身份统计");
        for (var i = 0; i < 1002; i++) engine.State.Tasks.Add(new WorkItem { Lane = Lane.Action, Title = "数字布局隔离样本" });
        ball.UpdateBadge(); Layout(ball.Root, 58, 58);
        var bubble = ball.CountBubble; var center = bubble.TransformToAncestor(ball.Root).Transform(new Point(bubble.ActualWidth / 2, bubble.ActualHeight / 2));
        var distance = (center - new Point(29, 29)).Length;
        Assert(bubble.ActualWidth == bubble.ActualHeight && distance + bubble.ActualWidth / 2 <= 29, "计数圆气泡完全位于悬浮球圆形范围内");
        Assert(ball.CountText == "1003" && ((Viewbox)bubble.Child).StretchDirection == StretchDirection.DownOnly, "多位任务数量完整保留并在气泡内缩放");
        Save(ball.Root, 58, 58, "count-bubble.png");
        var staged = engine.NewSession(Lane.Running); staged.Working.Title = "阶段气泡样本";
        staged.Working.Stages[0].Text = "阶段0详情"; engine.AddStage(staged); staged.Working.Stages[1].Name = "规划"; staged.Working.Stages[1].Text = "阶段详情保持普通文字";
        var item = engine.Submit(staged);
        var card = new TaskCard { Controller = controller, DataContext = item }; Layout(card, 260, 250);
        card.Arrange(new Rect(0, 0, 260, card.DesiredSize.Height)); card.UpdateLayout();
        var pills = Descendants<Border>(card).Where(x => Equals(x.Tag, "TaskStageName")).ToArray();
        Assert(pills.Length == BoardEngine.CardStages(item).Count(), "卡片的每个可见阶段名称都有气泡框");
        Assert(pills.All(x => x.Child is TextBlock text && item.Stages.Any(s => s.Name == text.Text)) && Descendants<TextBlock>(card).Any(x => x.Text == "阶段详情保持普通文字"), "阶段名在气泡内且详情仍独立可读");
        Save(card, 260, (int)Math.Ceiling(card.DesiredSize.Height), "stage-bubbles.png");
        var single = new TaskCard { Controller = controller, DataContext = new WorkItem { Lane = Lane.Running, Title = "仅阶段0" } }; Layout(single, 260, 200);
        Assert(!Descendants<Border>(single).Any(x => Equals(x.Tag, "TaskStageName")), "只有阶段0时保持原来的阶段名隐藏规则");
        engine.State.Tasks.RemoveAll(x => x.Lane == Lane.Action && x.ColumnId is null); ball.UpdateBadge();
        Assert(ball.CountBubble.Visibility == Visibility.Collapsed, "待操作清空后气泡关闭");
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "appearance-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, desktopShown = false, mouseMoved = false }, new JsonSerializerOptions { WriteIndented = true }));
        void Save(FrameworkElement view, int width, int height, string name)
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(controller.Store.DirectoryPath, name)); png.Save(stream);
        }
    }
    private static void Layout(FrameworkElement view, double width, double height)
    { view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject view) where T : DependencyObject
    {
        if (view is T found) yield return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(view); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(view, i))) yield return child;
    }
}
