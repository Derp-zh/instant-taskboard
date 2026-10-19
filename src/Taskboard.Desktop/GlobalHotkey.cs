using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class GlobalHotkey : IDisposable
{
    private readonly HwndSource source;
    private readonly Action pressed;
    private BoardHotkey? active;
    private int activeId;
    private int nextId = 100;
    private bool disposed;
    internal bool Suspended { get; set; }
    internal BoardHotkey? Current => active is null ? null : Copies.Of(active);
    internal GlobalHotkey(Action pressed)
    {
        this.pressed = pressed;
        source = new HwndSource(new HwndSourceParameters("Taskboard.Hotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        source.AddHook(Receive);
    }
    internal bool Apply(BoardHotkey value, out string error)
    {
        error = "";
        if (!value.IsValid) { error = "请使用Ctrl、Alt、Shift或Win加另一个按键。"; return false; }
        if (active is not null && Copies.Same(active, value)) return true;
        if (!value.Enabled)
        { if (activeId != 0) UnregisterHotKey(source.Handle, activeId); activeId = 0; active = Copies.Of(value); return true; }
        var candidateId = nextId++;
        if (nextId >= 0xBFFF) nextId = 100;
        // Register the replacement first: a conflict must leave the old key usable.
        if (!RegisterHotKey(source.Handle, candidateId, value.Modifiers | 0x4000, value.VirtualKey))
        { error = "此快捷键已被占用或Windows不允许使用，请换一组。" + (active is { Enabled: true } ? "原快捷键保持有效。" : ""); return false; }
        var previousId = activeId; activeId = candidateId; active = Copies.Of(value);
        if (previousId != 0) UnregisterHotKey(source.Handle, previousId);
        return true;
    }
    private IntPtr Receive(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x312 && wParam.ToInt32() == activeId && activeId != 0)
        { handled = true; if (!Suspended) pressed(); }
        return IntPtr.Zero;
    }
    internal static string Caption(BoardHotkey value)
    {
        var modifiers = (value.Modifiers & 2) != 0 ? "Ctrl+" : "";
        if ((value.Modifiers & 1) != 0) modifiers += "Alt+";
        if ((value.Modifiers & 4) != 0) modifiers += "Shift+";
        if ((value.Modifiers & 8) != 0) modifiers += "Win+";
        var key = value.VirtualKey == 0xC0 ? "~" : System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)value.VirtualKey).ToString();
        return modifiers + key;
    }
    public void Dispose()
    { if (disposed) return; disposed = true; if (activeId != 0) UnregisterHotKey(source.Handle, activeId); activeId = 0; source.RemoveHook(Receive); source.Dispose(); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
}
