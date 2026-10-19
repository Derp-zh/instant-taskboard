using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Taskboard.Desktop;

// WPF's popup collision avoidance can move a Custom placement when the owner
// moves or overlaps the hint. Pin the actual popup HWND in physical screen pixels.
internal sealed class HintScreenPin
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public IntPtr Window, After;
        public int X, Y, Width, Height;
        public uint Flags;
    }
    private readonly ToolTip bubble;
    private readonly Point position;
    private readonly HwndSource source;
    private HintScreenPin(ToolTip bubble, Point position, HwndSource source)
    { this.bubble = bubble; this.position = position; this.source = source; }
    internal static void Attach(ToolTip bubble, Point position)
    {
        if (PresentationSource.FromVisual(bubble) is not HwndSource source) return;
        var pin = new HintScreenPin(bubble, position, source);
        source.AddHook(pin.PositionChanging);
        RoutedEventHandler? closed = null;
        closed = (_, _) => { if (!source.IsDisposed) source.RemoveHook(pin.PositionChanging); bubble.Closed -= closed; };
        bubble.Closed += closed;
        var scale = Native.GetDpiForWindow(source.Handle) / 96.0;
        Native.SetWindowPos(source.Handle, IntPtr.Zero, (int)Math.Round(position.X - bubble.ActualWidth * scale / 2),
            (int)Math.Round(position.Y), 0, 0, 0x15);
    }
    private IntPtr PositionChanging(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0046) return IntPtr.Zero;
        var value = Marshal.PtrToStructure<WindowPosition>(lParam);
        var width = (value.Flags & 1) == 0 ? value.Width : bubble.ActualWidth * Native.GetDpiForWindow(source.Handle) / 96.0;
        value.X = (int)Math.Round(position.X - width / 2); value.Y = (int)Math.Round(position.Y);
        value.Flags &= ~2u;
        Marshal.StructureToPtr(value, lParam, false);
        return IntPtr.Zero;
    }
}
