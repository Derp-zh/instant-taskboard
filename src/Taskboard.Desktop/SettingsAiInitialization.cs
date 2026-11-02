using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    private TextBlock? aiSetupTitle, aiSetupNext, aiSetupDiagnostics, aiSetupVerification, aiSetupPaths, aiSetupCapabilities, aiTestResult, aiTestSummary;
    private Button? aiInitialize, aiOpenCodex, aiTest;
    private Expander? aiDiagnostics, aiTestDetails;
    private readonly List<Control> aiConfigurationControls = new();
    private bool updatingAiFields;
    internal void RefreshAiInitialization()
    {
        if (aiSetupTitle is null) return;
        var report = controller.AiSetupReport;
        var desktopConnected = controller.Codex.TransportConnected && controller.Codex.SourceIdentityVerified;
        var ids = controller.Engine.State.Tasks.SelectMany(x => x.Stages).Where(x => x.Codex is not null)
            .Select(x => x.Codex!.ThreadId).Distinct(StringComparer.Ordinal).ToArray();
        var capabilities = controller.Codex.Capabilities.Where(x => ids.Contains(x.ThreadId, StringComparer.Ordinal)).ToArray();
        var live = desktopConnected ? capabilities.Count(x => x.Live) : 0;
        var pending = desktopConnected ? capabilities.Count(x => x.Live && x.PendingAvailable) : 0;
        var checking = controller.AiSetupBusy || report.State is CodexSetupState.Finding or CodexSetupState.Checking;
        var failed = report.State is CodexSetupState.NotFound or CodexSetupState.InvalidPath or CodexSetupState.MissingCli or CodexSetupState.Incompatible or
            CodexSetupState.ConnectionUnavailable or CodexSetupState.CatalogUnavailable or CodexSetupState.Failed;
        aiSetupTitle.Text = checking ? "正在检查连接…" : failed ? report.Message : desktopConnected ? "本机连接已建立" : report.State == CodexSetupState.NotStarted ? "尚未检查连接" : report.SourceReady ? "连接暂时中断" : report.Message;
        aiSetupTitle.Foreground = checking || report.State == CodexSetupState.NotStarted ? Ui.Text : failed ? Ui.Brush("#F2C879") : desktopConnected ? Ui.Accent : Ui.Brush("#F2C879");
        aiSetupNext!.Text = failed ? report.Next : desktopConnected
            ? ids.Length == 0 ? "下一步：在任务阶段关联一个已有 Codex 聊天。" : "聊天状态无法确认时，会保留原有等待提醒。"
            : report.State == CodexSetupState.NotStarted ? "正常打开 Codex 后，点击检查连接。" : report.Next;
        if (!failed && report.SourceReady && !desktopConnected)
        {
            aiSetupNext.Text = "正在自动重连，也可点击检查连接。";
        }
        aiSetupDiagnostics!.Text = (report.Diagnostic.Length == 0 ? "尚无本次检查记录。" : report.Diagnostic) +
            "\n当前状态：" + controller.CodexStatus + "\n读取方式：" + controller.Codex.ReadMode +
            "\n连接成功不代表所有聊天均可观察。提问、批准及实际恢复应在对应聊天发生时分别验证。";
        aiSetupVerification!.Text = controller.AiConversationVerification;
        aiSetupCapabilities!.Text = $"聊天列表　{(report.State == CodexSetupState.CatalogUnavailable ? "暂不可用" : report.Catalog ? "本次检查可用" : "待检查")}\n本机连接　{(desktopConnected ? "已连接" : "待连接")}\n关联聊天　" +
            (ids.Length == 0 ? "尚未关联" : $"{live}/{ids.Length} 状态可读 · {pending}/{ids.Length} 提问／批准可读");
        aiTestResult!.Text = controller.AiConversationTest;
        var hasTest = !controller.AiConversationTest.StartsWith("尚未测试", StringComparison.Ordinal);
        aiTestDetails!.Visibility = hasTest ? Visibility.Visible : Visibility.Collapsed;
        var firstLine = controller.AiConversationTest.Split('\n')[0].Trim();
        var sentenceEnd = firstLine.IndexOf('。');
        aiTestSummary!.Text = (sentenceEnd >= 0 ? firstLine[..(sentenceEnd + 1)] : firstLine) + " · 查看详情";
        var config = controller.Engine.State.Preferences.ConnectionConfigs;
        aiSetupPaths!.Text = "系统会自动寻找 Codex。只有找不到或路径失效时，才需要手动选择。";
        aiInitialize!.Content = checking ? "检查中…" : "检查连接";
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
        aiSetupTitle = Ui.Label("", 14); aiSetupTitle.Tag = "AiInitializationResult"; aiSetupTitle.Margin = new Thickness(0, 8, 0, 3); body.Children.Add(aiSetupTitle);
        aiSetupNext = Ui.Label("", 12); aiSetupNext.Tag = "AiNextStep"; body.Children.Add(aiSetupNext);
        aiSetupCapabilities = Ui.Label("", 12, Ui.Muted); aiSetupCapabilities.Tag = "AiCapabilities"; aiSetupCapabilities.LineHeight = 24; aiSetupCapabilities.Margin = new Thickness(0, 8, 0, 10); body.Children.Add(aiSetupCapabilities);
        var actions = new WrapPanel { Margin = new Thickness(-3, 0, -3, 4) };
        aiInitialize = Ui.Button("检查连接", async () => await controller.InitializeAiPlatform(), true); aiInitialize.Tag = "AiInitialize"; actions.Children.Add(aiInitialize);
        aiTest = Ui.Button("测试已关联聊天", async () => await controller.TestAiConversations()); aiTest.Tag = "AiTestConversations"; actions.Children.Add(aiTest);
        aiTest.ToolTip = "仅查看已有监测结果，不额外连接聊天，不发送消息、回答问题或批准操作。";
        aiOpenCodex = Ui.Button("打开 Codex", async () => await controller.OpenCodexForInitialization()); aiOpenCodex.Tag = "AiOpenCodex"; actions.Children.Add(aiOpenCodex);
        body.Children.Add(actions);
        aiTestResult = Ui.Label("", 11, Ui.Muted); aiTestResult.Tag = "AiTestResult"; aiTestResult.LineHeight = 18;
        aiTestSummary = Ui.Label("", 11, Ui.Muted); aiTestSummary.Tag = "AiTestSummary"; aiTestSummary.TextWrapping = TextWrapping.NoWrap;
        aiTestSummary.TextTrimming = TextTrimming.CharacterEllipsis; aiTestSummary.MaxWidth = 440;
        aiTestDetails = SettingsDisclosure("", aiTestResult); aiTestDetails.Tag = "AiTestDetails"; aiTestDetails.Header = aiTestSummary;
        body.Children.Add(aiTestDetails);
        body.Children.Add(Ui.Label("仅查看已有信息，不代答、不批准。", 11, Ui.Muted));
        var diagnostics = new StackPanel(); aiSetupDiagnostics = Ui.Label("", 11, Ui.Muted); diagnostics.Children.Add(aiSetupDiagnostics);
        aiSetupVerification = Ui.Label("", 12, Ui.Muted); aiSetupVerification.Tag = "AiConversationVerification"; diagnostics.Children.Add(aiSetupVerification);
        aiSetupPaths = Ui.Label("", 11, Ui.Muted); diagnostics.Children.Add(aiSetupPaths);
        AddManualPath(diagnostics, "手动选择桌面程序", CodexInitialization.DesktopKey, "选择 Codex 桌面程序（商店版通常是 app 下的 ChatGPT.exe）");
        void AddPath(Panel destination, string key, string caption)
        {
            destination.Children.Add(Ui.Label(caption, 12, Ui.Muted));
            var input = Ui.Input(controller.Engine.State.Preferences.ConnectionConfigs.GetValueOrDefault(key, "")); input.Tag = key;
            input.TextChanged += (_, _) => { if (updatingAiFields) return; controller.Engine.State.Preferences.ConnectionConfigs[key] = input.Text.Trim(); controller.Engine.Touch(); };
            aiConfigurationControls.Add(input); destination.Children.Add(input);
        }
        AddPath(diagnostics, CodexInitialization.DesktopKey, "桌面程序路径");
        var compatibility = new StackPanel(); compatibility.Children.Add(Ui.Label("仅用于兼容诊断，普通连接无需填写。", 11, Ui.Muted));
        AddManualPath(compatibility, "手动选择 CLI", CodexInitialization.CliKey, "选择 Codex CLI（resources 下的 codex.exe）");
        AddPath(compatibility, CodexInitialization.CliKey, "CLI 路径（可选）");
        diagnostics.Children.Add(SettingsDisclosure("兼容工具（可选）", compatibility));
        aiDiagnostics = SettingsDisclosure("手动选择与诊断（可选）", diagnostics); aiDiagnostics.Tag = "AiDiagnostics";
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
