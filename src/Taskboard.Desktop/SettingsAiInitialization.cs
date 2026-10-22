using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    private TextBlock? aiSetupTitle, aiSetupNext, aiSetupDiagnostics, aiSetupVerification, aiSetupPaths, aiSetupCapabilities, aiTestResult;
    private Button? aiInitialize, aiOpenCodex, aiTest;
    private Expander? aiDiagnostics;
    private readonly List<Control> aiConfigurationControls = new();
    private bool updatingAiFields;
    internal void RefreshAiInitialization()
    {
        if (aiSetupTitle is null) return;
        var report = controller.AiSetupReport;
        aiSetupTitle.Text = report.Message;
        aiSetupNext!.Text = report.Next;
        var desktopConnected = controller.Codex.TransportConnected && controller.Codex.SourceIdentityVerified;
        var connected = report.SourceReady && desktopConnected;
        aiSetupTitle.Foreground = connected ? Ui.Accent : report.State is CodexSetupState.NotStarted or CodexSetupState.Finding or CodexSetupState.Checking ? Ui.Text : Ui.Brush("#F2C879");
        if (report.SourceReady && !connected)
        {
            aiSetupTitle.Text = "配置已保存，当前连接待确认";
            aiSetupNext.Text = "请重新检查当前连接；上次检查结果不代表本次仍然连通。";
        }
        aiSetupDiagnostics!.Text = (report.Diagnostic.Length == 0 ? "尚无本次检查结果。" : report.Diagnostic) +
            $"\n聊天目录：{(report.Catalog ? "本次检查可用" : "尚未通过本次检查")}\n完整观察源：{(report.SourceReady ? "本次检查已准备好" : "待完成")}\n当前连接：{controller.CodexStatus}";
        aiSetupVerification!.Text = controller.AiConversationVerification;
        aiSetupCapabilities!.Text = $"聊天搜索与选择：{(report.Catalog ? "本次检查可用" : "待检查")}\nCodex 实时连接：{(report.DesktopConnected ? desktopConnected ? "已连接" : "检查通过，当前连接待确认" : "待检查")}\n提问／批准状态观察：{(report.SourceReady ? connected ? "已准备好" : "检查通过，当前连接待确认" : "待完成")}";
        aiTestResult!.Text = controller.AiConversationTest;
        var config = controller.Engine.State.Preferences.ConnectionConfigs;
        aiSetupPaths!.Text = "桌面程序：" + config.GetValueOrDefault(CodexInitialization.DesktopKey, "尚未发现") +
            "\nCLI（codex.exe）：" + config.GetValueOrDefault(CodexInitialization.CliKey, "尚未发现");
        aiInitialize!.Content = report.State == CodexSetupState.NotStarted ? "一键初始化" : "重新检查";
        aiInitialize.IsEnabled = !controller.AiSetupBusy;
        aiTest!.IsEnabled = !controller.AiSetupBusy;
        aiOpenCodex!.Visibility = report.State is CodexSetupState.NeedsOpen or CodexSetupState.NeedsReopen ? Visibility.Visible : Visibility.Collapsed;
        aiOpenCodex.IsEnabled = !controller.AiSetupBusy;
        foreach (var control in aiConfigurationControls) control.IsEnabled = !controller.AiSetupBusy;
        if (report.State is CodexSetupState.NotFound or CodexSetupState.InvalidPath or CodexSetupState.MissingCli) aiDiagnostics!.IsExpanded = true;
        updatingAiFields = true;
        try
        {
            foreach (var control in aiConfigurationControls)
                if (control is TextBox { Tag: string key } input && !input.IsKeyboardFocusWithin)
                {
                    var value = config.GetValueOrDefault(key, "");
                    if (input.Text != value) input.Text = value;
                }
        }
        finally { updatingAiFields = false; }
    }

    private StackPanel AiInitializationSettings()
    {
        aiConfigurationControls.Clear();
        var body = new StackPanel();
        body.Children.Add(Ui.Label("初始化AI平台配置", 16));
        body.Children.Add(Ui.Label("关联 Codex 聊天后，看板可以根据执行状态自动移列。", 12, Ui.Muted));
        body.Children.Add(Ui.Label("目前仅支持 Codex。初始化不会选择或创建聊天，也不会发送消息、回答问题或批准操作。", 12, Ui.Muted));
        aiSetupTitle = Ui.Label("", 14, Ui.Accent); aiSetupTitle.Tag = "AiInitializationResult"; body.Children.Add(aiSetupTitle);
        aiSetupNext = Ui.Label("", 12); body.Children.Add(aiSetupNext);
        aiSetupCapabilities = Ui.Label("", 12); aiSetupCapabilities.Tag = "AiCapabilities"; body.Children.Add(aiSetupCapabilities);
        aiInitialize = Ui.Button("一键初始化", async () => await controller.InitializeAiPlatform(), true); aiInitialize.Tag = "AiInitialize"; body.Children.Add(aiInitialize);
        aiTest = Ui.Button("测试已关联聊天", async () => await controller.TestAiConversations()); aiTest.Tag = "AiTestConversations"; body.Children.Add(aiTest);
        aiTestResult = Ui.Label("", 12, Ui.Muted); aiTestResult.Tag = "AiTestResult"; body.Children.Add(aiTestResult);
        aiOpenCodex = Ui.Button("打开 Codex 并开启状态观察", async () => await controller.OpenCodexForInitialization()); aiOpenCodex.Tag = "AiOpenCodex"; body.Children.Add(aiOpenCodex);
        aiSetupVerification = Ui.Label("", 12, Ui.Muted); aiSetupVerification.Tag = "AiConversationVerification"; body.Children.Add(aiSetupVerification);
        var diagnostics = new StackPanel(); aiSetupDiagnostics = Ui.Label("", 11, Ui.Muted); diagnostics.Children.Add(aiSetupDiagnostics);
        aiSetupPaths = Ui.Label("", 11, Ui.Muted); diagnostics.Children.Add(aiSetupPaths);
        AddManualPath(diagnostics, "手动选择桌面程序", CodexInitialization.DesktopKey, "选择 Codex 桌面程序（商店版通常是 app 下的 ChatGPT.exe）");
        AddManualPath(diagnostics, "手动选择 CLI", CodexInitialization.CliKey, "选择 Codex CLI（resources 下的 codex.exe；不是桌面启动辅助程序）");
        foreach (var (key, caption) in new[] { (CodexInitialization.DesktopKey, "桌面程序路径"), (CodexInitialization.CliKey, "CLI 路径"), ("codex.endpoint", "原有本机服务地址（通常留空）"), (CodexInitialization.InspectorKey, "本机观察地址（自动配置，无需手填）") })
        {
            diagnostics.Children.Add(Ui.Label(caption, 12, Ui.Muted));
            var input = Ui.Input(controller.Engine.State.Preferences.ConnectionConfigs.GetValueOrDefault(key, "")); input.Tag = key;
            input.TextChanged += (_, _) => { if (updatingAiFields) return; controller.Engine.State.Preferences.ConnectionConfigs[key] = input.Text.Trim(); controller.Engine.Touch(); };
            aiConfigurationControls.Add(input); diagnostics.Children.Add(input);
        }
        aiDiagnostics = new Expander { Header = "手动选择与诊断（可选）", Content = diagnostics, Foreground = Ui.Muted, Margin = new Thickness(0, 8, 0, 8) };
        body.Children.Add(aiDiagnostics);
        RefreshAiInitialization(); return body;
    }

    private void AddManualPath(Panel body, string caption, string key, string title)
    {
        var button = Ui.Button(caption, async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Windows 程序|*.exe", Title = title, CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            if (key == CodexInitialization.CliKey && !await CodexInstallationDiscovery.IsCli(dialog.FileName))
            { Ui.Error("选择的程序未通过 CLI 检查，请选择可用的 codex.exe CLI。原配置已保留。"); return; }
            controller.Engine.State.Preferences.ConnectionConfigs[key] = dialog.FileName; controller.Engine.Touch();
            if (!await controller.Flush()) return;
            RefreshSection(); await controller.InitializeAiPlatform();
        }); button.Tag = key == CodexInitialization.CliKey ? "AiChooseCli" : "AiChooseDesktop";
        aiConfigurationControls.Add(button); body.Children.Add(button);
    }
}
