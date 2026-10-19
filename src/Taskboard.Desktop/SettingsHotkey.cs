using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    private StackPanel HotkeySettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("展开／收起看板快捷键", 15));
        var pending = Copies.Of(controller.Engine.State.Preferences.Hotkey);
        var enabled = new CheckBox { Content = "启用全局快捷键", IsChecked = pending.Enabled, Margin = new Thickness(0, 8, 0, 4) };
        var input = Ui.Input(GlobalHotkey.Caption(pending)); input.IsReadOnly = true; input.Tag = "HotkeyRecorder";
        input.ToolTip = "点击这里，然后按下想使用的组合键；Esc取消本次录入";
        InputMethod.SetIsInputMethodEnabled(input, false);
        var note = Ui.Label(controller.HotkeyStatus, 12, Ui.Muted);
        input.GotKeyboardFocus += (_, _) => { controller.SuspendHotkey(true); note.Text = "请按组合键，完成后点击应用。"; };
        input.LostKeyboardFocus += (_, _) => controller.SuspendHotkey(false);
        input.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            if (key == Key.Escape) { pending = Copies.Of(controller.Engine.State.Preferences.Hotkey); enabled.IsChecked = pending.Enabled; input.Text = GlobalHotkey.Caption(pending); note.Text = controller.HotkeyStatus; Keyboard.ClearFocus(); return; }
            var modifiers = Keyboard.Modifiers;
            var candidate = new BoardHotkey { Enabled = true, Modifiers = (uint)modifiers, VirtualKey = (uint)KeyInterop.VirtualKeyFromKey(key) };
            if (!candidate.IsValid || candidate.VirtualKey == 0x7B) { note.Text = "请按修饰键加另一个按键；F12为Windows保留键。"; return; }
            pending = candidate; enabled.IsChecked = true; input.Text = GlobalHotkey.Caption(pending); note.Text = "点击应用后生效。";
        };
        panel.Children.Add(enabled); panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Ui.Button("应用快捷键", () =>
        { pending.Enabled = enabled.IsChecked == true; note.Text = controller.ConfigureHotkey(pending, out var error) ? controller.HotkeyStatus : error; }));
        buttons.Children.Add(Ui.Button("恢复Ctrl+~", () =>
        {
            var defaults = new BoardHotkey();
            if (!controller.ConfigureHotkey(defaults, out var error)) { note.Text = error; return; }
            pending = defaults; enabled.IsChecked = true; input.Text = GlobalHotkey.Caption(pending); note.Text = controller.HotkeyStatus;
        }));
        panel.Children.Add(buttons); panel.Children.Add(note);
        panel.Children.Add(Ui.Label("默认按Ctrl加键盘左上角的~键，无需Shift；中文输入法同样使用按键组合。程序运行时有效。", 12, Ui.Muted));
        return panel;
    }
}
