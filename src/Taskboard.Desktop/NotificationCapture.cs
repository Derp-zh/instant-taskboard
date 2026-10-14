using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Threading;
using Taskboard.Core;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Taskboard.Desktop;

// Hooks stay on the WPF message-loop thread; UI Automation traversal runs on one bounded MTA worker.
internal sealed class NotificationCapture : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly string[] knownApps;
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<IntPtr> candidates = Channel.CreateBounded<IntPtr>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Dictionary<uint, (bool Shell, long Checked)> processes = new();
    private readonly Dictionary<IntPtr, long> windows = new();
    private readonly HashSet<uint> seen = new();
    private readonly WinEventCallback callback;
    private readonly DispatcherTimer polling;
    private readonly List<IntPtr> hooks = new();
    private UserNotificationListener? listener;
    private bool disposed, pollingNow, seeded, winrtAvailable;
    private readonly DateTimeOffset started = DateTimeOffset.Now;
    public string Status { get; private set; } = "正在启动监听";
    public bool Available => !disposed && (hooks.Count > 0 || winrtAvailable);
    public event Action? Changed;
    public event Action<CapturedNotification>? Captured;
    public NotificationCapture(Dispatcher dispatcher, string[] knownApps)
    {
        this.dispatcher = dispatcher; this.knownApps = knownApps; callback = OnWindowEvent;
        polling = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await Poll(), dispatcher);
        polling.Stop();
    }
    private void SetStatus(string value) { if (disposed) return; Status = value; Changed?.Invoke(); }
    public async Task Start(NotificationCaptureMode mode, bool requestAccess)
    {
        if (mode != NotificationCaptureMode.WinRT) StartAccessibility();
        if (mode == NotificationCaptureMode.Accessibility) return;
        try
        {
            listener = UserNotificationListener.Current;
            var access = requestAccess ? await listener.RequestAccessAsync() : listener.GetAccessStatus();
            if (disposed) return;
            if (access != UserNotificationListenerAccessStatus.Allowed)
            { SetStatus(hooks.Count > 0 ? "辅助功能监听已启动；Windows通知读取权限未获准" : "Windows通知读取权限未获准，请重试授权或选择辅助功能"); listener = null; return; }
            // Event subscription can fail in unpackaged apps even when polling works.
            try { listener.NotificationChanged += OnNotificationChanged; } catch (COMException) { }
            await Poll();
            if (!Available) return;
            if (!disposed) { polling.Start(); SetStatus(hooks.Count > 0 ? "双通道监听已启动；等待新通知验证捕获" : "Windows通知读取已启动；等待新通知验证捕获"); }
        }
        catch (Exception e)
        { listener = null; SetStatus(hooks.Count > 0 ? "辅助功能监听已启动；Windows读取不可用（" + e.Message + "）" : "监听启动失败：" + e.Message); }
    }
    private void StartAccessibility()
    {
        foreach (var eventId in new uint[] { 0x8002, 0x800C })
        {
            var hook = SetWinEventHook(eventId, eventId, IntPtr.Zero, callback, 0, 0, 2);
            if (hook != IntPtr.Zero) hooks.Add(hook);
        }
        if (hooks.Count == 0) { SetStatus("辅助功能监听启动失败：" + Marshal.GetLastWin32Error()); return; }
        _ = Task.Run(ReadCandidates);
        SetStatus("辅助功能监听已启动；等待新通知验证捕获");
    }
    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    { if (!disposed) dispatcher.BeginInvoke(new Action(async () => await Poll())); }
    private async Task Poll()
    {
        if (disposed || pollingNow || listener is null) return;
        pollingNow = true;
        try
        {
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            { winrtAvailable = false; SetStatus(hooks.Count > 0 ? "权限已撤销，继续辅助功能监听" : "权限已撤销，Windows通知监听不可用"); polling.Stop(); return; }
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            if (disposed) return;
            foreach (var notification in notifications)
            {
                if (!seen.Add(notification.Id) || !seeded || notification.CreationTime < started) continue;
                var appName = notification.AppInfo?.DisplayInfo?.DisplayName ?? "";
                var appId = notification.AppInfo?.AppUserModelId ?? "";
                var lines = notification.Notification.Visual.Bindings.SelectMany(x => x.GetTextElements()).Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                if (lines.Length > 0) Emit(new(appName, appId, lines[0], string.Join("\n", lines.Skip(1)), "Windows通知读取"));
            }
            // Replace with current IDs: CreationTime rejects historical notifications reappearing after trim.
            if (seen.Count > 5000) { seen.Clear(); foreach (var n in notifications) seen.Add(n.Id); }
            seeded = true; winrtAvailable = true;
        }
        catch (Exception e) { winrtAvailable = false; SetStatus("Windows通知读取失败：" + e.Message + (hooks.Count > 0 ? "；辅助功能继续监听" : "")); }
        finally { pollingNow = false; }
    }
    private void OnWindowEvent(IntPtr hook, uint eventId, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        try
        {
            if (disposed || hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return;
            GetWindowThreadProcessId(hwnd, out var pid); var now = Environment.TickCount64;
            if (!processes.TryGetValue(pid, out var cached) || now - cached.Checked > 10000)
            {
                using var process = Process.GetProcessById((int)pid);
                cached = (process.ProcessName is "ShellExperienceHost" or "StartMenuExperienceHost", now);
                if (processes.Count > 128) processes.Clear(); processes[pid] = cached;
            }
            if (!cached.Shell || !GetWindowRect(hwnd, out var rectangle)) return;
            // Scale-aware limit excludes Start/notification-center windows, preserves stacked toast hosts.
            var scale = Math.Max(1, GetDpiForWindow(hwnd) / 96.0);
            var width = (rectangle.Right - rectangle.Left) / scale; var height = (rectangle.Bottom - rectangle.Top) / scale;
            if (width < 80 || height < 40 || width > 800 || height > 700) return;
            if (windows.TryGetValue(hwnd, out var last) && now - last < 200) return;
            if (windows.Count > 256) windows.Clear(); windows[hwnd] = now;
            candidates.Writer.TryWrite(hwnd);
        }
        catch (Exception) { /* A Shell window can disappear between the native calls. */ }
    }
    private async Task ReadCandidates()
    {
        try
        {
            await foreach (var hwnd in candidates.Reader.ReadAllAsync(stop.Token))
            {
                await Task.Delay(300, stop.Token);
                try
                {
                    if (!IsWindowVisible(hwnd)) continue;
                    var root = AutomationElement.FromHandle(hwnd);
                    var cards = FindCards(root);
                    foreach (var card in cards)
                    {
                        var fields = NotificationSoundRules.FromCombinedText(Texts(card), knownApps);
                        if (fields.Length > 0) foreach (var field in fields) _ = dispatcher.BeginInvoke(new Action(() => Emit(field)));
                        else _ = dispatcher.BeginInvoke(new Action(() => SetStatus("已发现通知，但应用来源无法确定；本条未播放")));
                    }
                }
                catch (Exception) { /* Stale or inaccessible UIA providers are expected. */ }
            }
        }
        catch (OperationCanceledException) { }
    }
    private static readonly Condition TextCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
    private static string[] Texts(AutomationElement element) => element.FindAll(TreeScope.Descendants, TextCondition).Cast<AutomationElement>()
        .Where(x => !x.Current.IsOffscreen).Select(x => x.Current.Name).Where(x => !string.IsNullOrWhiteSpace(x) && !Ignored(x)).Take(80).ToArray();
    private static bool Ignored(string text) => new[] { "关闭", "Close", "Dismiss", "Notification", "新通知", "New notification", "通知", "更多选项", "More options" }.Contains(text.Trim(), StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<AutomationElement> FindCards(AutomationElement root)
    {
        var containers = new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        var level = root;
        for (var depth = 0; depth < 3; depth++)
        {
            var children = level.FindAll(TreeScope.Children, containers).Cast<AutomationElement>()
                .Where(x => NotificationSoundRules.FromCombinedText(Texts(x), knownApps).Length > 0).ToArray();
            if (children.Length >= 2) return children;
            if (children.Length != 1) break; level = children[0];
        }
        return [root];
    }
    private void Emit(CapturedNotification notification)
    { if (!disposed && !NotificationSoundRules.IsSelf(notification.AppName)) Captured?.Invoke(notification); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; polling.Stop();
        if (listener is not null) { try { listener.NotificationChanged -= OnNotificationChanged; } catch (Exception) { } }
        foreach (var hook in hooks) UnhookWinEvent(hook); hooks.Clear(); stop.Cancel(); candidates.Writer.TryComplete();
    }
    private delegate void WinEventCallback(IntPtr hook, uint eventId, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rectangle rectangle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
