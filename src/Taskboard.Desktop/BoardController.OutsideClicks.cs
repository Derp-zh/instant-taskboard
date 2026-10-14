using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private OutsideClickObserver? outsideClicks;
    private SurfaceWindow? outsideTarget;
    private int outsideCount;
    private long lastProtectedClick = long.MinValue / 2;
    internal void VerifyOutsideObserverRegistration() { using var observer = new OutsideClickObserver(this); }
    internal SurfaceWindow? DismissibleWindow() => taskWindows.Values.Cast<SurfaceWindow>().Append(draftStack).Append(settings)
        .Where(x => x is not null && x.ShouldRestore).OrderByDescending(x => x!.WindowLayer).ThenByDescending(x => x!.ActivationOrder).FirstOrDefault();
    internal void ObserveOutsidePress(SurfaceWindow target, bool inside)
    {
        if (!Expanded || !target.ShouldRestore) return;
        lastProtectedClick = Environment.TickCount64;
        if (inside) { outsideTarget = null; outsideCount = 0; return; }
        if (outsideTarget != target) { outsideTarget = target; outsideCount = 0; }
        if (++outsideCount < 2) return;
        outsideTarget = null; outsideCount = 0;
        if (target == settings) target.Hide();
        else
        {
            if (target is DraftStackSurface) CloseDraftWindow();
            else { foreach (var editor in Editors.Where(x => x.Host == target).ToArray()) editor.Suspend(); Persist(); }
        }
    }
    internal void CloseDraftWindow()
    {
        lastClosedDraft = ActiveDraft;
        // Close the host once before detaching views; never promote another
        // draft or recapture a half-removed stack while the group is closing.
        draftStack.Hide();
        layingOutDrafts = true;
        try { foreach (var editor in Editors.Where(x => x.Session.TaskId is null && x.Host == draftStack).ToArray()) editor.Suspend(); }
        finally { layingOutDrafts = false; LayoutDrafts(); }
        outsideTarget = null; outsideCount = 0; Persist();
    }
    private sealed class OutsideClickObserver : IDisposable
    {
        private readonly BoardController controller;
        private readonly MouseHook callback;
        private readonly IntPtr hook;
        private bool disposed;
        internal OutsideClickObserver(BoardController controller)
        {
            this.controller = controller; callback = Pressed;
            hook = SetWindowsHookEx(14, callback, IntPtr.Zero, 0);
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法启用窗口外点击检测");
        }
        private IntPtr Pressed(int code, IntPtr message, IntPtr data)
        {
            // Never block, suppress or store other applications' clicks.
            if (code >= 0 && !disposed && !controller.exiting && message.ToInt64() is 0x0201 or 0x0204 or 0x0207)
            {
                var target = controller.DismissibleWindow();
                if (target is not null && controller.Expanded)
                {
                    var point = Marshal.PtrToStructure<MouseData>(data).Point;
                    var hit = WindowFromPoint(point); var root = Native.GetAncestor(hit, 2);
                    if (!OwnedBy(root, new WindowInteropHelper(controller.Ball).Handle))
                    {
                        var handle = new WindowInteropHelper(target).Handle;
                        var inside = OwnedBy(root, handle);
                        var switchDuringAnimation = inside && target is DraftStackSurface { IsContentSwapping: true };
                        var bounds = Native.ScreenBounds(target); var scale = Native.GetDpiForWindow(handle) / 96.0;
                        var local = new Point((point.X - bounds.Left) / scale - target.SwapInputOffset.X, (point.Y - bounds.Top) / scale - target.SwapInputOffset.Y);
                        controller.lastProtectedClick = Environment.TickCount64;
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!disposed && !controller.exiting && Native.Dialogs().Length == 0)
                            {
                                controller.ObserveOutsidePress(target, inside);
                                if (switchDuringAnimation && target.ShouldRestore && target is DraftStackSurface drafts) drafts.SwitchAt(local);
                            }
                        }), DispatcherPriority.Input);
                    }
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }
        private static bool OwnedBy(IntPtr hit, IntPtr owner)
        {
            for (var i = 0; hit != IntPtr.Zero && i < 12; i++)
            {
                if (hit == owner || Native.GetAncestor(hit, 2) == owner) return true;
                hit = GetWindow(hit, 4);
            }
            return false;
        }
        public void Dispose() { if (disposed) return; disposed = true; if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); }
        private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct PixelPoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseData { public PixelPoint Point; public uint Buttons, Flags, Time; public IntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, MouseHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PixelPoint point);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    }
}
