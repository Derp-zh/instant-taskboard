using System;
using System.Linq;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private GlobalHotkey? globalHotkey;
    internal bool EnableIsolatedHotkeyRegistration { get; set; }
    internal int HotkeyPresses { get; private set; }
    internal string HotkeyStatus { get; private set; } = "快捷键尚未启用";
    internal void SuspendHotkey(bool suspended) { if (globalHotkey is not null) globalHotkey.Suspended = suspended; }
    internal bool ConfigureHotkey(BoardHotkey value, out string error)
    {
        error = "";
        if (!value.IsValid) { error = "请使用修饰键加另一个按键。"; return false; }
        if (!ApplyHotkey(value, out error)) return false;
        Engine.State.Preferences.Hotkey = Copies.Of(value); Engine.Touch(); return true;
    }
    private bool ApplyHotkey(BoardHotkey value, out string error)
    {
        error = "";
        if (Isolated && !EnableIsolatedHotkeyRegistration) { HotkeyStatus = "隔离验证不占用系统快捷键 · " + GlobalHotkey.Caption(value); return true; }
        globalHotkey ??= new GlobalHotkey(() => { if (!exiting && !dataTransition) { HotkeyPresses++; ToggleFromHotkey(); } });
        if (!globalHotkey.Apply(value, out error)) { HotkeyStatus = error; return false; }
        HotkeyStatus = value.Enabled ? "当前快捷键：" + GlobalHotkey.Caption(value) : "全局快捷键已关闭";
        return true;
    }
    private void RefreshHotkey()
    {
        var previous = globalHotkey?.Current;
        if (ApplyHotkey(Engine.State.Preferences.Hotkey, out _) || previous is null) return;
        // Imported configuration must not leave a failed candidate labelled as active.
        Engine.State.Preferences.Hotkey = previous; Engine.Touch();
        HotkeyStatus += previous.Enabled ? " 当前仍为" + GlobalHotkey.Caption(previous) : " 当前保持关闭";
    }
    internal void ToggleFromHotkey()
    {
        if (Expanded) { SetExpanded(false); return; }
        SetExpanded(true);
        foreach (var column in VisibleColumns) Native.Raise(column);
        foreach (var window in FloatingWindows().Where(x => x.ShouldRestore).OrderBy(x => x.ActivationOrder)) Native.Raise(window);
        var target = DismissibleWindow() ?? (SurfaceWindow?)VisibleColumns.FirstOrDefault() ?? Ball;
        target.Activate(); Native.RaiseBall();
    }
}
