using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private readonly SemaphoreSlim codexRestartGate = new(1, 1);
    internal CodexSetupReport AiSetupReport { get; private set; } = CodexSetupReport.Initial;
    internal bool AiSetupBusy { get; private set; }
    internal string AiConversationTest { get; private set; } = "尚未测试已关联聊天。测试只读取当前状态，不发送消息。";
    private bool CodexSetupScopeValid(object state) => ReferenceEquals(state, Engine.State) && !exiting && !dataTransition;
    internal string AiConversationVerification
    {
        get
        {
            var count = Engine.State.Tasks.SelectMany(x => x.Stages).Where(x => x.Codex is not null).Select(x => x.Codex!.ThreadId).Distinct().Count();
            return count == 0 ? "具体聊天尚未验证；请在任务阶段选择已有聊天。" :
                $"已关联 {count} 个聊天，实时可读 {Codex.Capabilities.Count(x => x.Live)} 个；本次提问／批准观察可用 {Codex.Capabilities.Count(x => x.PendingAvailable)} 个。未发生的等待和恢复不算已验证。";
        }
    }

    private void SetAiSetupReport(CodexSetupReport report)
    { AiSetupReport = report; settings?.RefreshAiInitialization(); }

    internal async Task TestAiConversations(Func<string[], string, Task<string[]>>? inspect = null)
    {
        if (AiSetupBusy || dataTransition || exiting) return;
        var state = Engine.State;
        var bindings = state.Tasks.SelectMany(task => task.Stages.Where(stage => stage.Codex is not null)
            .Select(stage => (task.Id, Stage: stage.Id, stage.Codex!.BindingId, stage.Codex.Generation, stage.Codex.ThreadId))).ToArray();
        if (bindings.Length == 0)
        { AiConversationTest = "尚未关联聊天。请先在任务阶段选择已有 Codex 聊天，再测试；不会为验证自动创建或选择聊天。"; settings?.RefreshAiInitialization(); return; }
        var inspector = CodexInitialization.Inspector(state.Preferences.ConnectionConfigs);
        var originalConfig = new Dictionary<string, string>(state.Preferences.ConnectionConfigs);
        var threadIds = bindings.Select(x => x.ThreadId).Distinct().ToArray();
        var labels = threadIds.Select(id => string.Join("、", state.Tasks.SelectMany(task => task.Stages
            .Where(stage => stage.Codex?.ThreadId == id).Select(stage => task.Title + " / " + stage.Name)))).ToArray();
        AiSetupBusy = true; AiConversationTest = "正在只读测试已关联聊天…"; settings?.RefreshAiInitialization();
        try
        {
            if (Isolated && inspect is null) { AiConversationTest = "隔离模式不连接原 Codex，请使用合成检查。"; return; }
            var results = await (inspect ?? CodexSetupEnvironment.TestConversations)(threadIds, inspector);
            var current = state.Tasks.SelectMany(task => task.Stages.Where(stage => stage.Codex is not null)
                .Select(stage => (task.Id, Stage: stage.Id, stage.Codex!.BindingId, stage.Codex.Generation, stage.Codex.ThreadId))).ToArray();
            if (!CodexSetupScopeValid(state) || !bindings.SequenceEqual(current) ||
                new[] { CodexInitialization.DesktopKey, CodexInitialization.CliKey, CodexInitialization.InspectorKey, "codex.endpoint" }
                    .Any(key => originalConfig.GetValueOrDefault(key) != state.Preferences.ConnectionConfigs.GetValueOrDefault(key)))
            { AiConversationTest = "聊天关联或配置已变化，请重新测试。"; return; }
            AiConversationTest = "本次只读测试（" + DateTime.Now.ToString("HH:mm:ss") + "）\n" + string.Join("\n", results.Select((value, index) =>
                results.Length == threadIds.Length ? labels[index] + "\n" + value : value)) +
                "\n提问、批准及实际恢复的完整过程，需在正常使用中分别观察；本次没有发送测试消息，也没有修改任务状态。";
        }
        catch (Exception e) when (e is System.IO.IOException or InvalidOperationException or TimeoutException or OperationCanceledException)
        { AiConversationTest = "本次测试未完成，请重新检查连接后再试。诊断：" + e.GetType().Name; }
        finally { AiSetupBusy = false; settings?.RefreshAiInitialization(); }
    }

    internal async Task InitializeAiPlatform(ICodexSetupEnvironment? environment = null)
    {
        if (AiSetupBusy || dataTransition || exiting) return;
        AiSetupBusy = true;
        var state = Engine.State;
        var original = new Dictionary<string, string>(state.Preferences.ConnectionConfigs);
        try
        {
            var service = new CodexInitialization(environment ?? new CodexSetupEnvironment());
            var result = await service.Run(original, report =>
            {
                if (CodexSetupScopeValid(state)) SetAiSetupReport(report.SourceReady
                    ? new(CodexSetupState.Checking, "连接检查通过，正在保存配置", "保存成功后才会显示配置完成。") : report);
            });
            if (!CodexSetupScopeValid(state)) return;
            // Do not overwrite edits made while the asynchronous check was running.
            foreach (var key in new[] { CodexInitialization.DesktopKey, CodexInitialization.CliKey, CodexInitialization.InspectorKey, "codex.endpoint" })
                if (state.Preferences.ConnectionConfigs.GetValueOrDefault(key) != original.GetValueOrDefault(key))
                { SetAiSetupReport(new(CodexSetupState.Failed, "配置在检查期间已变化", "请重新检查，采用最新配置。")); return; }
            foreach (var (key, value) in result.Config)
                if (!original.TryGetValue(key, out var before) || before != value) state.Preferences.ConnectionConfigs[key] = value;
            CodexInitialization.RememberPrompt(state.Preferences, "attempted");
            Engine.Touch();
            if (!await Flush()) { SetAiSetupReport(new(CodexSetupState.Failed, "配置未能保存", "请检查数据目录是否可写，再重新初始化。")); return; }
            // Test environments never contact the user's Codex or spawn a real CLI.
            if (environment is null) await RestartCodex();
            SetAiSetupReport(result.Report);
        }
        finally { AiSetupBusy = false; settings?.RefreshAiInitialization(); }
    }

    internal async Task OpenCodexForInitialization()
    {
        if (AiSetupBusy || dataTransition || exiting) return;
        var state = Engine.State;
        var config = state.Preferences.ConnectionConfigs;
        var desktop = config.GetValueOrDefault(CodexInitialization.DesktopKey, "");
        var inspector = CodexInitialization.Inspector(config);
        var environment = new CodexSetupEnvironment();
        if (environment.DesktopRunning(desktop))
        { SetAiSetupReport(new(CodexSetupState.NeedsReopen, "Codex 仍在运行，状态观察待开启", "请在方便时保存工作并自行退出 Codex，再点击此按钮。看板不会关闭任何 Codex 进程。")); return; }
        if (!await Ui.Confirm("将打开 Codex 并启用本机只读观察连接，用于识别提问、批准和实际恢复。不会自动选择聊天、发送消息或批准操作。是否打开？", "打开 Codex")) return;
        if (!CodexSetupScopeValid(state)) return;
        try
        {
            AiSetupBusy = true; settings?.RefreshAiInitialization();
            await Task.Run(() => environment.OpenDesktop(desktop, inspector));
            SetAiSetupReport(new(CodexSetupState.Checking, "正在等待 Codex 连接", "Codex 打开后将自动重新检查实际能力。"));
            // Bounded and asynchronous; never blocks the desktop or restarts it.
            for (var attempt = 0; attempt < 15 && CodexSetupScopeValid(state); attempt++)
            {
                await Task.Delay(1000);
                if (CodexPendingBridge.ValidEndpoint(inspector, out var address) &&
                    System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == address!.Port)) break;
            }
        }
        catch (Exception e)
        { SetAiSetupReport(new(CodexSetupState.Failed, "未能打开 Codex", "请检查诊断并重新检查。", e.Message)); return; }
        finally { AiSetupBusy = false; settings?.RefreshAiInitialization(); }
        await InitializeAiPlatform();
    }

    internal async Task<SurfaceWindow?> PromptAiInitialization(bool show = true)
    {
        if (!CodexInitialization.ShouldPrompt(Engine.State.Preferences)) return null;
        var state = Engine.State;
        CodexInitialization.RememberPrompt(state.Preferences, "shown"); Engine.Touch();
        if (!await Flush() || !CodexSetupScopeValid(state)) return null;
        var window = new SurfaceWindow(440, 260) { Title = "初始化 AI 平台配置", SuppressActivation = Smoke };
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(Ui.Label("初始化 AI 平台配置", 18));
        body.Children.Add(Ui.Label("关联 Codex 聊天后，看板可以根据执行状态自动移列。"));
        body.Children.Add(Ui.Label("目前仅支持 Codex。稍后也可从设置进入，不影响普通看板使用。", 12, Ui.Muted));
        var initialize = Ui.Button("一键初始化", async () =>
        {
            window.Destroy();
            if (!CodexSetupScopeValid(state)) return;
            Settings(); settings!.SelectSection("AI平台接入情况"); await InitializeAiPlatform();
        }, true); initialize.Tag = "AiInitializeNow"; body.Children.Add(initialize);
        var later = Ui.Button("稍后配置", async () =>
        {
            window.Destroy(); if (!CodexSetupScopeValid(state)) return;
            CodexInitialization.RememberPrompt(state.Preferences, "deferred"); Engine.Touch(); await Flush();
        }); later.Tag = "AiInitializeLater"; body.Children.Add(later);
        window.Root.Children.Add(body); RegisterAuxiliary(window); Clamp(window);
        var area = WorkArea(Ball); window.Left = area.Left + (area.Width - window.Width) / 2; window.Top = area.Top + (area.Height - window.Height) / 2;
        if (show) { window.Show(); window.Activate(); Native.RaiseBall(); }
        return window;
    }
}
