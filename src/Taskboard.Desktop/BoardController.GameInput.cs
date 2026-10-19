using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private sealed partial class OutsideClickObserver
    {
        private HwndSource? rawSink;
        private IntPtr rawBuffer;
        private bool rawRegistered;
        private int rawError, rawPresses, hookPresses;
        private readonly MousePressGate pressGate = new();
        internal object Status => new { RawInput = rawRegistered, RawError = rawError, RawPresses = rawPresses, HookPresses = hookPresses };
        internal void DisableLegacyHook() => observerDispatcher?.Invoke(() => { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } });
        private void StartRawInput()
        {
            // Background mouse buttons only. Do not use NOLEGACY or capture,
            // suppress, inject into, or change the foreground game's input.
            rawSink = new HwndSource(new HwndSourceParameters("Taskboard mouse input") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
            rawSink.AddHook(RawMessage); rawBuffer = Marshal.AllocHGlobal(64);
            var device = new RawDevice { Page = 1, Usage = 2, Flags = 0x100, Target = rawSink.Handle };
            rawRegistered = RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawDevice>());
            if (!rawRegistered) rawError = Marshal.GetLastWin32Error();
        }
        private void StopRawInput()
        {
            if (rawRegistered) RegisterRawInputDevices([new RawDevice { Page = 1, Usage = 2, Flags = 1 }], 1, (uint)Marshal.SizeOf<RawDevice>());
            rawRegistered = false; rawSink?.Dispose(); rawSink = null;
            if (rawBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(rawBuffer); rawBuffer = IntPtr.Zero; }
        }
        private IntPtr RawMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != 0x00ff || disposed) return IntPtr.Zero;
            var size = 64u; var header = (uint)(8 + IntPtr.Size * 2);
            if (GetRawInputData(lParam, 0x10000003, rawBuffer, ref size, header) == uint.MaxValue || size < header + 8 || Marshal.ReadInt32(rawBuffer) != 0) return IntPtr.Zero;
            var buttons = (ushort)Marshal.ReadInt16(rawBuffer, (int)header + 4);
            if ((buttons & 0x15) == 0 || !GetCursorPos(out var point)) return IntPtr.Zero;
            if ((buttons & 1) != 0) CapturePress(point, unchecked((uint)GetMessageTime()), 0, 2);
            if ((buttons & 4) != 0) CapturePress(point, unchecked((uint)GetMessageTime()), 1, 2);
            if ((buttons & 16) != 0) CapturePress(point, unchecked((uint)GetMessageTime()), 2, 2);
            // Let DefWindowProc perform WM_INPUT cleanup.
            return IntPtr.Zero;
        }
        private void CapturePress(PixelPoint point, uint time, int button, int source)
        {
            if (source == 1) Interlocked.Increment(ref hookPresses); else Interlocked.Increment(ref rawPresses);
            var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
            var visible = !GetCursorInfo(ref cursor) || (cursor.Flags & 1) != 0;
            var now = Environment.TickCount64;
            var pressedAt = now - unchecked((uint)now - time);
            presses.Writer.TryWrite(new Press(point, pressedAt, Interlocked.Read(ref controller.workspaceGeneration), button, source, Native.GetForegroundWindow(), visible));
        }
        private static bool CapturedByExternalWindow(Press press)
        {
            if (press.Foreground == IntPtr.Zero) return false;
            var thread = GetWindowThreadProcessId(press.Foreground, out var process);
            if (process == Environment.ProcessId) return false;
            var gui = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
            if (GetGUIThreadInfo(thread, ref gui) && gui.Capture != IntPtr.Zero) return true;
            return !press.CursorVisible && IsFullscreen(press.Foreground);
        }
        private static bool IsFullscreen(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out var window)) return false;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            return GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)
                && window.Left <= monitor.Bounds.Left && window.Top <= monitor.Bounds.Top
                && window.Right >= monitor.Bounds.Right && window.Bottom >= monitor.Bounds.Bottom;
        }
        [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] private struct PixelRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size, Flags; public IntPtr Cursor; public PixelPoint Position; }
        [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo { public int Size, Flags; public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret; public PixelRect CaretBounds; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public PixelRect Bounds, Work; public int Flags; }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
        [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll")] private static extern int GetMessageTime();
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out PixelPoint point);
        [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out PixelRect bounds);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }
}

// Hook and raw input report the same physical press. Merge only opposite
// sources, never two presses from the same source, including rapid doubles.
internal sealed class MousePressGate
{
    private long lastTime = long.MinValue, lastGeneration;
    private int lastButton, sources;
    internal bool Accept(int button, long time, int source, long generation)
    {
        if (lastTime >= 0 && generation == lastGeneration && button == lastButton && Math.Abs(time - lastTime) <= 12 && (sources & source) == 0)
        { sources |= source; return false; }
        lastTime = time; lastButton = button; lastGeneration = generation; sources = source; return true;
    }
}
