using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal enum CodexSetupState { NotStarted, Finding, Checking, NotFound, InvalidPath, MissingCli, Incompatible, NeedsOpen, NeedsReopen, ConnectionUnavailable, CatalogUnavailable, ExistingService, Ready, Failed }
internal sealed record CodexSetupReport(CodexSetupState State, string Message, string Next, string Diagnostic = "", bool Catalog = false, bool SourceReady = false, bool DesktopConnected = false)
{
    internal static CodexSetupReport Initial => new(CodexSetupState.NotStarted, "尚未初始化", "点击一键初始化，自动寻找并检查 Codex。");
}
internal sealed record CodexConnectionCheck(bool Catalog, bool Desktop, bool Pending, string Reason);
internal sealed record CodexSetupOutcome(CodexSetupReport Report, Dictionary<string, string> Config);

internal interface ICodexSetupEnvironment
{
    Task<CodexInstallation> Discover(string desktop, string cli);
    bool CanOpenDesktop(string desktop);
    Task<CodexConnectionCheck> Check(string cli, string endpoint, string inspector);
    bool DesktopRunning(string desktop);
    Task OpenDesktop(string desktop, string inspector);
}

// No UI, chat binding, task mutation, or desktop lifecycle operation in this flow.
// OpenDesktop is a separate action invoked only after an explicit UI confirmation.
internal sealed class CodexInitialization(ICodexSetupEnvironment environment)
{
    internal const string InspectorKey = "codex.inspector";
    internal const string DesktopKey = "codex.desktop";
    internal const string CliKey = "codex.executable";
    internal const string PromptKey = "ai.initialization.prompt";
    internal static bool ShouldPrompt(Preferences preferences) => !preferences.ConnectionConfigs.ContainsKey(PromptKey);
    internal static void RememberPrompt(Preferences preferences, string choice) => preferences.ConnectionConfigs[PromptKey] = choice;
    internal static string Inspector(Dictionary<string, string> config) => config.GetValueOrDefault(InspectorKey,
        Environment.GetEnvironmentVariable(CodexPendingBridge.Setting) ?? "");

    internal async Task<CodexSetupOutcome> Run(Dictionary<string, string> original, Action<CodexSetupReport>? progress = null)
    {
        var config = new Dictionary<string, string>(original);
        CodexSetupOutcome Result(CodexSetupReport report) { progress?.Invoke(report); return new(report, config); }
        try
        {
            progress?.Invoke(new(CodexSetupState.Finding, "正在寻找 Codex", "正在检查本机已安装的 Codex。"));
            var desktop = config.GetValueOrDefault(DesktopKey, ""); var cli = config.GetValueOrDefault(CliKey, "");
            var install = await environment.Discover(desktop, cli);
            bool Changed(string before, string after) => before.Length > 0 && !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
            bool CanRepair(string before, string after) => install.RegisteredDesktops.Contains(install.Desktop, StringComparer.OrdinalIgnoreCase) && CodexInstallationDiscovery.ReplacedStorePath(before, after);
            if (Changed(desktop, install.Desktop) && !CanRepair(desktop, install.Desktop))
                return Result(new(CodexSetupState.InvalidPath, "需要修复 Codex 位置", "请在“连接诊断”中重新选择 Codex 桌面程序。原配置已保留。", install.Diagnostic));
            if (Changed(desktop, install.Desktop)) config[DesktopKey + ".previous"] = desktop;
            var useDiscoveredCli = cli.Length == 0 || !Changed(cli, install.Cli) || CanRepair(cli, install.Cli);
            if (Changed(cli, install.Cli) && useDiscoveredCli) config[CliKey + ".previous"] = cli;
            if (install.Desktop.Length == 0)
                return Result(new(CodexSetupState.NotFound, "未找到 Codex，请手动选择", "使用“手动选择桌面程序”，选择已安装 Codex 的桌面程序。", install.Diagnostic));
            config[DesktopKey] = install.Desktop;
            if (install.Cli.Length > 0 && useDiscoveredCli) config[CliKey] = install.Cli;
            var inspector = Inspector(config);
            // Preserve old observation/service values. Normal local state no
            // longer depends on their validity or on a debug launch parameter.
            progress?.Invoke(new(CodexSetupState.Checking, "正在检查连接", "分别检查聊天列表和本机连接，不操作聊天。"));
            // Try the read-only transport first. Version numbers and executable
            // hashes do not decide whether a connection or protocol works.
            var endpoint = config.GetValueOrDefault("codex.endpoint", "");
            var check = await environment.Check(install.Cli, endpoint, inspector);
            var diagnostics = install.Diagnostic + "\n桌面版本：" + install.Version + "\n" + check.Reason;
            if (!check.Catalog)
                return Result(new(CodexSetupState.CatalogUnavailable, "本地聊天目录暂不可用，连接检查已执行", "请正常打开 Codex 后重试；若仍失败，展开诊断检查本地数据目录。不会联网补取聊天。", diagnostics, false, false, check.Desktop));
            if (!check.Desktop || !check.Pending)
            {
                if (check.Desktop && check.Reason.Contains("local-transport-only", StringComparison.Ordinal))
                    return Result(new(CodexSetupState.Ready, "Codex 已连接", "在任务阶段关联已有聊天。提问、批准和恢复状态会在实际发生时分别确认。", diagnostics, true, false, true));
                if (check.Reason.Contains("unsupported-desktop-module", StringComparison.Ordinal))
                    return Result(new(CodexSetupState.Incompatible, "Codex 已连接，提问／批准观察暂不支持", "基础连接可继续使用；状态观察结构与现有适配不符，请更新看板。可点击“测试已关联聊天”查看当前实际能力。", diagnostics, true, false, check.Desktop));
                var running = await Task.Run(() => environment.DesktopRunning(install.Desktop));
                if (running && !check.Desktop)
                    return Result(new(CodexSetupState.ConnectionUnavailable, "聊天可选择，Codex 实时连接未建立", "请确认 Codex 主界面已打开且与看板使用相同的用户和权限，再重新检查。诊断中可查看连接失败原因；未据此判定版本不兼容。", diagnostics, true));
                if (check.Desktop && check.Reason.Contains("manager-", StringComparison.Ordinal))
                    return Result(new(CodexSetupState.ConnectionUnavailable, "Codex 已连接，状态观察尚未准备好", "请等待 Codex 主界面加载完成后重新检查；仍失败时展开诊断。无需退出正在执行的聊天。", diagnostics, true, false, true));
                if (running)
                    return Result(new(CodexSetupState.ConnectionUnavailable, "Codex 本地状态连接暂不可用", "看板会自动重连，也可点击检查连接。仍未恢复时，展开连接诊断查看原因。", diagnostics, true, false, check.Desktop));
                return Result(new(CodexSetupState.NeedsOpen, "聊天可选择，Codex 尚未运行", "正常打开 Codex 即可自动连接，也可点击“打开 Codex”。", diagnostics, true));
            }
            return Result(new(CodexSetupState.Ready, "配置完成 · 连接已准备好", "请在任务阶段选择已有 Codex 聊天，再点击“测试已关联聊天”。具体聊天的提问／批准及实际恢复需在发生时分别验证。", diagnostics, true, true, true));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or System.Text.Json.JsonException)
        { return Result(new(CodexSetupState.Failed, "初始化未完成", "请重新检查；仍失败时展开诊断，修复路径或连接配置。", e.GetType().Name + ": " + e.Message)); }
    }
}

internal sealed class CodexSetupEnvironment : ICodexSetupEnvironment
{
    // Describe only a snapshot already held by the normal observer. The settings
    // test must never create a connection, register a follower or reset idle time.
    internal static string DescribeSnapshot(CodexDesktopSnapshot snapshot, int index)
    {
        var prefix = $"聊天 {index + 1}：";
        if (!snapshot.LiveCapable) return prefix + "状态待确认，保留原有提醒；本次未额外连接聊天。";
        var type = CodexState.Text(snapshot.Status, "type");
        if (type == "idle") return prefix + "最近确认为空闲；提问、批准和恢复尚需分别验证。";
        if (type != "active") return prefix + "状态待确认，保留原有提醒。";
        var version = CodexState.Object(snapshot.Status, "pendingSourceVersion");
        var flags = CodexState.Object(snapshot.Status, "activeFlags");
        var flagsValid = flags.ValueKind == System.Text.Json.JsonValueKind.Array && flags.EnumerateArray().All(x => x.ValueKind == System.Text.Json.JsonValueKind.String);
        var values = flagsValid ? flags.EnumerateArray().Select(x => x.GetString()).ToArray() : [];
        var requests = CodexState.Object(snapshot.Status, "pendingRequests");
        var overflow = CodexState.Object(snapshot.Status, "pendingOverflow");
        var requestSet = requests.ValueKind == System.Text.Json.JsonValueKind.Array ? requests.EnumerateArray().ToArray() : [];
        var running = snapshot.Turns.Where(x => x.Status == "inProgress" && x.Id.Length > 0).Select(x => x.Id).Distinct(StringComparer.Ordinal).ToArray();
        var complete = version.ValueKind == System.Text.Json.JsonValueKind.Number && version.TryGetInt32(out var value) && value == 1 &&
            flagsValid && running.Length == 1 && CodexState.Text(snapshot.Status, "pendingTurnId") == running[0] &&
            requests.ValueKind == System.Text.Json.JsonValueKind.Array && requestSet.Length <= 128 &&
            overflow.ValueKind == System.Text.Json.JsonValueKind.False && requestSet.All(x => CodexState.Text(x, "id").Length is > 0 and <= 256 &&
                CodexState.Object(x, "approval").ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False) &&
            requestSet.Select(x => CodexState.Text(x, "id")).Distinct(StringComparer.Ordinal).Count() == requestSet.Length;
        var status = values.Contains("waitingOnApproval") ? "最近确认在等待批准" : values.Contains("waitingOnUserInput") ? "最近确认在等待回答" :
            complete && values.Length == 0 && requestSet.Length == 0 ? "最近确认正在执行" : "执行或等待状态仍待确认";
        return prefix + status + (complete ? "；本轮请求信息可读。" : "；提问／批准信息尚未确认完整。");
    }
    public Task<CodexInstallation> Discover(string desktop, string cli) => CodexInstallationDiscovery.Find(desktop, cli, inspectCli: false);
    public bool CanOpenDesktop(string desktop) => CodexInstallationDiscovery.TrustedDesktop(desktop);
    public async Task<CodexConnectionCheck> Check(string cli, string endpoint, string inspector)
    {
        var catalog = false; var reason = "";
        async Task<bool> Catalog()
        {
            try
            {
                _ = await CodexLocalCatalog.Load().ConfigureAwait(false);
                return true;
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or OperationCanceledException or System.Net.WebSockets.WebSocketException or System.ComponentModel.Win32Exception)
            { reason += "\ncatalog-unavailable-" + e.GetType().Name; return false; }
        }
        catalog = await Catalog().ConfigureAwait(false);
        reason += "\nlocal-sqlite-catalog; no-app-server; existing-endpoint-preserved-unused" +
            "\n当前准确监测仍使用本机订阅，会影响相关聊天的闲置释放；完全被动监测尚未实现。";
        try
        {
            await using var source = await CodexLocalEvents.Connect().ConfigureAwait(false);
            return new(catalog, source.Connected, false, reason + "\n" + source.IdentityKind + "\nlocal-transport-only; no-chat-selected-or-queried");
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
        { return new(catalog, false, false, reason + "\nlocal-state-unavailable-" + e.GetType().Name); }
    }
    public bool DesktopRunning(string desktop)
    {
        // Conservative: if a same-named process cannot be inspected, don't launch another.
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(desktop)))
            using (process) if (!process.HasExited) return true;
        return false;
    }
    public Task OpenDesktop(string desktop, string inspector)
    {
        if (!CanOpenDesktop(desktop)) throw new InvalidOperationException("所选程序未通过 Codex 系统登记与签名检查，请重新选择已安装的 Codex。");
        if (DesktopRunning(desktop)) throw new InvalidOperationException("Codex 仍在运行。请先保存工作并自行退出，再重试；看板不会关闭它。");
        var start = new ProcessStartInfo(desktop) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(desktop)!, WindowStyle = ProcessWindowStyle.Hidden };
        Process.Start(start)?.Dispose();
        return Task.CompletedTask;
    }
}
