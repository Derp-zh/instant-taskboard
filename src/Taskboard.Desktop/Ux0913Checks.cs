using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class Ux0913Checks
{
    internal static async Task Run(BoardController controller)
    {
        var checks = new List<string>(); var root = controller.Store.DirectoryPath;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); File.WriteAllText(Path.Combine(root, "ux0913-progress.json"), JsonSerializer.Serialize(checks)); }
        controller.SuppressCollapse = true;
        var old = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new Preferences()))!.AsObject(); old.Remove(nameof(Preferences.BallEdgeAutoHide));
        Check(new Preferences().BallEdgeAutoHide && old.Deserialize<Preferences>()!.BallEdgeAutoHide, "新配置与缺少字段的旧配置默认开启吸附隐藏");
        old[nameof(Preferences.BallEdgeAutoHide)] = false;
        Check(!old.Deserialize<Preferences>()!.BallEdgeAutoHide, "已有明确关闭配置保留关闭");
        foreach (var (text, factor) in new[] { ("未绑定", 1.0), ("M", 1.0), ("MMMMMMMM", 8.0 / 6), ("MMMMMMMMMMMM", 10.0 / 6) })
        {
            var label = Ui.Label(text, 11); Check(Math.Abs(MarqueeText.ConversationWidth(label) - MarqueeText.SixCharacterWidth(label) * factor) < .1, "会话宽度6至10英文字符：" + text);
        }
        var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "短标题"; session.Working.Stages[0].Text = "阶段正文";
        session.Working.Stages[0].Codex = new CodexBinding { ThreadId = "a", ThreadName = "M" };
        var second = controller.Engine.AddStage(session); second.IsRunning = true; second.Text = "第二阶段";
        var item = controller.Engine.Submit(session); controller.SetExpanded(true); await Task.Delay(400);
        var column = controller.Columns.Single(x => x.Lane == Lane.Action); var card = column.Locate(item.Id)!; card.UpdateLayout();
        foreach (var name in Children<MarqueeText>(card).Where(x => Equals(x.Tag, "TaskConversationName")))
        {
            var button = Children<Button>(card).Single(x => Equals(x.Tag, "TaskStageRun") && ReferenceEquals(x.DataContext, name.DataContext));
            var right = name.TransformToAncestor(card).Transform(new Point(name.ActualWidth, 0)).X;
            var left = button.TransformToAncestor(card).Transform(new Point()).X;
            Check(Math.Abs(left - right - 3) < .1 && name.HorizontalAlignment == HorizontalAlignment.Right && name.Label.TextAlignment == TextAlignment.Right, "会话名称紧贴右侧阶段图标，保留3单位间隔且短文本右对齐");
        }
        var entry = Children<Button>(column.Root).Single(x => Equals(x.Tag, "NewTaskEntry")); entry.UpdateLayout();
        var hit = entry.InputHitTest(new Point(entry.ActualWidth - 16, entry.ActualHeight / 2));
        Check(hit is not null && ((SolidColorBrush)entry.Background).Color.A > 0 && entry.Margin == new Thickness(0), "新建入口文字之外整块虚线区域有不透明度及实际命中");
        var editor = controller.EditTask(item.Id); await Settled(editor.Host!); var picker = editor.StagePickers[item.Stages[0].Id];
        picker.Trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); editor.UpdateLayout();
        picker.Select(new CodexThread("bound", "已关联会话名称", 0));
        Check(picker.Pane.Visibility == Visibility.Collapsed && !picker.IsDropdownOpen && picker.Placeholder.Text == "已关联会话名称" && picker.Input.Text == "", "选择会话后收起关联面板，空搜索预显示已关联名称");
        picker.Trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); editor.UpdateLayout();
        picker.DropdownToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(picker.IsDropdownOpen && Equals(picker.DropdownToggle.Content, "⌃") && picker.DropdownToggle.BorderThickness == new Thickness(0), "无气泡箭头展开列表并向上显示");
        picker.DropdownToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!picker.IsDropdownOpen && Equals(picker.DropdownToggle.Content, "⌄") && picker.Pane.Visibility == Visibility.Visible, "箭头可再次收起下拉但保留关联面板");
        picker.DropdownToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); picker.CollapseUnlessContains(picker.Input);
        Check(picker.IsDropdownOpen && picker.Pane.Visibility == Visibility.Visible, "搜索框内操作不收起");
        var body = Children<TextBox>(picker.StageScope!).First(x => x.AcceptsReturn);
        picker.CollapseUnlessContains(body);
        Check(!picker.IsDropdownOpen && picker.Pane.Visibility == Visibility.Visible, "阶段卡片内搜索框外只收起下拉");
        picker.CollapseUnlessContains(editor.StagePickers[second.Id].Trigger);
        Check(picker.Pane.Visibility == Visibility.Collapsed, "另一阶段卡片的点击收起原阶段关联面板");
        picker.Trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); editor.UpdateLayout();
        var hint = Ui.CreateHint(picker.Trigger, BoardEngine.LastRunningStageMessage)!; Ui.OpenHint(hint); await Task.Delay(90);
        Check(hint.IsOpen && PresentationSource.FromVisual(hint) is HwndSource, "固定提示使用真实原生气泡");
        var source = (HwndSource)PresentationSource.FromVisual(hint)!; GetWindowRect(source.Handle, out var first);
        var screen = System.Windows.Forms.Screen.FromHandle(editor.Host!.ContentHandle).WorkingArea;
        Check(Math.Abs((first.Left + first.Right) / 2.0 - (screen.Left + screen.Width / 2.0)) < 2 && Math.Abs(first.Top - (screen.Top + screen.Height * .62)) < 2, "气泡实际原生位置位于屏幕水平中央与62%高度");
        Native.SetWindowPos(source.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x15); await Task.Delay(30); GetWindowRect(source.Handle, out var after);
        Check(first.Left == after.Left && first.Top == after.Top, "原生弹窗重新定位无法改变固定提示位置"); hint.IsOpen = false;
        var before = editor.Host.MotionGeneration; var children = editor.Root.Children.Cast<UIElement>().ToArray();
        controller.ObserveOutsidePress(editor.Host, true); controller.ObserveOutsidePress(editor.Host, false, new Point(50, 50), 1000); await Task.Delay(80);
        Check(controller.Editors.Contains(editor) && editor.Host.MotionGeneration == before && children.SequenceEqual(editor.Root.Children.Cast<UIElement>()) && !editor.Host.LiveContentHidden, "外部第一击不重开窗口、不隐藏控件、不重建编辑内容");
        controller.ObserveOutsidePress(editor.Host, false, new Point(50, 50), 1100);
        for (var i = 0; i < 100 && controller.Editors.Contains(editor); i++) await Task.Delay(20);
        Check(!controller.Editors.Contains(editor) && controller.Engine.State.Tasks.Single(x => x.Id == item.Id).Stages[0].Codex?.ThreadId == "bound", "外部第二击正常保存并关闭窗口");
        controller.SetExpanded(false); await Task.Delay(240); var ball = controller.Ball; ball.Show();
        controller.Engine.State.Preferences.BallEdgeAutoHide = true; ball.RefreshEdgeAutoHide();
        Check(Math.Abs(ball.Left + ball.Width - (-8000)) < .1 && ball.IsEdgeDocked, "开启后悬浮球吸附右侧屏幕边缘");
        var y = ball.Top; ball.MoveDraggedBall(-11000, y + 100);
        Check(Math.Abs(ball.Left + ball.Width - (-8000)) < .1 && Math.Abs(ball.Top - y - 100) < .1, "吸附状态拖动仅改变上下位置");
        var shape = BallSurface.BallGeometry(58, 58, true);
        Check(shape.FillContains(new Point(57, 1)) && !shape.FillContains(new Point(1, 1)) && shape.FillContains(new Point(1, 29)), "右侧形状为左半圆加右矩形");
        Check(ball.ContainsOrbPoint(new Point(57, 1)) && !ball.ContainsOrbPoint(new Point(1, 1)), "右侧矩形触发范围与左半圆形状一致");
        ball.HideAtEdge(new Rect(-12000, -12000, 4000, 4000)); var from = ball.Left; var poses = new List<double>();
        Check(ball.IsEdgeHidden && ball.IsEdgeAnimating && Math.Abs(from - (-8058)) < 1, "收起从完整显示位置启动动画");
        while (ball.IsEdgeAnimating) { await Task.Delay(7); poses.Add(ball.Left); }
        Check(poses.Any(x => x > from + 1 && x < -8015) && Math.Abs(ball.Left - (-8014.5)) < .1 && Math.Abs(ball.EdgeVisibleClip.Width - 14.5) < .1, "收起动画有中间位置且最终显示1/4");
        Check(ball.EdgeDot.Visibility == Visibility.Visible && ball.CountBubble.Visibility == Visibility.Collapsed && ReferenceEquals(ball.EdgeDot.Fill, ball.CountBubble.BorderBrush) && ball.EdgeDot.RenderTransform is TranslateTransform { Y: -10 }, "隐藏红点上移10并使用待操作气泡边框同色");
        ball.RestoreFromEdge(animate: true); var opacities = new List<double>();
        while (ball.IsEdgeRestoring) { await Task.Delay(7); opacities.Add(ball.EdgeDot.Opacity); }
        Check(opacities.Any(x => x > 0 && x < 1) && !ball.IsEdgeHidden && ball.EdgeDot.Visibility == Visibility.Collapsed && Math.Abs(ball.Left - from) < .1, "100毫秒弹出时红点渐隐，最终恢复完整显示");
        ball.ResetEdgeIdle(); ball.AdvanceEdgeIdle(DateTime.UtcNow.AddMilliseconds(1900)); Check(!ball.IsEdgeHidden, "闲置不足2秒保持完整");
        ball.AdvanceEdgeIdle(DateTime.UtcNow.AddMilliseconds(2100)); Check(ball.IsEdgeHidden, "整组收起闲置2秒启动隐藏"); ball.RestoreFromEdge();
        controller.Engine.State.Preferences.BallEdgeAutoHide = false; ball.RefreshEdgeAutoHide(); ball.MoveDraggedBall(-11000, -11000);
        Check(ball.Left == -11000 && ball.Top == -11000 && !ball.IsEdgeDocked && !ball.IsEdgeHidden && !ball.ContainsOrbPoint(new Point(57, 1)), "关闭后恢复圆球、任意拖动与正常计数");
        File.WriteAllText(Path.Combine(root, "ux0913-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, hidePositions = poses, dotOpacities = opacities, mouseMoved = false, keyboardInjected = false, productionDataChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject => StageFeatureChecks.Children<T>(root);
    private static async Task Settled(SurfaceWindow window) { for (var i = 0; i < 200 && (!window.IsVisible || window.IsRevealAnimating || window.IsContentSwapHandoff); i++) await Task.Delay(15); }
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Bounds rect);
}
