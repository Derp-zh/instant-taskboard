using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class Ux0912Checks
{
    internal static async Task Run(BoardController controller)
    {
        var checks = new List<string>(); var root = controller.Store.DirectoryPath;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); File.WriteAllText(Path.Combine(root, "ux0912-progress.json"), JsonSerializer.Serialize(checks)); }
        controller.SuppressCollapse = true;
        var menu = controller.EntryMenu;
        Check(menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Select(x => x.Text).SequenceEqual(new[] { "隐藏悬浮球", "设置", "恢复窗口默认", "撤销", "恢复", "AI", "展开 / 收起看板", "退出程序" }), "悬浮球与托盘共用同一菜单，完整八项及顺序一致");
        Check(menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(x => x.Text == "AI").DropDownItems.Count > 0, "共用菜单保留AI子菜单");
        foreach (var (value, factor) in new[] { ("未绑定", 1.0), ("中中中", 1.0), ("中中中中", 4.0 / 3), ("中中中中中中", 5.0 / 3) })
        {
            var label = Ui.Label(value, 11, Ui.Muted);
            Check(Math.Abs(MarqueeText.ConversationWidth(label) - MarqueeText.ConversationMinimumWidth(label) * factor) < .1, "会话宽度按最小3最大5个中文字宽限制：" + value);
        }
        Check(StageFeatureChecks.Children<TextBlock>(StageRunIcon.Caption(false)).Any(x => x.Text == "未运行") && !StageFeatureChecks.Children<TextBlock>(StageRunIcon.Caption(false)).Any(x => x.Text == "暂无进程"), "暂停阶段显示未运行，运行中名称保持");
        var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "重复点击与刷新检查"; session.Working.Stages[0].Text = "正文";
        var second = controller.Engine.AddStage(session); second.IsRunning = true;
        var item = controller.Engine.Submit(session);
        controller.SetExpanded(true); await Task.Delay(350);
        var column = controller.Columns.Single(x => x.Lane == Lane.Action); var card = column.Locate(item.Id)!; card.UpdateLayout();
        Check(StageFeatureChecks.Children<MarqueeText>(card).Count(x => Equals(x.Tag, "TaskConversationName") && x.Label.Text == "未绑定") == 2, "未关联会话的阶段分别显示未绑定");
        var builds = card.BuildCount; var child = card.Child;
        for (var i = 0; i < 5; i++) { item.UpdatedUtc = DateTime.UtcNow; controller.Engine.Notify([item.Id]); }
        Check(card.BuildCount == builds && ReferenceEquals(child, card.Child), "仅后台元数据变动及重复通知不重建卡片，不重置文本");
        item.Title = "真正变化的新标题"; controller.Engine.Notify([item.Id]);
        Check(card.BuildCount == builds + 1 && !ReferenceEquals(child, card.Child), "真实可见内容变化仍刷新卡片");
        controller.SetExpanded(false); await Task.Delay(220); controller.SetExpanded(true); await Task.Delay(300);
        Check(controller.Columns.All(x => x.PersistentBackdropMotionCount > 0 && x.LastAnimationError is null), "六列开合均使用常驻背景，动画无回退错误");
        Check(controller.Columns.All(x => x.Root.Background is SolidColorBrush { Color.A: 1 }), "列表暗色层与毛玻璃常驻，透明子表面保留alpha1全区域命中");
        controller.ToggleTaskEditor(item.Id); var editor = controller.Editors.Single(x => x.Session.TaskId == item.Id); var host = editor.Host!;
        var early = host.TaskOpeningElapsedMs;
        Check(host.IsScrollAnimating && !host.CanInterruptTaskOpening && !ScrollMotion.CanInterruptOpening(39.9) && ScrollMotion.CanInterruptOpening(40), "准备或首帧前忽略第二次操作，2/5边界仍为40毫秒");
        controller.ToggleTaskEditor(item.Id); Check(controller.Editors.Contains(editor), "过早第二次操作不会关闭编辑窗口");
        for (var i = 0; i < 300 && host.TaskOpeningElapsedMs < 45; i++) await Task.Delay(1);
        var allowed = host.TaskOpeningElapsedMs;
        Check(allowed >= 40 && host.CanInterruptTaskOpening, "实际动画达到40%门槛后允许第二次关闭；繁忙首帧可延迟");
        controller.ToggleTaskEditor(item.Id);
        Check(!controller.Editors.Contains(editor) && host.IsScrollAnimating, "动画未结束时第二次操作已反向关闭，并保留副本");
        await Task.Delay(250);
        var ball = controller.Ball; controller.SetExpanded(false); await Task.Delay(220); ball.Show();
        controller.Engine.State.Preferences.BallEdgeAutoHide = true; ball.RefreshEdgeAutoHide(); ball.Left = -11900;
        var clock = Stopwatch.StartNew(); ball.ResetEdgeIdle(); await Task.Delay(1650); Check(!ball.IsEdgeHidden, "鼠标离开未满2秒不隐藏");
        for (var i = 0; i < 30 && !ball.IsEdgeHidden; i++) await Task.Delay(30);
        clock.Stop(); await Task.Delay(150); Check(ball.IsEdgeHidden && clock.ElapsedMilliseconds is >= 2000 and < 2800, "真实闲置2秒后隐藏");
        Check(ball.EdgeDot.RenderTransform is TranslateTransform { Y: -10 } && ((SolidColorBrush)ball.EdgeDot.Fill).Color == ((SolidColorBrush)ball.CountBubble.BorderBrush).Color, "隐藏小红点上移10单位且与计数边框同色");
        var from = ball.Left; var positions = new List<double>();
        ball.Root.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
        Check(ball.IsEdgeRestoring && ball.IsEdgeHidden && Math.Abs(ball.Left - from) < 1, "移入后启动弹出动画而非瞬移");
        var reveal = Stopwatch.StartNew();
        for (var i = 0; i < 40 && ball.IsEdgeRestoring; i++) { await Task.Delay(5); positions.Add(ball.Left); }
        reveal.Stop();
        Check(!ball.IsEdgeRestoring && !ball.IsEdgeHidden && Math.Abs(ball.Left - (-8058)) < 1, "弹出动画结束在屏幕边缘内完整球位置");
        Check(positions.Any(x => x < from - 1 && x > -8057) && reveal.ElapsedMilliseconds is >= 90 and < 260, "0.1秒弹出有真实中间位置，无超调到桌面外");
        Check(!controller.Expanded, "鼠标移入只恢复球，仍需单击打开面板");
        File.WriteAllText(Path.Combine(root, "ux0912-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, earlyOpeningMs = early, acceptedOpeningMs = allowed,
            hideDelayMs = clock.ElapsedMilliseconds, edgeRevealMs = reveal.ElapsedMilliseconds, edgePositions = positions,
            mouseMoved = false, keyboardInjected = false, productionDataChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
