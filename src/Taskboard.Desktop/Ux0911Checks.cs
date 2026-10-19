using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class Ux0911Checks
{
    internal static async Task Run(BoardController controller)
    {
        var checks = new List<string>(); var root = controller.Store.DirectoryPath;
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException(label);
            checks.Add(label); File.WriteAllText(Path.Combine(root, "ux0911-progress.json"), JsonSerializer.Serialize(checks));
        }
        controller.SuppressCollapse = true;
        var prefs = controller.Engine.State.Preferences;
        Check(prefs.BallEdgeAutoHide, "吸附自动隐藏默认开启");
        var old = JsonNode.Parse(JsonSerializer.Serialize(prefs))!.AsObject(); old.Remove(nameof(prefs.BallEdgeAutoHide));
        Check(old.Deserialize<Preferences>()!.BallEdgeAutoHide, "旧配置缺少新字段时默认开启");
        var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "单阶段任务";
        session.Working.Stages[0].Text = "单阶段正文保留";
        session.Working.Stages[0].Codex = new CodexBinding { ThreadId = "ux0911-local", ThreadName = "关联文本保留" };
        var item = controller.Engine.Submit(session);
        controller.SetExpanded(true); await Task.Delay(300);
        var column = controller.Columns.Single(x => x.Lane == Lane.Action);
        var card = column.Locate(item.Id)!; card.UpdateLayout();
        Check(!Children<Button>(card).Any(x => Equals(x.Tag, "TaskStageRun")), "单阶段列表卡片不再出现阶段状态图标");
        Check(Children<MarqueeText>(card).Any(x => Equals(x.Tag, "TaskConversationName") && x.Label.Text == "关联文本保留"), "单阶段标题右侧会话名称仍显示");
        Check(Children<TextBlock>(card).Any(x => x.Text == "单阶段正文保留") && !Children<TextBlock>(card).Any(x => x.Text.StartsWith("Codex · ")), "正文保留，冗余Codex状态行移除");
        Render(card, Path.Combine(root, "single-stage.png"));
        foreach (var lane in Enum.GetValues<Lane>())
        {
            var sample = Copies.Of(item); sample.Lane = lane;
            var sampleCard = new TaskCard { Controller = controller, DataContext = sample };
            Check(!Children<Button>(sampleCard).Any(x => Equals(x.Tag, "TaskStageRun")), "单阶段在" + Names.Lanes[(int)lane] + "列表均无状态图标");
        }
        var edit = controller.EditTask(item.Id); await Settled(edit.Host!);
        var close = Children<Button>(edit).Single(x => Equals(x.Tag, "EditorClose"));
        var save = Children<Button>(edit).Single(x => Equals(x.Content, "保存任务"));
        Check(Math.Abs(close.ActualWidth - save.ActualWidth) < .1 && Math.Abs(close.ActualHeight - save.ActualHeight) < .1, "取消保存与保存任务的实际宽高完全一致");
        Check(close.BorderBrush == Brushes.White && Equals(close.Content, "取消保存") && close.Padding == new Thickness(0), "取消保存原颜色、边框、文字和内边距保持");
        Check(Children<Button>(edit).Any(x => Equals(x.Tag, "StageRun")), "单阶段编辑窗口的运行控件仍保留");
        Render(edit, Path.Combine(root, "edit-buttons.png"));
        Children<TextBox>(edit).First().Text = "不应提交的修改";
        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(200);
        Check(item.Title == "单阶段任务" && !controller.Editors.Contains(edit), "取消保存仍放弃编辑且关闭窗口");
        var multi = controller.Engine.Edit(item.Id); var stage = controller.Engine.AddStage(multi); stage.IsRunning = true; stage.Text = "第二阶段";
        item = controller.Engine.Submit(multi); await Task.Delay(150); card = column.Locate(item.Id)!;
        Check(Children<Button>(card).Count(x => Equals(x.Tag, "TaskStageRun")) == 2, "多阶段卡片原状态图标保持");
        var settings = new SettingsSurface(controller); controller.Clamp(settings); settings.Show(); await Settled(settings);
        var option = Children<CheckBox>(settings.Root).Single(x => Equals(x.Tag, "BallEdgeAutoHide"));
        Check(option.IsChecked == true && settings.CurrentSection == "主设置", "主设置提供默认开启的吸附自动隐藏选项");
        option.IsChecked = true; option.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(prefs.BallEdgeAutoHide, "主设置开关启用实际配置");
        await controller.Flush();
        Check(controller.Store.Load().Preferences.BallEdgeAutoHide && ConfigurationBackup.Capture(controller.Engine.State, root).Apply(new BoardState()).Preferences.BallEdgeAutoHide, "自动隐藏配置写盘并随配置备份恢复");
        settings.Destroy();
        var ball = controller.Ball; ball.Show(); controller.SetExpanded(false); await Task.Delay(300);
        ball.Left = -11900; ball.ResetEdgeIdle(); var clock = Stopwatch.StartNew();
        await Task.Delay(1600);
        Check(!ball.IsEdgeHidden, "真实计时未满2秒时不隐藏");
        for (var i = 0; i < 20 && !ball.IsEdgeHidden; i++) await Task.Delay(50);
        clock.Stop();
        await Task.Delay(150);
        Check(ball.IsEdgeHidden && clock.ElapsedMilliseconds >= 2000 && clock.ElapsedMilliseconds < 2800, "真实DispatcherTimer在离开2秒后自动隐藏");
        Check(ball.EdgeDot.Visibility == Visibility.Visible && ball.CountBubble.Visibility == Visibility.Collapsed, "隐藏且有待操作时只显示小红点");
        File.WriteAllText(Path.Combine(root, "edge-geometry.json"), JsonSerializer.Serialize(new { ball.Left, ball.Top, ball.Width, ball.Height, ball.EdgeHiddenOnLeft, native = Native.ScreenBounds(ball), dpi = Native.GetDpiForWindow(new WindowInteropHelper(ball).Handle) }));
        Check(Math.Abs(ball.Left - (-8000 - 58 * .25)) <= 1, "固定右边缘并露出四分之一宽度");
        Check(Math.Abs(ClippedWidth(ball) - 14.5 * Native.GetDpiForWindow(new WindowInteropHelper(ball).Handle) / 96.0) <= 1, "原生窗口确实只保留四分之一可见区域，避免跨屏泄露");
        Render(ball.Root, Path.Combine(root, "edge-left.png"));
        controller.SaveBall(ball); Check(prefs.Positions["ball"].X == -8058, "保存位置使用完整球位置，绝不写入藏到边缘外的坐标");
        ball.Root.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
        await Task.Delay(160);
        Check(!ball.IsEdgeHidden && ball.Left == -8058 && ball.CountBubble.Visibility == Visibility.Visible && ball.EdgeDot.Visibility == Visibility.Collapsed, "移入露出部分恢复完整球及原数量显示");
        Check((ClippedWidth(ball) == 0 || ClippedWidth(ball) >= 58 * Native.GetDpiForWindow(new WindowInteropHelper(ball).Handle) / 96.0 - 1) && !controller.Expanded, "移入恢复完整窗口裁形但不自动展开面板");
        ball.Left = -8058; ball.HideAtEdge(new Rect(-12000, -12000, 4000, 4000), animate: false);
        Check(ball.IsEdgeHidden && Math.Abs(ball.Left - (-8000 - 58 * .25)) <= 1 && ball.EdgeDot.HorizontalAlignment == HorizontalAlignment.Left, "右侧隐藏露出左四分之一，红点位于露出部分");
        Render(ball.Root, Path.Combine(root, "edge-right.png"));
        item.Lane = Lane.Running; item.ColumnId = null; controller.Engine.Notify([item.Id]);
        Check(ball.IsEdgeHidden && ball.EdgeDot.Visibility == Visibility.Collapsed, "后台任务离开待操作时不恢复球，红点自动消失");
        item.Reminder.Enabled = true; item.Reminder.NextLocal = DateTime.Now.AddSeconds(-1);
        await (Task)typeof(BoardController).GetMethod("CheckReminders", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null)!;
        Check(item.Lane == Lane.Action && item.TimerUnread && ball.IsEdgeHidden && ball.EdgeDot.Visibility == Visibility.Visible, "隐藏时真实后台提醒路径仍转入待操作并更新红点");
        controller.Toggle(); await Settled(ball);
        Check(controller.Expanded && !ball.IsEdgeHidden && ball.Width == 370, "单击原切换入口正常展开面板和搜索胶囊");
        ball.AdvanceEdgeIdle(DateTime.UtcNow.AddSeconds(5)); Check(!ball.IsEdgeHidden, "面板展开时即使超过4秒也不隐藏");
        controller.SetExpanded(false); await Task.Delay(220); ball.HideAtEdge(new Rect(-12000, -12000, 4000, 4000));
        prefs.BallEdgeAutoHide = false; controller.ApplySettings();
        Check(!ball.IsEdgeHidden && ball.EdgeDot.Visibility == Visibility.Collapsed && ball.CountBubble.Visibility == Visibility.Visible, "关闭自动隐藏立即恢复完整球和旧显示");
        ball.AdvanceEdgeIdle(DateTime.UtcNow.AddSeconds(10)); Check(!ball.IsEdgeHidden, "关闭后不会继续隐藏");
        prefs.BallEdgeAutoHide = true; ball.RefreshEdgeAutoHide(); ball.SetEdgeMenuOpen(true);
        ball.AdvanceEdgeIdle(DateTime.UtcNow.AddSeconds(10)); Check(!ball.IsEdgeHidden, "右键菜单使用期间不隐藏");
        ball.SetEdgeMenuOpen(false); ball.HideAtEdge(new Rect(-12000, -12000, 4000, 4000));
        ball.Hide(); Check(!ball.IsEdgeHidden, "手动隐藏球时清理自动隐藏状态，托盘恢复仍可用");
        ball.Show(); ball.HideAtEdge(new Rect(-12000, -12000, 4000, 4000)); controller.RestoreDefaults();
        Check(!ball.IsEdgeHidden && ball.Left == -8058, "恢复默认布局清理藏球状态，球位置可找回");
        await controller.Flush();
        File.WriteAllText(Path.Combine(root, "ux0911-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks,
            autoHideDelayMs = clock.ElapsedMilliseconds, cancelWidth = close.ActualWidth, saveWidth = save.ActualWidth,
            mouseMoved = false, keyboardInjected = false, productionDataChanged = false,
            boundary = "窗口均在屏幕外；移入使用路由事件验证，未声称真实鼠标交互或多屏实机验收。" }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject => StageFeatureChecks.Children<T>(root);
    private static async Task Settled(SurfaceWindow window)
    {
        for (var i = 0; i < 180 && (!window.IsVisible || window.IsRevealAnimating || window.IsContentSwapHandoff); i++) await Task.Delay(20);
        if (!window.IsVisible || window.IsRevealAnimating || window.IsContentSwapHandoff) throw new InvalidOperationException("隔离窗口未就绪");
    }
    private static void Render(FrameworkElement element, string path)
    {
        element.UpdateLayout(); var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
    [StructLayout(LayoutKind.Sequential)] private struct RegionRect { public int Left, Top, Right, Bottom; }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(IntPtr region, out RegionRect rect);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    private static int ClippedWidth(Window window)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try { if (GetWindowRgn(new WindowInteropHelper(window).Handle, region) == 0) return 0; GetRgnBox(region, out var rect); return rect.Right - rect.Left; }
        finally { DeleteObject(region); }
    }
}

