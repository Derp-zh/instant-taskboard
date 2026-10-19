using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class Ux0914Checks
{
    internal static async Task Run(BoardController controller)
    {
        controller.SuppressCollapse = true;
        var checks = new List<string>(); var root = controller.Store.DirectoryPath;
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); File.WriteAllText(Path.Combine(root, "ux0914-progress.json"), JsonSerializer.Serialize(checks)); }
        controller.SetExpanded(true); await Task.Delay(350);
        foreach (var column in controller.Columns)
        {
            column.Root.UpdateLayout();
            var hit = column.Root.InputHitTest(new Point(column.Width / 2, column.Height - 65)) as DependencyObject;
            Check(column.Root.Background is SolidColorBrush { Color.A: 1 } && hit is not null && column.Root.ContextMenu is not null, column.Title + "透明空白保留完整输入和右键菜单");
        }
        var inspiration = controller.Columns.Single(x => x.Lane == Lane.Inspiration);
        var grip = Children<Thumb>(inspiration.Root).Single(x => Equals(x.Tag, "WorkspaceResizeGrip"));
        foreach (var point in new[] { new Point(2, 2), new Point(20, 2), new Point(2, 20), new Point(20, 20) })
        {
            var local = grip.TransformToAncestor(inspiration.Root).Transform(point);
            Check(Within(inspiration.Root.InputHitTest(local) as DependencyObject, grip), "整组缩放22单位区域角部可命中：" + point);
        }
        var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "保存前不变暗";
        for (var i = 0; i < 4; i++)
        {
            var stage = i == 0 ? session.Working.Stages[0] : controller.Engine.AddStage(session);
            stage.IsRunning = true; stage.Text = string.Join("\n", Enumerable.Repeat("较长阶段正文，用于隔离验证滚动与保存显示", 16));
        }
        var item = controller.Engine.Submit(session); var editor = controller.EditTask(item.Id); var host = editor.Host!;
        await Settled(host); editor.UpdateLayout();
        var save = Children<Button>(editor).Single(x => Equals(x.Tag, "SaveTask"));
        Check(((SolidColorBrush)save.Foreground).Color == (Color)ColorConverter.ConvertFromString("#102033"), "保存任务文字为深蓝黑");
        var outer = Children<ScrollViewer>(editor).First(x => x.Content is StackPanel && x.ScrollableHeight > 0);
        var bar = (ScrollBar)outer.Template.FindName("PART_VerticalScrollBar", outer);
        var thumbBorder = Children<Border>(bar).First(x => x.Background is SolidColorBrush b && b.Color == Color.FromRgb(168, 214, 201));
        Check(Math.Abs(bar.ActualWidth - 8.04) < .1 && thumbBorder.Margin.Left == 1.34 && thumbBorder.Margin.Top == 1, "悬浮滚动轨道和可见滑块横向均为原1.34倍，长度留白不变");
        var copy = editor.CreateSnapshot();
        Check(Math.Abs(QuickScroll.GetVerticalBarWidth(copy) - 8.04) < .1 && QuickScroll.GetThumbMargin(copy).Left == 1.34, "暂存动画快照继承相同滚动条尺寸，不在切换时缩细"); copy.Destroy();
        outer.ScrollToVerticalOffset(0); outer.UpdateLayout();
        outer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        outer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        Check(!QuickScroll.IsAnimating(outer) && outer.VerticalOffset == 0, "首帧前快速反向滚轮抵消待滚动距离，不继续向错误方向滑动");
        outer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        Check(QuickScroll.IsAnimating(outer) && outer.VerticalOffset == 0, "悬浮窗口滚轮启动衔接动画，不瞬移");
        var offsets = new List<double>(); var clock = Stopwatch.StartNew();
        while (QuickScroll.IsAnimating(outer) && clock.ElapsedMilliseconds < 600) { await Task.Delay(6); offsets.Add(outer.VerticalOffset); }
        clock.Stop();
        outer.UpdateLayout();
        Check(offsets.Any(x => x > 0 && x < outer.VerticalOffset - .1) && clock.ElapsedMilliseconds < 250 && outer.VerticalOffset > 0, "90毫秒快速缓出有实际中间位移并完成");
        outer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        outer.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        Check(!QuickScroll.IsAnimating(outer), "直接拖动或点击取消惯性，避免滚动条操作被动画抢回");
        var columnForList = controller.Columns.Single(x => x.Lane == Lane.Running);
        for (var i = 0; i < 35; i++) { var sample = controller.Engine.NewSession(Lane.Running); sample.Working.Title = "滚动卡片" + i; sample.Working.Stages[0].Text = "短正文"; controller.Engine.Submit(sample); }
        await Task.Delay(120); columnForList.Root.UpdateLayout();
        var list = Children<ListBox>(columnForList.Root).Single(); var listScroll = Children<ScrollViewer>(list).First();
        var listBar = (ScrollBar)listScroll.Template.FindName("PART_VerticalScrollBar", listScroll);
        Check(Math.Abs(listBar.ActualWidth - 6) < .1 && QuickScroll.GetThumbMargin(listBar).Left == 1, "列表滚动条及滑块原尺寸保留");
        Check(VirtualizingPanel.GetIsVirtualizing(list) && VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel && listScroll.ScrollableHeight > 0, "列表采用像素衔接但保持虚拟化和实际可滚动范围");
        listScroll.ScrollToTop(); listScroll.UpdateLayout();
        listScroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        var listOffsets = new List<double>(); while (QuickScroll.IsAnimating(listScroll)) { await Task.Delay(6); listOffsets.Add(listScroll.VerticalOffset); }
        listScroll.UpdateLayout();
        Check(listOffsets.Any(x => x > 0 && x < listScroll.VerticalOffset - .1), "列表滚动有连续中间位移，非0秒跳转");
        listScroll.ScrollToTop(); listScroll.UpdateLayout(); QuickScroll.StepItem(listScroll, 1); listScroll.UpdateLayout();
        Check(listScroll.VerticalOffset > 20 && !QuickScroll.IsAnimating(listScroll), "拖动边缘自动滚动仍按一张卡片推进，不误用16像素小步");
        var ball = controller.Ball;
        var target = Children<Border>(ball.Root).Single(x => Equals(x.Tag, "BallClickTarget"));
        foreach (var point in new[] { new Point(ball.Width - 2, 2), new Point(ball.Width - 2, 56), new Point(ball.Width - 15, 5), new Point(ball.Width - 15, 53) })
            Check(Within(ball.HitTestOrb(point) as DependencyObject, target), "吸附形状新矩形内部实际按钮命中：" + point);
        Check(!Within(ball.HitTestOrb(new Point(ball.Width - 57, 1)) as DependencyObject, target), "左上半圆以外不扩大触发到透明角部");
        // Hold only the isolated save gate so the busy visual is observable even
        // on a fast SSD; old Root.IsEnabled=false fails this check immediately.
        var gate = (SemaphoreSlim)typeof(BoardController).GetField("saveGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        var enableChanges = 0; editor.Root.IsEnabledChanged += (_, _) => enableChanges++;
        Children<TextBox>(editor).First().Text = "双击后正式保存";
        await gate.WaitAsync();
        try
        {
            controller.ObserveOutsidePress(host, true); controller.ObserveOutsidePress(host, false, new Point(40, 40), 1000);
            Check(editor.Root.IsEnabled && controller.Editors.Contains(editor), "第一次外点保留编辑内容及正常亮度");
            controller.ObserveOutsidePress(host, false, new Point(40, 40), 1100); await Task.Delay(70);
            Check(editor.Root.IsEnabled && save.IsEnabled && !editor.Root.IsHitTestVisible && enableChanges == 0, "第二次外点等待保存时拦截输入但不全局禁用变暗");
            save.ApplyTemplate(); var saveFrame = (Border)save.Template.FindName("border", save);
            Check(saveFrame.Opacity == 1 && Children<TextBox>(editor).All(x => x.IsEnabled), "保存过程按钮和输入框保持原不透明度");
            Capture(editor, Path.Combine(root, "editor-saving.png"));
        }
        finally { gate.Release(); }
        for (var i = 0; i < 100 && controller.Editors.Contains(editor); i++) await Task.Delay(15);
        Check(!controller.Editors.Contains(editor) && controller.Engine.State.Tasks.Single(x => x.Id == item.Id).Title == "双击后正式保存" && enableChanges == 0, "第二次外点保存成功并关闭，全程没有禁用再启用闪变");
        File.WriteAllText(Path.Combine(root, "ux0914-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, scrollMs = clock.ElapsedMilliseconds, offsets, listOffsets, mouseMoved = false, keyboardInjected = false, productionDataChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static bool Within(DependencyObject? child, DependencyObject parent)
    {
        for (var node = child; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node)) if (ReferenceEquals(node, parent)) return true;
        return false;
    }
    internal static async Task Settled(SurfaceWindow window) { for (var i = 0; i < 200 && (!window.IsVisible || window.IsRevealAnimating || window.IsContentSwapHandoff); i++) await Task.Delay(15); }
    internal static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject => StageFeatureChecks.Children<T>(root);
    internal static void Capture(FrameworkElement view, string path)
    {
        view.UpdateLayout(); var image = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
    }
}

