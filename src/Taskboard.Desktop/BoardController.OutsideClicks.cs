using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private OutsideClickObserver? outsideClicks;
    private SurfaceWindow? outsideTarget;
    private long outsidePressTime;
    private Point outsidePressPoint;
    private long workspaceGeneration;
    internal void VerifyOutsideObserverRegistration() { using var observer = new OutsideClickObserver(this); }
    private System.Collections.Generic.IEnumerable<SurfaceWindow> FloatingWindows() => taskWindows.Values.Cast<SurfaceWindow>().Append(draftStack).Concat(auxiliary).Where(x => x.FloatingAnimations).Distinct();
    internal SurfaceWindow? DismissibleWindow() => FloatingWindows()
        .Where(x => x.ShouldRestore).OrderByDescending(x => x.WindowLayer).ThenByDescending(x => x.ActivationOrder).FirstOrDefault();
    internal bool HasFloatingWindow => FloatingWindows().Any(x => x.IsVisible || x.ShouldRestore);
    internal void ObserveWorkspaceOutsidePress(bool externalAtPress, long? generation = null)
    {
        // A real other-app click folds the whole group exactly like the orb.
        // Focus notifications after closing a window must never replay clicks.
        if (!externalAtPress || (generation is long captured && captured != workspaceGeneration)) return;
        if (Expanded && !SuppressCollapse) SetExpanded(false);
    }
    internal void ObserveOutsidePress(SurfaceWindow target, bool inside, Point screenPoint = default, long? pressedAt = null, bool leftButton = true, bool taskCard = false)
    {
        if (!Expanded || !target.ShouldRestore) return;
        if (inside || !leftButton || taskCard) { outsideTarget = null; return; }
        var now = pressedAt ?? Environment.TickCount64;
        // Physical press time avoids mistaking delayed dispatcher deliveries
        // for a double-click when the UI thread is busy.
        var doubleClick = outsideTarget == target && now >= outsidePressTime
            && now - outsidePressTime <= GetDoubleClickTime()
            && Math.Abs(screenPoint.X - outsidePressPoint.X) <= GetSystemMetrics(36) / 2.0
            && Math.Abs(screenPoint.Y - outsidePressPoint.Y) <= GetSystemMetrics(37) / 2.0;
        if (!doubleClick) { outsideTarget = target; outsidePressTime = now; outsidePressPoint = screenPoint; return; }
        outsideTarget = null;
        if (target is DraftStackSurface) CloseDraftWindow();
        else if (target == settings) target.Hide();
        else
        {
            var editors = Editors.Where(x => x.Host == target).ToArray();
            if (editors.Length == 0) target.Destroy();
            else { foreach (var editor in editors) editor.Suspend(); Persist(); }
        }
    }
    internal void CloseDraftWindow()
    {
        lastClosedDraft = ActiveDraft;
        // Close the host once before detaching views; never promote another
        // draft or recapture a half-removed stack while the group is closing.
        draftStack.Hide();
        layingOutDrafts = true;
        try { foreach (var editor in Editors.Where(x => x.Session.TaskId is null).ToArray()) editor.Suspend(); }
        finally { layingOutDrafts = false; LayoutDrafts(); }
        outsideTarget = null; Persist();
    }
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private sealed class OutsideClickObserver : IDisposable
    {
        private readonly BoardController controller;
        private readonly MouseHook callback;
        private IntPtr hook;
        private readonly IntPtr ballHandle;
        private readonly Dispatcher ui;
        private Dispatcher? observerDispatcher;
        private readonly Thread observerThread;
        private readonly Channel<Press> presses = Channel.CreateUnbounded<Press>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        private volatile bool disposed;
        internal OutsideClickObserver(BoardController controller)
        {
            this.controller = controller; callback = Pressed;
            ui = Application.Current.Dispatcher;
            ballHandle = new WindowInteropHelper(controller.Ball).Handle;
            using var ready = new ManualResetEventSlim();
            Exception? failure = null;
            observerThread = new Thread(() =>
            {
                observerDispatcher = Dispatcher.CurrentDispatcher;
                hook = SetWindowsHookEx(14, callback, IntPtr.Zero, 0);
                if (hook == IntPtr.Zero) failure = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法启用窗口外点击检测");
                ready.Set();
                if (failure is not null) return;
                try { Dispatcher.Run(); }
                finally { UnhookWindowsHookEx(hook); }
            }) { IsBackground = true, Name = "Taskboard outside clicks" };
            observerThread.Start(); ready.Wait();
            if (failure is not null) throw failure;
            _ = Task.Run(DeliverPresses);
        }
        private IntPtr Pressed(int code, IntPtr message, IntPtr data)
        {
            // Never block, suppress or store other applications' clicks.
            if (code >= 0 && !disposed && message.ToInt64() is 0x0201 or 0x0204 or 0x0207)
            {
                // This thread only captures point and time. Never
                // wait for WPF/layout/persistence: Windows removes a slow
                // low-level hook silently, permanently losing outside clicks.
                presses.Writer.TryWrite(new Press(Marshal.PtrToStructure<MouseData>(data).Point,
                    Environment.TickCount64, Interlocked.Read(ref controller.workspaceGeneration), message.ToInt64() == 0x0201));
            }
            return CallNextHookEx(hook, code, message, data);
        }
        private async Task DeliverPresses()
        {
            // Native hit testing can wait for another application's UI. Keep
            // it off the hook thread as well; preserve physical press ordering.
            await foreach (var press in presses.Reader.ReadAllAsync())
            {
                if (disposed) return;
                var (point, pressedAt, generation, leftButton) = press;
                var root = Native.GetAncestor(WindowFromPoint(point), 2);
                GetWindowThreadProcessId(root, out var process);
                var external = root != IntPtr.Zero && process != Environment.ProcessId;
                if (!OwnedBy(root, ballHandle) && !ui.HasShutdownStarted)
                {
                    _ = ui.BeginInvoke(new Action(() =>
                    {
                        if (disposed || controller.exiting || !controller.Expanded || generation != controller.workspaceGeneration || Native.Dialogs().Length != 0) return;
                        if (external)
                        {
                            controller.ObserveWorkspaceOutsidePress(true, generation); return;
                        }
                        var target = controller.DismissibleWindow(); if (target is null) return;
                        var handle = new WindowInteropHelper(target).Handle;
                        var inside = OwnedBy(root, handle);
                        var screenPoint = new Point(point.X, point.Y);
                        var interactiveColumn = controller.Columns.Any(column => new WindowInteropHelper(column).Handle == root && !column.IsOutsideDismissArea(screenPoint));
                        controller.ObserveOutsidePress(target, inside, screenPoint, pressedAt, leftButton, interactiveColumn);
                        if (leftButton && inside && target is DraftStackSurface { IsContentSwapping: true } drafts && target.ShouldRestore)
                        {
                            var bounds = Native.ScreenBounds(target); var scale = Native.GetDpiForWindow(handle) / 96.0;
                            drafts.SwitchAt(new Point((point.X - bounds.Left) / scale - target.SwapInputOffset.X, (point.Y - bounds.Top) / scale - target.SwapInputOffset.Y), pressedAt);
                        }
                    }), DispatcherPriority.Input);
                }
            }
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
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            presses.Writer.TryComplete();
            observerDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
            if (Thread.CurrentThread != observerThread) observerThread.Join(2000);
        }
        private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
        private readonly record struct Press(PixelPoint Point, long PressedAt, long Generation, bool LeftButton);
        [StructLayout(LayoutKind.Sequential)] private struct PixelPoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseData { public PixelPoint Point; public uint Buttons, Flags, Time; public IntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, MouseHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PixelPoint point);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    }
}
