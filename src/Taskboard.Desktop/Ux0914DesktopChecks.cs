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
using System.Windows.Interop;
using System.Windows.Media;
using Taskboard.Core;
using Drawing = System.Drawing;

namespace Taskboard.Desktop;

// This mode moves the mouse. Requires explicit coordination and isolated data.
internal static class Ux0914DesktopChecks
{
    [StructLayout(LayoutKind.Sequential)] private struct PixelPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PixelPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PixelPoint point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);
    internal static async Task Run(BoardController controller)
    {
        if (!controller.Isolated || controller.Smoke) throw new InvalidOperationException("真实鼠标检查只允许可见隔离窗口");
        controller.SuppressCollapse = true;
        var root = controller.Store.DirectoryPath; var checks = new List<string>(); var metrics = new List<object>();
        var oldForeground = Native.GetForegroundWindow(); GetCursorPos(out var oldCursor); var moved = false;
        SurfaceWindow? backing = null;
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); File.WriteAllText(Path.Combine(root, "ux0914-desktop-progress.json"), JsonSerializer.Serialize(checks)); }
        void OwnPoint(Point point)
        {
            var hit = WindowFromPoint(new PixelPoint { X = (int)point.X, Y = (int)point.Y });
            GetWindowThreadProcessId(hit, out var process);
            if (process != Environment.ProcessId) throw new InvalidOperationException("目标不属于隔离进程，停止输入：" + point);
        }
        async Task Click(Point point, bool right = false)
        {
            OwnPoint(point); moved = true;
            await Task.Run(() => { SetCursorPos((int)point.X, (int)point.Y); Thread.Sleep(30); mouse_event(right ? 8u : 2u, 0, 0, 0, 0); Thread.Sleep(25); mouse_event(right ? 16u : 4u, 0, 0, 0, 0); });
            await Task.Delay(110);
        }
        async Task Drag(Point start, Vector delta, bool right = false)
        {
            OwnPoint(start); moved = true;
            await Task.Run(() =>
            {
                SetCursorPos((int)start.X, (int)start.Y); Thread.Sleep(35); mouse_event(right ? 8u : 2u, 0, 0, 0, 0); Thread.Sleep(right ? 340 : 70);
                for (var i = 1; i <= 8; i++) { SetCursorPos((int)(start.X + delta.X * i / 8), (int)(start.Y + delta.Y * i / 8)); Thread.Sleep(20); }
                mouse_event(right ? 16u : 4u, 0, 0, 0, 0);
            });
            await Task.Delay(160);
        }
        try
        {
            var area = controller.WorkArea(controller.Ball); var width = Math.Min(180, (area.Width - 70) / 6 - 10);
            controller.Engine.State.Preferences.ResizeCorner = true;
            foreach (var column in controller.Columns) column.ApplySettings();
            backing = new SurfaceWindow(area.Width - 20, Math.Min(760, area.Height - 20)) { GlassEnabled = false, FloatingAnimations = false, WindowLayer = -1, Left = area.Left + 10, Top = area.Top + 10, SuppressActivation = true };
            backing.Root.Background = Brushes.DimGray; backing.Show();
            controller.SetExpanded(true); await Task.Delay(500);
            for (var i = 0; i < controller.Columns.Count; i++) { var column = controller.Columns[i]; column.Left = area.Left + 25 + i * (width + 10); column.Top = area.Top + 30; column.Width = width; column.Height = Math.Min(470, area.Height - 100); }
            await Task.Delay(120);
            var first = controller.Columns[0];
            var thread = GetCurrentThreadId(); var foregroundThread = GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
            var attached = thread != foregroundThread && AttachThreadInput(thread, foregroundThread, true);
            try { SetForegroundWindow(new WindowInteropHelper(first).Handle); first.Activate(); }
            finally { if (attached) AttachThreadInput(thread, foregroundThread, false); }
            Check(Native.GetAncestor(Native.GetForegroundWindow(), 2) == new WindowInteropHelper(first).Handle, "真实输入前确认隔离窗口在前台");
            foreach (var column in controller.Columns)
            {
                var point = column.Root.PointToScreen(new Point(column.Width / 2, column.Height - 60));
                Check(WindowFromPoint(new PixelPoint { X = (int)point.X, Y = (int)point.Y }) == column.ContentHandle, column.Title + "空白原生命中完整子窗口");
                await Click(point, right: true); Check(column.Root.ContextMenu!.IsOpen, column.Title + "空白右键实际弹出菜单"); column.Root.ContextMenu.IsOpen = false; await Task.Delay(50);
            }
            await Task.Delay(180); // Let the native context-menu dismissal finish before a new drag.
            var beforeLeft = first.Left; var beforeTop = first.Top;
            await Drag(first.Root.PointToScreen(new Point(first.Width - 50, 20)), new Vector(12, 12));
            Check(first.Left > beforeLeft + 3 && first.Top > beforeTop + 3, "标题空白仍可实际拖动列表");
            var grip = Ux0914Checks.Children<Thumb>(first.Root).Single(x => Equals(x.Tag, "WorkspaceResizeGrip"));
            var range = controller.MaximumRange;
            await Drag(grip.PointToScreen(new Point(19, 19)), new Vector(-24, 24));
            Check(Math.Abs(controller.MaximumRange.Width - range.Width) > 2, "整组缩放入口文字以外角部实际可拖动并改变最大范围");
            var corner = Ux0914Checks.Children<Thumb>(first.Root).Single(x => x.Cursor == System.Windows.Input.Cursors.SizeNWSE && x.Width == 18);
            var oldWidth = first.Width; var oldHeight = first.Height;
            await Drag(corner.PointToScreen(new Point(9, 9)), new Vector(18, 15));
            Check(first.Width > oldWidth + 2 && first.Height > oldHeight + 2, "单列右下缩放原操作仍可实际拖动");
            var entry = Ux0914Checks.Children<Button>(first.Root).Single(x => Equals(x.Tag, "NewTaskEntry"));
            await Click(entry.PointToScreen(new Point(15, entry.ActualHeight / 2)));
            Check(first.QuickEntryActive, "灵感库新建入口文字外实际点击可进入快捷输入");
            // Commit the empty quick entry before testing other popups.
            await Click(first.Root.PointToScreen(new Point(first.Width / 2, 20)));
            var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "鼠标双击关闭闪烁"; session.Working.Stages[0].Text = "保存期间按钮和正文应维持原亮度";
            var item = controller.Engine.Submit(session); var edit = controller.EditTask(item.Id); var host = edit.Host!;
            host.Left = area.Left + Math.Min(560, area.Width - host.Width - 30); host.Top = area.Top + 35;
            await Ux0914Checks.Settled(host); await Task.Delay(200);
            var title = Ux0914Checks.Children<TextBox>(edit).First(); title.Text = "真实外部双击已保存";
            var save = Ux0914Checks.Children<Button>(edit).Single(x => Equals(x.Tag, "SaveTask"));
            var saveFrame = (Border)save.Template.FindName("border", save);
            var enableChanges = 0; edit.Root.IsEnabledChanged += (_, _) => enableChanges++;
            var gate = (SemaphoreSlim)typeof(BoardController).GetField("saveGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            await gate.WaitAsync();
            try
            {
                var outside = first.Root.PointToScreen(new Point(first.Width / 2, first.Height - 65));
                var beforeMotion = host.MotionGeneration;
                await Click(outside); Check(controller.Editors.Contains(edit) && host.MotionGeneration == beforeMotion, "真实第一次外点保留编辑且不重播动画");
                await Click(outside); await Task.Delay(70);
                Check(!edit.Root.IsHitTestVisible && edit.Root.IsEnabled && saveFrame.Opacity == 1 && enableChanges == 0, "真实第二次外点进入保存，全窗口无禁用变暗");
                SaveFrame(host, Path.Combine(root, "actual-double-click-saving.png"));
                var sample = save.PointToScreen(new Point(8, save.ActualHeight / 2));
                using var image = new Drawing.Bitmap(4, 4); using (var g = Drawing.Graphics.FromImage(image)) g.CopyFromScreen((int)sample.X, (int)sample.Y, 0, 0, image.Size);
                var color = image.GetPixel(1, 1); metrics.Add(new { savingPixel = new[] { color.R, color.G, color.B } });
            }
            finally { gate.Release(); }
            for (var i = 0; i < 100 && controller.Editors.Contains(edit); i++) await Task.Delay(15);
            Check(!controller.Editors.Contains(edit) && controller.Engine.State.Tasks.Single(x => x.Id == item.Id).Title == "真实外部双击已保存" && enableChanges == 0, "真实双击保存后关闭，不经过整体变暗再恢复");
            await Task.Delay(220);
            var col = controller.Columns.Single(x => x.Lane == Lane.Action); var card = col.Locate(item.Id)!;
            await Click(card.PointToScreen(new Point(30, 22))); Check(controller.Editors.Any(x => x.Session.TaskId == item.Id), "列表卡片实际点击仍打开任务编辑");
            edit = controller.Editors.Single(x => x.Session.TaskId == item.Id); await Ux0914Checks.Settled(edit.Host!); edit.Suspend(); await Task.Delay(200);
            controller.SetExpanded(false); await Task.Delay(300);
            var ball = controller.Ball; ball.Top = area.Top + 120; ball.RefreshEdgeAutoHide(); ball.Show(); await Ux0914Checks.Settled(ball); await Task.Delay(100);
            var clickTarget = Ux0914Checks.Children<Border>(ball.Root).Single(x => Equals(x.Tag, "BallClickTarget"));
            var cornerPoint = ball.OrbPointToScreen(new Point(ball.Width - 2, 2));
            Check(Ux0914Checks.Within(ball.HitTestOrb(new Point(ball.Width - 2, 2)) as DependencyObject, clickTarget), "球新矩形区域在内部命中按钮");
            await Click(cornerPoint); Check(controller.Expanded, "右上矩形区域真实左键可展开面板");
            controller.SetExpanded(false); await Ux0914Checks.Settled(ball); await Task.Delay(160);
            cornerPoint = ball.OrbPointToScreen(new Point(ball.Width - 2, 2));
            await Click(cornerPoint, right: true);
            var ballBounds = Native.ScreenBounds(ball);
            Check(controller.EntryMenu.Visible && controller.EntryMenu.Right <= ballBounds.Right - ballBounds.Height - 5, "球新矩形区域真实右键打开共用菜单，菜单在球旁而非贴屏边缘");
            controller.EntryMenu.Close(); await Task.Delay(200);
            var x = ball.Left; var top = ball.Top;
            await Drag(ball.OrbPointToScreen(new Point(ball.Width - 2, 2)), new Vector(-55, 28), right: true);
            Check(Math.Abs(ball.Left - x) < .5 && ball.Top > top + 5 && !controller.EntryMenu.Visible, "新矩形区域右键长按仍可上下拖动，横向固定且不误开菜单");
            Check(controller.Engine.State.Tasks.Count == 1, "所有实际点击均使用隔离数据，未额外创建空任务");
        }
        finally
        {
            mouse_event(4 | 16, 0, 0, 0, 0);
            controller.EntryMenu.Close(); controller.SetExpanded(false);
            backing?.Destroy(false);
            if (moved) SetCursorPos(oldCursor.X, oldCursor.Y);
            SetForegroundWindow(oldForeground);
            File.WriteAllText(Path.Combine(root, "ux0914-desktop-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, metrics, mouseMoved = moved, keyboardInjected = false, productionDataChanged = false, cursorRestored = moved, boundary = "实际鼠标检查与保存前屏幕截图；未覆盖全部显卡、混合DPI和热插拔" }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private static void SaveFrame(SurfaceWindow window, string path)
    {
        var bounds = Native.ScreenBounds(window); using var image = new Drawing.Bitmap((int)bounds.Width, (int)bounds.Height);
        using (var graphics = Drawing.Graphics.FromImage(image)) graphics.CopyFromScreen((int)bounds.Left, (int)bounds.Top, 0, 0, image.Size); image.Save(path);
    }
}

