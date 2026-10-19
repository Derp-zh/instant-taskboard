using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Taskboard.Core;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

// This mode moves the mouse. Run only after arranging a desktop test with the user.
internal static class Ux099DesktopChecks
{
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);
    internal static async Task Run(BoardController controller)
    {
        var root = controller.Store.DirectoryPath; var checks = new List<string>(); var windows = new List<SurfaceWindow>();
        var foreground = GetForegroundWindow(); GetCursorPos(out var cursor); var moved = false; var down = false;
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); checks.Add(text); }
        async Task Click(FrameworkElement target)
        {
            var owner = Window.GetWindow(target); var top = owner is null ? 0 : new WindowInteropHelper(owner).Handle;
            var active = Native.GetAncestor(GetForegroundWindow(), 2);
            if (top != 0 && active != top && Native.GetAncestor(top, 2) != active) throw new InvalidOperationException("输入目标未在前台，停止鼠标验证");
            var point = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
            moved |= SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(40);
            mouse_event(2, 0, 0, 0, 0); down = true; await Task.Delay(50); mouse_event(4, 0, 0, 0, 0); down = false; await Task.Delay(160);
        }
        try
        {
            var area = controller.WorkArea(controller.Ball);
            foreach (var definition in controller.Engine.State.Preferences.Columns) definition.Visible = definition.BuiltinLane == Lane.Action;
            var column = controller.Columns.Single(x => x.Lane == Lane.Action); column.Left = area.Left + 35; column.Top = area.Top + 30; column.Width = 260; column.Height = Math.Min(650, area.Height - 60); windows.Add(column);
            controller.Ball.Left = area.Left + 30; controller.Ball.Top = area.Bottom - 90; controller.Ball.Show(); windows.Add(controller.Ball);
            controller.ConversationFixture = [new CodexThread("isolated-alpha", "搜索交互示例 Alpha", 0), new CodexThread("isolated-beta", "搜索交互示例 Beta", 0)];
            var draft = controller.Engine.NewSession(Lane.Action); draft.Working.Title = "隔离桌面交互";
            var stage = controller.Engine.AddStage(draft); stage.IsRunning = false; stage.Text = "保存后显示";
            var item = controller.Engine.Submit(draft); controller.SetExpanded(true); await Task.Delay(700);
            var editor = controller.EditTask(item.Id); var host = editor.Host!; windows.Add(host); await Settled(host);
            var currentThread = GetCurrentThreadId(); var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            var attached = foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
            try { SetForegroundWindow(new WindowInteropHelper(host).Handle); host.Activate(); }
            finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
            await Task.Delay(200);
            Check(Native.GetAncestor(GetForegroundWindow(), 2) == new WindowInteropHelper(host).Handle, "真实输入前确认编辑窗口在前台");
            var picker = editor.StagePickers[editor.Session.Working.Stages[0].Id];
            await Click(picker.Trigger); await Click(picker.Input);
            Check(picker.IsDropdownOpen, "真实鼠标点击并松开后，会话下拉仍保持打开");
            SaveFrame(host, Path.Combine(root, "conversation-search.png"));
            picker.Results.UpdateLayout(); var result = (FrameworkElement)picker.Results.ItemContainerGenerator.ContainerFromIndex(0);
            var point = result.PointToScreen(new Point(result.ActualWidth / 2, result.ActualHeight / 2));
            moved |= SetCursorPos((int)point.X, (int)point.Y); mouse_event(2, 0, 0, 0, 0); down = true; await Task.Delay(50); mouse_event(4, 0, 0, 0, 0); down = false; await Task.Delay(180);
            Check(editor.Session.Working.Stages[0].Codex?.ThreadId == "isolated-alpha", "真实点击结果可选择会话，未被窗口外检测吞掉");
            await Click(StageFeatureChecks.Children<TextBox>(editor).First());
            Check(picker.Pane.Visibility == Visibility.Collapsed, "真实点击任务名称自动收起关联区域");
            editor.Session.Working.Stages.Single(x => x.Id == stage.Id).IsRunning = true;
            controller.ObserveOutsidePress(host, true); controller.ObserveOutsidePress(host, false, pressedAt: 1000); controller.ObserveOutsidePress(host, false, pressedAt: 1100);
            for (var i = 0; i < 100 && controller.Editors.Contains(editor); i++) await Task.Delay(20);
            Check(item.Stages.Count(x => x.IsRunning == true) == 2, "外部双击保存后两个运行阶段均显示");
            editor = controller.EditTask(item.Id); host = editor.Host!; windows.Add(host); await Settled(host);
            for (var n = 0; n < 3; n++)
            {
                controller.SetExpanded(false); await Task.Delay(250); controller.SetExpanded(true);
                await Settled(host); SaveFrame(host, Path.Combine(root, "restored-" + n + ".png"));
            }
            Check(!host.LiveContentHidden && host.LastAnimationError is null && column.LastAnimationError is null, "真实桌面连续收起恢复后，画面交接完成且无动画异常");
            var button = StageFeatureChecks.Children<Button>(editor).First(x => Equals(x.Tag, "StageRun"));
            Ui.Hint(button, BoardEngine.LastRunningStageMessage);
            await Task.Delay(150); Check(Ui.ActiveHint?.IsOpen == true, "固定提示气泡可以显示");
            await Task.Delay(1150); Check(Ui.ActiveHint is null, "固定提示气泡按1.2秒自动消失");
        }
        finally
        {
            if (down) mouse_event(4, 0, 0, 0, 0);
            if (moved) SetCursorPos(cursor.X, cursor.Y);
            foreach (var window in windows.Distinct()) if (window.IsVisible) window.Hide();
            SetForegroundWindow(foreground);
            File.WriteAllText(Path.Combine(root, "ux099-desktop-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, mouseMoved = moved, keyboardInjected = false, formalDataTouched = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private static async Task Settled(SurfaceWindow window)
    {
        for (var i = 0; i < 180 && (!window.IsVisible || window.IsRevealAnimating || window.IsContentSwapHandoff); i++) await Task.Delay(20);
        if (!window.IsVisible || window.IsRevealAnimating || window.IsContentSwapHandoff) throw new InvalidOperationException("桌面窗口动画未结束");
    }
    private static void SaveFrame(SurfaceWindow window, string path)
    {
        var bounds = Native.ScreenBounds(window); using var image = new Drawing.Bitmap((int)bounds.Width, (int)bounds.Height);
        using var graphics = Drawing.Graphics.FromImage(image); graphics.CopyFromScreen((int)bounds.X, (int)bounds.Y, 0, 0, image.Size); image.Save(path, Drawing.Imaging.ImageFormat.Png);
    }
}
