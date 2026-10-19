using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Opt-in real input verification. Program requires an explicit, non-production data directory.
internal static class FeatureDesktopChecks
{
    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct ScreenRect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out ScreenRect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out ScreenPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint process);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    internal static async Task Run(BoardController controller)
    {
        var directory = controller.Store.DirectoryPath; var checks = new List<string>(); var modes = new List<object>();
        var originalForeground = GetForegroundWindow(); GetCursorPos(out var originalCursor);
        Process? sink = null; var serial = 0; Exception? failure = null; var restored = false; var keyboardInjected = false; var mouseMoved = false;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message); checks.Add(message);
            File.WriteAllText(Path.Combine(directory, "feature-desktop-progress.json"), JsonSerializer.Serialize(checks));
        }
        async Task<JsonElement> SinkMode(string mode)
        {
            if (sink is not null) AllowSetForegroundWindow((uint)sink.Id);
            File.WriteAllText(Path.Combine(directory, "sink-command.tmp"), JsonSerializer.Serialize(new { Serial = ++serial, Mode = mode }));
            File.Move(Path.Combine(directory, "sink-command.tmp"), Path.Combine(directory, "sink-command.json"), true);
            for (var i = 0; i < 600; i++)
            {
                await Task.Delay(50);
                var error = Path.Combine(directory, "sink-error.txt"); if (File.Exists(error)) throw new InvalidOperationException(File.ReadAllText(error));
                var state = Path.Combine(directory, "sink-state.json"); if (!File.Exists(state)) continue;
                try { using var doc = JsonDocument.Parse(File.ReadAllText(state)); if (doc.RootElement.GetProperty("Serial").GetInt32() == serial) return doc.RootElement.Clone(); }
                catch (IOException) { }
            }
            throw new TimeoutException("输入法隔离窗口未回应：" + mode);
        }
        async Task PressHotkey()
        {
            keyboardInjected = true;
            try { keybd_event(0x11, 0, 0, 0); keybd_event(0xC0, 0, 0, 0); await Task.Delay(60); keybd_event(0xC0, 0, 0, 0); await Task.Delay(60); }
            finally { keybd_event(0xC0, 0, 2, 0); keybd_event(0x11, 0, 2, 0); }
            await Task.Delay(400);
        }
        async Task FocusSink(JsonElement state)
        {
            var window = (nint)state.GetProperty("Window").GetInt64(); SetForegroundWindow(window);
            if (!GetWindowRect((nint)state.GetProperty("TextWindow").GetInt64(), out var rect)) throw new InvalidOperationException("输入目标窗口已关闭");
            mouseMoved |= SetCursorPos(rect.Left + (rect.Right - rect.Left) / 2, rect.Top + (rect.Bottom - rect.Top) / 2);
            mouse_event(2, 0, 0, 0, 0); await Task.Delay(40); mouse_event(4, 0, 0, 0, 0); await Task.Delay(150);
        }
        async Task MouseClick(FrameworkElement element)
        {
            var point = element.PointToScreen(new Point(Math.Min(24, element.ActualWidth / 2), element.ActualHeight / 2));
            mouseMoved |= SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(60); mouse_event(2, 0, 0, 0, 0); await Task.Delay(45); mouse_event(4, 0, 0, 0, 0); await Task.Delay(300);
        }
        try
        {
            controller.SuppressCollapse = true; controller.EnableIsolatedHotkeyRegistration = true;
            Check(controller.ConfigureHotkey(new BoardHotkey(), out var error), "默认Ctrl+~实际注册成功：" + error);
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Environment.CurrentDirectory };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-STA", "-File", Path.Combine(Environment.CurrentDirectory, "tools", "Hotkey-Input-Sink.ps1"), "-DataDirectory", directory }) start.ArgumentList.Add(argument);
            sink = Process.Start(start) ?? throw new InvalidOperationException("无法启动隔离输入窗口");
            var draft = controller.NewTask(Lane.Action); draft.Session.Working.Title = "快捷键保留草稿";
            controller.SetExpanded(false); await Task.Delay(250);
            foreach (var mode in new[] { "english", "chinese" })
            {
                var state = await SinkMode(mode); modes.Add(state);
                await FocusSink(state);
                Check(GetForegroundWindow() == (nint)state.GetProperty("Window").GetInt64(), mode + "键盘输入目标确为隔离窗口");
                if (mode == "chinese") Check(state.GetProperty("HasChinese").GetBoolean() && state.GetProperty("ImeOpen").GetBoolean(), "中文输入法实际处于开启状态");
                var before = controller.HotkeyPresses; await PressHotkey();
                Check(controller.Expanded && controller.HotkeyPresses == before + 1 && draft.Host!.ShouldRestore && draft.Session.Working.Title == "快捷键保留草稿", mode + "：Ctrl+~展开并保留草稿，长按重复消息不连跳");
                state = await SinkMode(mode); await FocusSink(state);
                Check(GetForegroundWindow() == (nint)state.GetProperty("Window").GetInt64(), mode + "再次触发前输入仍定位隔离窗口");
                before = controller.HotkeyPresses; await PressHotkey();
                Check(!controller.Expanded && controller.HotkeyPresses == before + 1, mode + "：Ctrl+~再次收起整组");
            }
            File.WriteAllText(Path.Combine(directory, "sink-command.json"), JsonSerializer.Serialize(new { Serial = ++serial, Mode = "close" }));
            await Task.Delay(250); controller.CloseDraftWindow(); controller.SetExpanded(true);
            var session = controller.Engine.NewSession(Lane.Action); session.Working.Title = "隔离鼠标测试任务"; var item = controller.Engine.Submit(session);
            var column = controller.Columns.First(x => x.Lane == Lane.Action); var area = controller.WorkArea(controller.Ball);
            foreach (var other in controller.Columns.Where(x => x != column)) other.Hide();
            column.Left = area.Left + 12; column.Top = area.Top + 24; column.Width = 280; column.Height = Math.Min(600, area.Height - 48); column.Activate(); await Task.Delay(400);
            FrameworkElement Title() => StageFeatureChecks.Children<TextBlock>(column.Locate(item.Id)!).First(x => Equals(x.Tag, "TaskTitle"));
            await MouseClick(Title());
            var editor = controller.Editors.Single(x => x.Session.TaskId == item.Id);
            StageFeatureChecks.Children<TextBox>(editor).First().Text = "真实鼠标未提交内容";
            await Task.Delay((int)GetDoubleClickTime() + 50); await MouseClick(Title());
            Check(controller.Editors.All(x => x.Session.TaskId != item.Id), "实际鼠标再次点击任务卡片关闭对应编辑窗口");
            await Task.Delay((int)GetDoubleClickTime() + 50); await MouseClick(Title());
            editor = controller.Editors.Single(x => x.Session.TaskId == item.Id);
            Check(StageFeatureChecks.Children<TextBox>(editor).First().Text == "真实鼠标未提交内容", "实际鼠标重新打开保留未提交编辑");
            var hints = Ui.HintCount;
            await MouseClick(StageFeatureChecks.Children<Button>(editor).Single(x => Equals(x.Tag, "StageRun")));
            Check(Ui.HintCount == hints + 1 && Ui.ActiveHint is { IsOpen: true, IsVisible: true } && editor.Session.Working.Stages[0].IsRunning == true, "编辑窗口最后运行阶段真实点击后显示气泡并保留状态");
            controller.ToggleTaskEditor(item.Id); await Task.Delay(300); hints = Ui.HintCount;
            await MouseClick(StageFeatureChecks.Children<Button>(column.Locate(item.Id)!).Single(x => Equals(x.Tag, "TaskStageRun")));
            Check(Ui.HintCount == hints + 1 && Ui.ActiveHint is { IsOpen: true, IsVisible: true } && item.Stages[0].IsRunning == true, "列表最后运行阶段真实点击后显示气泡且不会打开编辑");
            var settingsButton = StageFeatureChecks.Children<Button>(controller.Ball.Root).Single(x => Equals(x.Content, "设置"));
            await MouseClick(settingsButton); var settings = Application.Current.Windows.OfType<SettingsSurface>().Single();
            Check(settings.ShouldRestore, "实际鼠标点击设置按钮打开设置");
            await Task.Delay((int)GetDoubleClickTime() + 50); await MouseClick(settingsButton);
            Check(!settings.ShouldRestore, "实际鼠标再次点击设置关闭设置");
        }
        catch (Exception e) { failure = e; }
        finally
        {
            controller.ConfigureHotkey(new BoardHotkey { Enabled = false }, out _); controller.EnableIsolatedHotkeyRegistration = false;
            if (sink is not null)
            {
                File.WriteAllText(Path.Combine(directory, "sink-command.json"), JsonSerializer.Serialize(new { Serial = ++serial, Mode = "close" }));
                for (var i = 0; i < 20 && !sink.HasExited; i++) await Task.Delay(50);
                if (!sink.HasExited) sink.Kill();
                File.WriteAllText(Path.Combine(directory, "sink-stderr.txt"), await sink.StandardError.ReadToEndAsync()); sink.Dispose();
            }
            SetCursorPos(originalCursor.X, originalCursor.Y); if (originalForeground != 0) SetForegroundWindow(originalForeground);
            restored = GetCursorPos(out var currentCursor) && currentCursor.X == originalCursor.X && currentCursor.Y == originalCursor.Y;
            File.WriteAllText(Path.Combine(directory, "feature-desktop-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, modes, isolated = true, keyboardInjected, mouseMoved, restored, error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (failure is not null) throw failure;
    }
}
