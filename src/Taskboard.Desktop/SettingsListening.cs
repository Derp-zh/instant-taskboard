using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    private TextBlock? listeningStatus, playbackStatus;
    private StackPanel? listeningHistory;
    private readonly Dictionary<Guid, TextBlock> listeningRuleStatus = new();
    internal void RefreshListeningStatus()
    {
        if (listeningStatus is not null) listeningStatus.Text = controller.Listening.Status;
        if (playbackStatus is not null) playbackStatus.Text = controller.Listening.PlaybackStatus;
        foreach (var (id, label) in listeningRuleStatus) label.Text = controller.Listening.RuleStatus(id);
        if (listeningHistory is null) return;
        listeningHistory.Children.Clear();
        foreach (var entry in controller.Listening.History.Take(8))
            listeningHistory.Children.Add(Ui.Label($"{entry.LocalTime:HH:mm:ss} · {entry.App} · {entry.Source}\n{entry.Result}", 11, Ui.Muted));
    }
    private async Task SaveListening()
    {
        controller.Listening.Suspend();
        controller.Engine.Touch();
        if (!await controller.Flush()) return;
        await controller.Listening.Apply(true); RefreshListeningStatus();
    }
    private StackPanel ListeningSettings()
    {
        var panel = new StackPanel(); var options = controller.Engine.State.Preferences.Listening;
        panel.Children.Add(Ui.Label("监听", 18));
        panel.Children.Add(Ui.Label("为不同应用分别播放自己的通知音。保留Windows横幅和应用自身声音。", 12, Ui.Muted));
        var enabled = new CheckBox { Content = "启用通知音替换", IsChecked = options.Enabled, Margin = new Thickness(0, 10, 0, 8), IsEnabled = !controller.Isolated };
        enabled.Click += async (_, _) => { options.Enabled = enabled.IsChecked == true; await SaveListening(); }; panel.Children.Add(enabled);
        listeningStatus = Ui.Label(controller.Listening.Status, 12, Ui.Accent); panel.Children.Add(listeningStatus);
        panel.Children.Add(Ui.Label("开启替换会自动关闭已关联应用的Windows通知音；关闭替换或正常退出时恢复原设置。系统更新后请用一条实际通知确认效果。", 12, Ui.Muted));
        var system = Ui.Button("打开Windows通知设置", () => Try(() => Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true })));
        system.IsEnabled = !controller.Isolated; panel.Children.Add(system);
        panel.Children.Add(Ui.Label("捕获方式", 13));
        var mode = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = new[] { "自动：双通道", "辅助功能：捕获横幅", "Windows通知读取：需要权限" }, SelectedIndex = (int)options.CaptureMode };
        mode.SelectionChanged += async (_, _) => { options.CaptureMode = (NotificationCaptureMode)mode.SelectedIndex; await SaveListening(); }; panel.Children.Add(mode);
        panel.Children.Add(Ui.Label("辅助功能需要通知横幅可见；开启勿扰、关闭横幅或通知来源无法识别时，可能无法捕获。", 11, Ui.Muted));
        var retry = Ui.Button("重试监听 / 请求通知读取权限", async () => { await controller.Listening.Apply(true); RefreshListeningStatus(); }); retry.IsEnabled = !controller.Isolated; panel.Children.Add(retry);
        var volumeLabel = Ui.Label($"替换音量 {options.Volume * 100:0}%", 13); panel.Children.Add(volumeLabel);
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = options.Volume * 100, Margin = new Thickness(6, 4, 6, 12) };
        volume.ValueChanged += (_, _) => { options.Volume = volume.Value / 100; volumeLabel.Text = $"替换音量 {volume.Value:0}%"; controller.Engine.Touch(); }; panel.Children.Add(volume);
        panel.Children.Add(Ui.Label("应用与音频", 15));
        panel.Children.Add(Ui.Label("先选择Windows应用，再选择音频并启用。应用名精确匹配，忽略大小写和.exe；其他应用保持原通知音。", 12, Ui.Muted));
        panel.Children.Add(Ui.Button("刷新应用列表", async () => { await controller.Listening.RefreshApplications(); RefreshSection(); }));
        listeningRuleStatus.Clear();
        foreach (var rule in options.Rules.ToArray()) panel.Children.Add(ListeningRule(rule));
        panel.Children.Add(Ui.Button("＋ 添加应用", () =>
        { options.Rules.Add(new NotificationSoundRule { Enabled = false }); controller.Engine.Touch(); RefreshSection(); }));
        playbackStatus = Ui.Label(controller.Listening.PlaybackStatus, 12, Ui.Accent); panel.Children.Add(playbackStatus);
        panel.Children.Add(Ui.Button("停止试听 / 当前播放", controller.Listening.StopPlayback));
        panel.Children.Add(Ui.Label("近期捕获", 15));
        panel.Children.Add(Ui.Label("仅在本次运行中显示应用、时间和播放结果，不保存通知正文。多条提醒依次播放，避免叠音。", 11, Ui.Muted));
        listeningHistory = new StackPanel(); panel.Children.Add(listeningHistory);
        RefreshListeningStatus(); return panel;
    }
    private Border ListeningRule(NotificationSoundRule rule)
    {
        var body = new StackPanel();
        var switchRow = new DockPanel();
        var remove = Ui.Button("删除", async () => { controller.Engine.State.Preferences.Listening.Rules.Remove(rule); await SaveListening(); RefreshSection(); }); DockPanel.SetDock(remove, Dock.Right); switchRow.Children.Add(remove);
        var enabled = new CheckBox { Content = "此应用启用替换", IsChecked = rule.Enabled, VerticalAlignment = VerticalAlignment.Center };
        enabled.Click += async (_, _) => { rule.Enabled = enabled.IsChecked == true; await SaveListening(); }; switchRow.Children.Add(enabled); body.Children.Add(switchRow);
        var applications = controller.Listening.Applications.Concat(controller.Listening.SeenApps.Where(x => x.Id.Length > 0))
            .GroupBy(x => x.Id).Select(x => x.First()).ToArray();
        body.Children.Add(Ui.Label("Windows通知应用", 12, Ui.Muted));
        var picker = new ComboBox { ItemsSource = applications, DisplayMemberPath = "Name", Margin = new Thickness(0, 4, 0, 8), SelectedItem = applications.FirstOrDefault(x => x.Id == rule.AppId), ToolTip = "只列出已在Windows注册的通知发送方" };
        var identity = Ui.Label(rule.AppId.Length > 0 ? rule.AppId : "尚未关联Windows发送方", 10, Ui.Muted);
        var name = Ui.Input(rule.AppName); name.ToolTip = "横幅中的应用名；可填写该发送方的准确显示名";
        picker.SelectionChanged += async (_, _) =>
        {
            if (picker.SelectedItem is not NotificationApplication selected) return;
            var duplicate = controller.Engine.State.Preferences.Listening.Rules.Any(x => x.Id != rule.Id && x.AppId == selected.Id);
            if (duplicate) { identity.Text = "这个应用已有配置，请修改原条目"; picker.SelectedItem = applications.FirstOrDefault(x => x.Id == rule.AppId); return; }
            rule.AppId = selected.Id; rule.AppName = selected.Name; name.Text = selected.Name; identity.Text = selected.Id; await SaveListening();
        };
        body.Children.Add(picker); body.Children.Add(identity); body.Children.Add(Ui.Label("通知显示名（辅助功能匹配）", 12, Ui.Muted)); body.Children.Add(name);
        name.LostKeyboardFocus += async (_, _) =>
        {
            var value = name.Text.Trim(); if (value == rule.AppName) return;
            if (rule.AppId.Length == 0 && controller.Engine.State.Preferences.Listening.Rules.Any(x => x.Id != rule.Id && x.AppId.Length == 0 && NotificationSoundRules.SameName(x.AppName, value)))
            { name.Text = rule.AppName; identity.Text = "此应用名已配置，请修改原条目"; return; }
            rule.AppName = value; await SaveListening();
        };
        var path = Ui.Label(rule.AudioPath.Length > 0 ? rule.AudioPath : "还未选择音频", 11, Ui.Muted); body.Children.Add(path);
        var buttons = new WrapPanel();
        buttons.Children.Add(Ui.Button("选择音频", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择此应用的通知音", Filter = "音频文件|*.wav;*.mp3;*.m4a;*.aac;*.wma|所有文件|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            var error = await NotificationAudio.Validate(dialog.FileName); if (error is not null) { Ui.Error(error); return; }
            rule.AudioPath = dialog.FileName; path.Text = rule.AudioPath; await SaveListening();
        }));
        buttons.Children.Add(Ui.Button("试听", async () => await controller.Listening.Preview(rule.AudioPath))); body.Children.Add(buttons);
        var status = Ui.Label(controller.Listening.RuleStatus(rule.Id), 11, Ui.Accent); listeningRuleStatus[rule.Id] = status; body.Children.Add(status);
        body.Children.Add(Ui.Label("音频记录原文件位置，请保留文件。配置备份含应用规则及路径，不含音频文件。", 10, Ui.Muted));
        return Ui.Panel(body);
    }
}
