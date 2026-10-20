using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class Ux0915Checks
{
    internal static async Task Run(BoardController controller)
    {
        controller.SuppressCollapse = true;
        var checks = new List<string>(); var root = controller.Store.DirectoryPath;
        void Check(bool value, string text)
        { if (!value) throw new InvalidOperationException(text); checks.Add(text); File.WriteAllText(Path.Combine(root, "ux0915-progress.json"), JsonSerializer.Serialize(checks)); }
        controller.SetExpanded(true); await Task.Delay(350);
        var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "卡片布局测试";
        var stage = session.Working.Stages[0]; stage.Name = "很长的阶段名称用于测试右边剩余宽度自动分配";
        stage.Text = "正文保留"; stage.Codex = new CodexBinding { ThreadId = "test", ThreadName = "MMMMMMMMMMMM" };
        var second = controller.Engine.AddStage(session); second.IsRunning = true; second.Name = "短名";
        var item = controller.Engine.Submit(session);
        var column = controller.Columns.Single(x => x.Lane == Lane.Action);
        foreach (var width in new[] { 300.0, 430.0, 350.0 })
        {
            column.Width = width; column.Root.UpdateLayout();
            var card = column.Locate(item.Id)!; card.UpdateLayout();
            var conversation = Children<MarqueeText>(card).Single(x => Equals(x.Tag, "TaskConversationName") && x.DataContext is Stage s && s.Id == stage.Id);
            var pill = Children<Border>(card).Single(x => Equals(x.Tag, "TaskStageName") && x.Child is MarqueeText text && text.Label.Text == stage.Name);
            var shortPill = Children<Border>(card).Single(x => Equals(x.Tag, "TaskStageName") && x.Child is MarqueeText text && text.Label.Text == "短名");
            var heading = (CardHeading)VisualTreeHelper.GetParent(pill);
            var state = Children<Button>(heading).Single();
            var conversationX = conversation.TranslatePoint(new Point(), heading).X;
            var stateX = state.TranslatePoint(new Point(), heading).X;
            Check(Math.Abs(conversation.ActualWidth - MarqueeText.ConversationWidth(conversation.Label)) < .2 && conversation.Overflow > 0, width + "宽下会话实际字体限制5个中文字宽且溢出");
            Check(Math.Abs(stateX - conversationX - conversation.ActualWidth - state.Margin.Left) < .2, width + "宽下会话位于右侧状态图标紧邻左侧");
            Check(Math.Abs(pill.ActualWidth + pill.Margin.Right - conversationX + conversation.Margin.Left) < .5 && ((MarqueeText)pill.Child).Overflow > 0, width + "宽下阶段气泡占满右边控件以外的剩余空间并滚动");
            Check(shortPill.ActualWidth < 90 && ((MarqueeText)shortPill.Child).Overflow < .1, width + "宽下短阶段保持自然气泡不拉伸");
            Check(!Children<TextBlock>(card).Any(x => x.Text.StartsWith("Codex · ")) && Children<TextBlock>(card).Any(x => x.Text == "正文保留"), width + "宽下只删除Codex状态行，正文保持");
        }
        var activeCard = column.Locate(item.Id)!; var builds = activeCard.BuildCount;
        stage.Codex!.Phase = CodexPhase.Running; controller.Engine.Notify([item.Id]);
        Check(activeCard.BuildCount == builds, "不再显示的关联进程状态变化不会重建卡片或打断滚动");
        var longText = Children<MarqueeText>(activeCard).Single(x => Equals(x.Tag, "TaskConversationName") && x.DataContext is Stage s && s.Id == stage.Id);
        longText.Restart(); var clock = Stopwatch.StartNew();
        await Task.Delay(250); Check(Math.Abs(longText.Offset) < .1, "循环开始停顿0.5秒期间不移动");
        var end = .5 + longText.Overflow / MarqueeText.PixelsPerSecond;
        while (clock.Elapsed.TotalSeconds < .8) await Task.Delay(8);
        Check(longText.Offset < -1 && longText.Offset > -longText.Overflow + 1, "停顿后以24单位每秒缓慢单向滚动");
        while (clock.Elapsed.TotalSeconds < end + .10) await Task.Delay(8);
        var firstEnd = longText.Offset; await Task.Delay(90);
        Check(Math.Abs(firstEnd + longText.Overflow) < .7 && Math.Abs(longText.Offset - firstEnd) < .7, "滚到末尾后保持0.3秒，无反向播放");
        while (clock.Elapsed.TotalSeconds < end + .4) await Task.Delay(8);
        Check(Math.Abs(longText.Offset) < .1 && MarqueeText.EndPauseSeconds == .3, "末停结束回到开头，重新进入0.5秒停顿");
        foreach (var value in new[] { "短", "MMMMMMMM", "MMMMMMMMMMMM" })
        {
            stage.Codex.ThreadName = value; controller.Engine.Notify([item.Id]); activeCard = column.Locate(item.Id)!; activeCard.UpdateLayout();
            var name = Children<MarqueeText>(activeCard).Single(x => Equals(x.Tag, "TaskConversationName") && x.DataContext is Stage s && s.Id == stage.Id);
            Check(Math.Abs(name.ActualWidth - MarqueeText.ConversationWidth(name.Label)) < .2 && name.ActualWidth >= MarqueeText.ConversationMinimumWidth(name.Label) - .2, "会话短/中/长名称实际宽度均在3至5个中文字宽内：" + value);
        }
        var draft = controller.NewTask(Lane.Action); var host = draft.Host!;
        await Ux0914Checks.Settled(host);
        foreach (var editor in new[] { draft, controller.EditTask(item.Id) })
        {
            await Ux0914Checks.Settled(editor.Host!); editor.UpdateLayout();
            var save = Children<Button>(editor).Single(x => Equals(x.Tag, "SaveTask"));
            Check(save.Foreground is SolidColorBrush brush && brush.Color == Color.FromRgb(16, 32, 51) && save.IsEnabled, "新建/编辑保存文字深蓝黑，按钮仍可操作");
        }
        var frameCount = 0; var hiddenFrames = 0;
        EventHandler onFrame = (_, _) => { if (host.IsScrollAnimating) { frameCount++; if (host.LiveContentHidden || host.Root.Opacity != 1) hiddenFrames++; } };
        CompositionTarget.Rendering += onFrame;
        try
        {
            for (var i = 0; i < 8; i++)
            {
                host.Hide(); await Task.Delay(160); host.Show(); await Ux0914Checks.Settled(host);
                Check(host.IsVisible && !host.LiveContentHidden && !host.IsContentSwapHandoff && host.Root.Opacity == 1 && host.RestingHandoffCount == 0, "反复新建开合第" + (i + 1) + "轮全程真实控件，结束无截图交接");
            }
        }
        finally { CompositionTarget.Rendering -= onFrame; }
        Check(frameCount > 8 && hiddenFrames == 0 && host.LiveScrollRevealCount >= 16 && !host.UsesIndependentAnimation, "实际动画帧中真实内容持续可见，使用已确认的UI裁切路径");
        controller.RemoveEditor(draft); draft.Destroy();
        Check(host.IsScrollAnimating && Children<TextBox>(host.Root).Any(), "关闭最后一张草稿时保留真实控件，收起开始不提前清空");
        await Task.Delay(250);
        Check(!host.IsVisible && !Children<TextBox>(host.Root).Any(), "收起完成后再释放最后草稿控件");
        var reopen = controller.NewTask(Lane.Action); await Ux0914Checks.Settled(reopen.Host!);
        controller.RemoveEditor(reopen); reopen.Destroy();
        var replacement = controller.NewTask(Lane.Action); await Task.Delay(300);
        Check(replacement.Host!.IsVisible && Children<TextBox>(replacement.Host.Root).Any(), "关闭最后草稿后立即新建，旧清理不会移除新内容");
        controller.SetExpanded(false); await Task.Delay(250);
        var ball = controller.Ball; var widths = new List<double>(); var rights = new List<double>(); var textXs = new List<double>(); var hiddenBallFrames = 0;
        EventHandler ballFrame = (_, _) => { if (ball.IsRevealAnimating) {
            widths.Add(ball.Width); rights.Add(Native.ScreenBounds(ball).Right);
            var input = Children<TextBox>(ball.Root).First();
            if (ball.Width > 64 && input.ActualWidth > 0) textXs.Add(input.PointToScreen(new Point(0, 0)).X);
            if (ball.LiveContentHidden || ball.Root.Opacity != 1) hiddenBallFrames++; } };
        CompositionTarget.Rendering += ballFrame;
        try { controller.SetExpanded(true); await Task.Delay(250); controller.SetExpanded(false); await Task.Delay(250); }
        finally { CompositionTarget.Rendering -= ballFrame; }
        Check(widths.Any(x => x > 59 && x < 369) && ball.LiveBallRevealCount >= 2 && ball.Width == 58, "球向左展开及收起使用实际宽度中间帧，结束仍为58单位");
        Check(hiddenBallFrames == 0 && ball.RestingHandoffCount == 0 && !ball.UsesIndependentAnimation, "球全程真实控件，无截图交接或隐藏内容");
        File.WriteAllText(Path.Combine(root, "ball-bounds.json"), JsonSerializer.Serialize(new { widths, rights, textXs, bounds = Native.ScreenBounds(ball).ToString(), ball.LiveContentBounds }));
        Check(rights.Max() - rights.Min() < 1 && Children<TextBox>(ball.Root).Any(), "球右侧锚点及原搜索输入控件保持，宽度动画不跳换宿主");
        Check(textXs.Count >= 3 && textXs.Max() - textXs.Min() < 1, "球双向展开中搜索文字保持固定屏幕位置、仅裁切显露");
        var transient = new SurfaceWindow(80, 80) { Left = -15000, Top = -15000 }; var destroyed = new TaskCompletionSource<bool>();
        transient.LiveScrollStarted += () =>
        {
            var staleFrame = (EventHandler?)typeof(SurfaceWindow).GetField("liveScrollFrame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(transient);
            transient.Destroy(false); staleFrame?.Invoke(null, EventArgs.Empty); destroyed.TrySetResult(staleFrame is not null);
        };
        transient.Show(); await Task.WhenAny(destroyed.Task, Task.Delay(1500));
        File.WriteAllText(Path.Combine(root, "destroyed-frame.json"), JsonSerializer.Serialize(new { status = destroyed.Task.Status.ToString(), result = destroyed.Task.IsCompletedSuccessfully ? destroyed.Task.Result : (bool?)null,
            transient.IsVisible, validHandle = Native.IsWindow(transient.ContentHandle), handle = transient.ContentHandle.ToInt64(), transient.LiveScrollRevealCount, transient.IsScrollAnimating }));
        Check(destroyed.Task.IsCompletedSuccessfully && destroyed.Task.Result && !transient.IsVisible && !Native.IsWindow(transient.ContentHandle), "离屏首帧有兜底，销毁后模拟已排队旧帧回调不再访问失效句柄");
        File.WriteAllText(Path.Combine(root, "ux0915-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, frameCount, hiddenFrames, mouseMoved = false, productionDataChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<T> Children<T>(DependencyObject value) where T : DependencyObject => StageFeatureChecks.Children<T>(value);
}
