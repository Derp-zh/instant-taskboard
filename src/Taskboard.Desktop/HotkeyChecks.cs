using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class HotkeyChecks
{
    // Native registration contract only: no keyboard injection or default-key capture.
    internal static void Run(Action<bool, string> check)
    {
        using var competitor = new HwndSource(new HwndSourceParameters("Taskboard.IsolatedHotkeyProbe") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        using var hotkey = new GlobalHotkey(() => { });
        var candidates = Enumerable.Range(0x75, 6).Select(x => new BoardHotkey { Modifiers = 7, VirtualKey = (uint)x }).ToArray();
        var original = candidates.FirstOrDefault(x => hotkey.Apply(x, out _)) ?? throw new InvalidOperationException("隔离快捷键注册检查没有空闲测试组合");
        var blocked = candidates.FirstOrDefault(x => x.VirtualKey != original.VirtualKey && RegisterHotKey(competitor.Handle, 1, x.Modifiers | 0x4000, x.VirtualKey))
            ?? throw new InvalidOperationException("隔离冲突检查没有空闲测试组合");
        try
        {
            check(!hotkey.Apply(blocked, out var error) && error.Length > 0 && !RegisterHotKey(competitor.Handle, 2, original.Modifiers | 0x4000, original.VirtualKey), "真实Win32注册冲突保留原快捷键，原组合仍由本服务占有");
            UnregisterHotKey(competitor.Handle, 1);
            check(hotkey.Apply(blocked, out _) && RegisterHotKey(competitor.Handle, 2, original.Modifiers | 0x4000, original.VirtualKey), "新组合注册成功后释放旧组合，其他窗口可以立即注册旧组合");
            check(hotkey.Apply(new BoardHotkey { Enabled = false }, out _) && RegisterHotKey(competitor.Handle, 3, blocked.Modifiers | 0x4000, blocked.VirtualKey), "关闭全局快捷键后立即释放系统注册");
            UnregisterHotKey(competitor.Handle, 3);
            if (!hotkey.Apply(blocked, out _)) throw new InvalidOperationException("退出释放检查准备失败");
            hotkey.Dispose();
            check(RegisterHotKey(competitor.Handle, 3, blocked.Modifiers | 0x4000, blocked.VirtualKey), "服务退出释放快捷键，不遗留系统占用；测试未发送键盘输入");
        }
        finally { for (var id = 1; id <= 3; id++) UnregisterHotKey(competitor.Handle, id); }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
}
