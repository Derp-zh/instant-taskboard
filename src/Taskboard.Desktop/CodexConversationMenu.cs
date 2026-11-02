using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using Condition = System.Windows.Automation.Condition;

namespace Taskboard.Desktop;

// Uses Codex's own "Copy deeplink" action, which is bound to the actual row/header
// thread ID. No catalog/title match, app restart, developer endpoint or chat input.
internal static class CodexConversationMenu
{
    internal static string LastStep { get; private set; } = "not-started";
    internal sealed record Anchor(IntPtr Window, uint ProcessId, AutomationElement Element, string Name, bool Sidebar);
    private static readonly string[] HeaderNames = ["聊天操作", "Chat actions", "Thread actions"];
    private static readonly string[] CopyNames = ["复制", "Copy"];
    internal static bool IsDeeplinkAction(string name) => new[] { "复制深度链接", "复制深层链接", "复制应用链接", "Copy deeplink", "Copy deep link" }
        .Any(prefix => name == prefix || name.StartsWith(prefix + " ", StringComparison.Ordinal) || name.StartsWith(prefix + "\t", StringComparison.Ordinal));
    private static Condition ButtonsNamed(string[] names) => new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        new OrCondition(names.Select(name => (Condition)new PropertyCondition(AutomationElement.NameProperty, name)).ToArray()));
    private static bool Visible(AutomationElement element) => !element.Current.IsOffscreen && element.Current.IsEnabled
        && element.Current.BoundingRectangle.Width > 1 && element.Current.BoundingRectangle.Height > 1;
    internal static Anchor? TryLocate(Point point)
    {
        var window = RootAt(point); GetWindowThreadProcessId(window, out var pid);
        if (!CodexConversationDropTarget.VerifyDesktop(pid)) return null;
        var root = AutomationElement.FromHandle(window); var hit = AutomationElement.FromPoint(point);
        var ancestors = new List<AutomationElement>(); AutomationElement? document = null;
        for (var current = hit; current is not null && ancestors.Count < 30; current = TreeWalker.ControlViewWalker.GetParent(current))
        {
            var info = current.Current; ancestors.Add(current);
            if (info.ControlType == ControlType.Button && info.ClassName.Contains("sidebar-item", StringComparison.Ordinal) && Visible(current))
                return new(window, pid, current, info.Name, true);
            if (info.ControlType == ControlType.Document) document = current;
            if (info.ControlType == ControlType.Window) break;
        }
        if (document is null) return null;
        var headers = root.FindAll(TreeScope.Descendants, ButtonsNamed(HeaderNames)).Cast<AutomationElement>().Where(Visible).ToArray();
        if (headers.Length == 0) return null;
        foreach (var ancestor in ancestors)
        {
            var bounds = ancestor.Current.BoundingRectangle;
            if (!bounds.Contains(point) || bounds.Width < 300 || bounds.Height < 100) continue;
            var scoped = headers.Where(x => bounds.Contains(x.Current.BoundingRectangle)).ToArray();
            if (scoped.Length != 1) continue;
            // A sidebar blank/project action must not fall back to the open chat.
            var rows = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
                .Cast<AutomationElement>().Where(x => Visible(x) && x.Current.ClassName.Contains("sidebar-item", StringComparison.Ordinal)).ToArray();
            if (rows.Length > 0 && point.X < rows.Max(x => x.Current.BoundingRectangle.Right)) return null;
            return new(window, pid, scoped[0], document.Current.Name, false);
        }
        return null;
    }
    internal static async Task<ConversationDropTarget> Resolve(Point point)
    {
        Anchor? anchor = null; ConversationClipboard? previous = null;
        uint originalSequence = 0, ownedSequence = 0; bool menuOpened = false, restored = false;
        var previousForeground = Native.GetForegroundWindow();
        ConversationDropTarget result = ConversationDropTarget.Unavailable("未能取得 Codex 会话链接，请重新拖动");
        try
        {
            LastStep = "locate";
            anchor = await Task.Run(() => TryLocate(point));
            if (anchor is null) return result;
            if ((await Task.Run(() => MenuItems(anchor.ProcessId))).Length != 0)
                return ConversationDropTarget.Unavailable("Codex 菜单正在使用，请关闭后重新拖动");
            LastStep = "clipboard-cache";
            previous = new ConversationClipboard(); originalSequence = previous.Sequence;
            LastStep = "open-menu";
            Foreground(anchor.Window); await Task.Delay(100);
            if (!PointerStillAt(point) || RootAt(point) != anchor.Window)
                return ConversationDropTarget.Unavailable("落点已变化，请重新拖动");
            if (anchor.Sidebar)
            {
                // Recheck the actual row after activation. Never click a guessed position.
                var fresh = await Task.Run(() => TryLocate(point));
                if (fresh is null || !fresh.Sidebar || !fresh.Element.GetRuntimeId().SequenceEqual(anchor.Element.GetRuntimeId())) return result;
                SendRightClick();
            }
            else
            {
                // The current Codex chat-actions button exposes ExpandCollapse,
                // whereas menu commands expose Invoke. Use the advertised pattern.
                if (anchor.Element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var headerExpand))
                {
                    LastStep = "expand-header";
                    await Task.Run(() => ((ExpandCollapsePattern)headerExpand).Expand());
                }
                else if (anchor.Element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
                {
                    LastStep = "invoke-header";
                    await Task.Run(() => ((InvokePattern)invoke).Invoke());
                }
                else return result;
            }
            var items = await WaitForMenu(anchor.ProcessId); menuOpened = items.Length > 0; LastStep = "find-copy-menu:" + items.Length;
            var copy = items.Where(x => IsDeeplinkAction(x.Current.Name)).ToArray();
            if (copy.Length == 0)
            {
                var parents = items.Where(x => CopyNames.Contains(x.Current.Name, StringComparer.Ordinal)).ToArray();
                if (parents.Length != 1 || !parents[0].TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand)) return result;
                LastStep = "expand-copy"; await Task.Run(() => ((ExpandCollapsePattern)expand).Expand());
                for (var i = 0; i < 12 && copy.Length == 0; i++)
                {
                    await Task.Delay(50);
                    items = await Task.Run(() => MenuItems(anchor.ProcessId));
                    copy = items.Where(x => IsDeeplinkAction(x.Current.Name)).ToArray();
                }
            }
            LastStep = "copy-action:" + copy.Length;
            if (copy.Length != 1 || !copy[0].TryGetCurrentPattern(InvokePattern.Pattern, out var copyAction)) return result;
            if (GetClipboardSequenceNumber() != originalSequence || !PointerStillAt(point) || !ForegroundBelongsTo(anchor.ProcessId))
                return ConversationDropTarget.Unavailable("操作期间输入或剪贴板已变化，请重新拖动");
            LastStep = "copy-invoke"; await Task.Run(() => ((InvokePattern)copyAction).Invoke());
            for (var i = 0; i < 35; i++)
            {
                await Task.Delay(30);
                var sequence = GetClipboardSequenceNumber();
                if (sequence == originalSequence) continue;
                var owner = GetClipboardOwner(); GetWindowThreadProcessId(owner, out var clipboardPid);
                if (clipboardPid != 0 && clipboardPid != anchor.ProcessId && !CodexConversationDropTarget.VerifyDesktop(clipboardPid))
                    return ConversationDropTarget.Unavailable("剪贴板来源已变化，请重试");
                var value = System.Windows.Clipboard.GetText();
                if (GetClipboardSequenceNumber() != sequence) return result;
                ownedSequence = sequence;
                var id = CodexConversationDropTarget.ParseThreadUri(value);
                if (id is null) return ConversationDropTarget.Unavailable("Codex 未返回本机会话链接，请重试");
                LastStep = "copied"; result = new(id, string.IsNullOrWhiteSpace(anchor.Name) ? "未命名会话" : anchor.Name,
                    "已从原 Codex 会话菜单取得编号", false, "codex-copy-deeplink");
                break;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or COMException or ArgumentException or System.ComponentModel.Win32Exception)
        { LastStep += ":" + error.GetType().Name; result = ConversationDropTarget.Unavailable("读取 Codex 会话链接未完成，请重新拖动"); }
        finally
        {
            // Never send Escape to an open conversation: only to a verified, still-open menu.
            try
            {
                if (menuOpened && anchor is not null)
                    for (var i = 0; i < 2 && ForegroundBelongsTo(anchor.ProcessId) && (await Task.Run(() => MenuItems(anchor.ProcessId))).Length > 0; i++)
                    { if (!ForegroundBelongsTo(anchor.ProcessId)) break; SendEscape(); await Task.Delay(60); }
            }
            catch (Exception error) when (error is InvalidOperationException or COMException) { }
            if (ownedSequence != 0 && GetClipboardSequenceNumber() == ownedSequence)
                for (var i = 0; i < 5 && !restored; i++)
                {
                    if (GetClipboardSequenceNumber() != ownedSequence) break;
                    try { restored = previous?.Restore(ownedSequence) == true; break; }
                    catch (ExternalException) { }
                    await Task.Delay(30);
                }
            if (anchor is not null && ForegroundBelongsTo(anchor.ProcessId)) Foreground(previousForeground);
        }
        return result with { ClipboardRestored = ownedSequence == 0 ? null : restored };
    }
    private static async Task<AutomationElement[]> WaitForMenu(uint pid)
    {
        for (var i = 0; i < 18; i++)
        { await Task.Delay(60); var items = await Task.Run(() => MenuItems(pid)); if (items.Length > 0) return items; }
        return [];
    }
    private static AutomationElement[] MenuItems(uint pid)
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) => { GetWindowThreadProcessId(hwnd, out var owner); if (owner == pid && IsWindowVisible(hwnd)) windows.Add(hwnd); return true; }, IntPtr.Zero);
        var result = new List<AutomationElement>(); var seen = new HashSet<string>();
        foreach (var hwnd in windows)
        {
            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                foreach (AutomationElement item in root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)))
                    if (Visible(item) && !item.Current.AutomationId.StartsWith("application-menu-trigger-", StringComparison.Ordinal)
                        && seen.Add(string.Join(",", item.GetRuntimeId()))) result.Add(item);
            }
            catch (ElementNotAvailableException) { }
        }
        return result.ToArray();
    }
    private static bool PointerStillAt(Point point) => GetCursorPos(out var cursor) && Math.Abs(cursor.X - point.X) < 5 && Math.Abs(cursor.Y - point.Y) < 5;
    private static IntPtr RootAt(Point point) => GetAncestor(WindowFromPoint(new NativePoint((int)point.X, (int)point.Y)), 2);
    private static bool ForegroundBelongsTo(uint pid) { GetWindowThreadProcessId(Native.GetForegroundWindow(), out var foreground); return foreground == pid; }
    private static void Foreground(IntPtr hwnd)
    {
        var other = GetWindowThreadProcessId(Native.GetForegroundWindow(), out _); var self = GetCurrentThreadId();
        var attached = other != self && AttachThreadInput(self, other, true);
        try { BringWindowToTop(hwnd); SetForegroundWindow(hwnd); }
        finally { if (attached) AttachThreadInput(self, other, false); }
    }
    private static void SendRightClick() => Send([new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = 8 } } }, new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = 16 } } }]);
    private static void SendEscape() => Send([new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = 27 } } }, new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = 27, Flags = 2 } } }]);
    private static void Send(Input[] inputs) { if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) throw new InvalidOperationException("系统未接受输入"); }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public IntPtr Extra; }
    private delegate bool WindowCallback(IntPtr hwnd, IntPtr value);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr value);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
}
