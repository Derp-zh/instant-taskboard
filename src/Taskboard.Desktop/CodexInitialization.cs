using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
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
    internal const string DefaultInspector = "http://127.0.0.1:9339";
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
            progress?.Invoke(new(CodexSetupState.Finding, "正在寻找 Codex", "正在核对系统安装登记、桌面程序和 CLI。"));
            var desktop = config.GetValueOrDefault(DesktopKey, ""); var cli = config.GetValueOrDefault(CliKey, "");
            var install = await environment.Discover(desktop, cli);
            bool Changed(string before, string after) => before.Length > 0 && !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
            bool CanRepair(string before, string after) => install.RegisteredDesktops.Contains(install.Desktop, StringComparer.OrdinalIgnoreCase) && CodexInstallationDiscovery.ReplacedStorePath(before, after);
            if (Changed(desktop, install.Desktop) && !CanRepair(desktop, install.Desktop) ||
                Changed(cli, install.Cli) && !CanRepair(cli, install.Cli))
                return Result(new(CodexSetupState.InvalidPath, "原路径未通过检查，配置已保留", "请使用“手动选择桌面程序／CLI”修复路径，再重新检查。", install.Diagnostic));
            if (Changed(desktop, install.Desktop)) config[DesktopKey + ".previous"] = desktop;
            if (Changed(cli, install.Cli)) config[CliKey + ".previous"] = cli;
            if (install.Desktop.Length == 0)
                return Result(new(CodexSetupState.NotFound, "未找到 Codex，请手动选择", "使用“手动选择桌面程序”，选择已安装 Codex 的桌面程序。", install.Diagnostic));
            config[DesktopKey] = install.Desktop;
            if (install.Cli.Length == 0)
                return Result(new(CodexSetupState.MissingCli, "未找到可用的 Codex CLI", "使用“手动选择 CLI”；CLI 是 codex.exe，和桌面程序不同。", install.Diagnostic));
            config[CliKey] = install.Cli;
            var inspector = Inspector(config);
            if (inspector.Length == 0) inspector = DefaultInspector;
            if (!CodexPendingBridge.ValidEndpoint(inspector, out _))
                return Result(new(CodexSetupState.Failed, "原观察连接地址无效，配置已保留", "请在诊断与连接配置中修复本机地址，再重新检查。"));
            config[InspectorKey] = inspector;
            progress?.Invoke(new(CodexSetupState.Checking, "正在检查连接", "分别检查聊天目录、原桌面身份及提问／批准观察能力。"));
            // Try the read-only transport first. Version numbers and executable
            // hashes do not decide whether a connection or protocol works.
            var endpoint = config.GetValueOrDefault("codex.endpoint", "");
            var check = await environment.Check(install.Cli, endpoint, inspector);
            var diagnostics = install.Diagnostic + "\n桌面版本：" + install.Version + "\n" + check.Reason;
            if (!check.Catalog)
                return Result(new(CodexSetupState.CatalogUnavailable, "聊天目录暂不可用，连接检查已执行", "请确认 Codex 已登录，再重新检查；若仍失败，展开诊断或手动重新选择 CLI。", diagnostics, false, false, check.Desktop));
            if (!check.Desktop || !check.Pending)
            {
                if (check.Reason.Contains("inspector-port-occupied", StringComparison.Ordinal))
                    return Result(new(CodexSetupState.ConnectionUnavailable, "聊天可选择，状态观察连接被占用", "请在诊断中更换本机观察端口后重试；不要中断正在使用的 Codex。", diagnostics, true, false, check.Desktop));
                if (check.Reason.Contains("unsupported-desktop-module", StringComparison.Ordinal))
                    return Result(new(CodexSetupState.Incompatible, "Codex 已连接，提问／批准观察暂不支持", "基础连接可继续使用；状态观察结构与现有适配不符，请更新看板。可点击“测试已关联聊天”查看当前实际能力。", diagnostics, true, false, check.Desktop));
                var running = await Task.Run(() => environment.DesktopRunning(install.Desktop));
                if (running && !check.Desktop)
                    return Result(new(CodexSetupState.ConnectionUnavailable, "聊天可选择，Codex 实时连接未建立", "请确认 Codex 主界面已打开且与看板使用相同的用户和权限，再重新检查。诊断中可查看连接失败原因；未据此判定版本不兼容。", diagnostics, true));
                if (check.Desktop && check.Reason.Contains("manager-", StringComparison.Ordinal))
                    return Result(new(CodexSetupState.ConnectionUnavailable, "Codex 已连接，状态观察尚未准备好", "请等待 Codex 主界面加载完成后重新检查；仍失败时展开诊断。无需退出正在执行的聊天。", diagnostics, true, false, true));
                if (running)
                    return Result(new(CodexSetupState.NeedsReopen, check.Desktop ? "Codex 已连接，完整状态观察待开启" : "聊天可选择，Codex 实时连接待开启", "可先测试已关联聊天。若需开启提问／批准观察，请在方便时自行退出 Codex，再点击“打开 Codex 并开启状态观察”。看板不会替你退出或重启。", diagnostics, true, false, check.Desktop));
                return Result(new(CodexSetupState.NeedsOpen, "聊天可选择，Codex 尚未运行", "打开 Codex 后重新检查，或点击“打开 Codex 并开启状态观察”，确认后由看板打开。", diagnostics, true));
            }
            return Result(new(CodexSetupState.Ready, "配置完成 · 连接已准备好", "请在任务阶段选择已有 Codex 聊天，再点击“测试已关联聊天”。具体聊天的提问／批准及实际恢复需在发生时分别验证。", diagnostics, true, true, true));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or System.Text.Json.JsonException)
        { return Result(new(CodexSetupState.Failed, "初始化未完成", "请重新检查；仍失败时展开诊断，修复路径或连接配置。", e.GetType().Name + ": " + e.Message)); }
    }
}

internal sealed class CodexSetupEnvironment : ICodexSetupEnvironment
{
    internal static async Task<string[]> TestConversations(string[] threads, string inspector)
    {
        await using var desktop = await CodexDesktopBridge.TryConnect(inspector: inspector).ConfigureAwait(false);
        if (desktop is null) return ["Codex 实时连接未建立，请打开 Codex 后重新检查；未判定版本不兼容。"];
        await using var pending = new CodexPendingBridge(inspector);
        var source = await pending.Probe(desktop.ServerProcessId).ConfigureAwait(false);
        var results = new List<string>();
        for (var index = 0; index < threads.Length; index++)
        {
            var snapshot = await desktop.Read(threads[index]).ConfigureAwait(false);
            var prefix = $"聊天 {index + 1}：";
            if (!snapshot.LiveCapable)
            { results.Add(prefix + "当前状态不可读，请在 Codex 中打开该聊天后重试。诊断：" + snapshot.Reason); continue; }
            var active = CodexState.Text(snapshot.Status, "type") == "active";
            if (!active)
            { results.Add(prefix + "实时可读，当前空闲；" + (source ? "状态观察源已准备好，等待与恢复尚未发生。" : "提问／批准观察尚未准备好。")); continue; }
            var exact = await pending.Read(threads[index], desktop.ServerProcessId, default).ConfigureAwait(false);
            var merged = CodexPendingBridge.Merge(snapshot.Status, snapshot.Turns, exact);
            var complete = CodexState.Object(merged, "pendingSourceVersion").ValueKind == System.Text.Json.JsonValueKind.Number;
            var flags = CodexState.Object(merged, "activeFlags");
            var values = flags.ValueKind == System.Text.Json.JsonValueKind.Array ? flags.EnumerateArray().Select(x => x.GetString()).ToArray() : [];
            var status = values.Contains("waitingOnApproval") ? "正在等待批准" : values.Contains("waitingOnUserInput") ? "正在等待回答" :
                complete && exact is { Executing: true, Requests.Length: 0, Overflow: false } ? "已确认正在执行" : "活动中，实际执行／等待仍待确认";
            results.Add(prefix + "实时可读，" + status + (complete ? "；本轮请求集合可读。" : "；提问／批准观察待完成（" + pending.Reason + "）。"));
        }
        return results.ToArray();
    }
    public Task<CodexInstallation> Discover(string desktop, string cli) => CodexInstallationDiscovery.Find(desktop, cli);
    public bool CanOpenDesktop(string desktop) => CodexInstallationDiscovery.TrustedDesktop(desktop);
    public async Task<CodexConnectionCheck> Check(string cli, string endpoint, string inspector)
    {
        var catalog = false; var reason = "";
        async Task<bool> Catalog(string address)
        {
            try
            {
                await using var rpc = await CodexRpc.Start(cli, false, address).ConfigureAwait(false);
                var result = await rpc.Call("thread/list", new { limit = 1, archived = false, useStateDbOnly = true }).ConfigureAwait(false);
                return CodexState.Object(result, "data").ValueKind == System.Text.Json.JsonValueKind.Array;
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or OperationCanceledException or System.Net.WebSockets.WebSocketException or System.ComponentModel.Win32Exception)
            { reason += "\ncatalog-unavailable-" + e.GetType().Name; return false; }
        }
        catalog = await Catalog(endpoint).ConfigureAwait(false);
        if (!catalog && endpoint.Length > 0) { catalog = await Catalog("").ConfigureAwait(false); reason += "\nexisting-service-preserved-local-catalog-fallback"; }
        await using var desktop = await CodexDesktopBridge.TryConnect(inspector: inspector).ConfigureAwait(false);
        if (desktop is null) return new(catalog, false, false, reason + "\n" + CodexDesktopBridge.DiscoveryReason);
        await using var pending = new CodexPendingBridge(inspector);
        var ready = await pending.Probe(desktop.ServerProcessId).ConfigureAwait(false);
        var pendingReason = pending.Reason;
        if (!ready && pendingReason == "inspector-peer-mismatch" && CodexPendingBridge.ValidEndpoint(inspector, out var address) &&
            IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == address!.Port)) pendingReason = "inspector-port-occupied";
        return new(catalog, true, ready, reason + "\n" + desktop.IdentityKind + "\n" + pendingReason);
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
        if (!CodexPendingBridge.ValidEndpoint(inspector, out var address)) throw new InvalidOperationException("观察连接地址无效。");
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == address!.Port))
            throw new InvalidOperationException("本机连接端口被占用，请关闭冲突程序或在诊断中更换端口后重试。");
        var start = new ProcessStartInfo(desktop) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(desktop)!, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--remote-debugging-address=127.0.0.1"); start.ArgumentList.Add("--remote-debugging-port=" + address!.Port);
        Process.Start(start)?.Dispose();
        return Task.CompletedTask;
    }
}
