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
internal sealed record CodexSetupReport(CodexSetupState State, string Message, string Next, string Diagnostic = "", bool Catalog = false, bool SourceReady = false)
{
    internal static CodexSetupReport Initial => new(CodexSetupState.NotStarted, "尚未初始化", "点击一键初始化，自动寻找并检查 Codex。");
}
internal sealed record CodexConnectionCheck(bool Catalog, bool Desktop, bool Pending, string Reason);
internal sealed record CodexSetupOutcome(CodexSetupReport Report, Dictionary<string, string> Config);

internal interface ICodexSetupEnvironment
{
    Task<CodexInstallation> Discover(string desktop, string cli);
    bool Compatible(string desktop);
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
            if (desktop.Length > 0 && !string.Equals(desktop, install.Desktop, StringComparison.OrdinalIgnoreCase) ||
                cli.Length > 0 && !string.Equals(cli, install.Cli, StringComparison.OrdinalIgnoreCase))
                return Result(new(CodexSetupState.InvalidPath, "原路径未通过检查，配置已保留", "请使用“手动选择桌面程序／CLI”修复路径，再重新检查。", install.Diagnostic));
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
            // Compatibility does not depend on being able to list history.
            if (!await Task.Run(() => environment.Compatible(install.Desktop)))
                return Result(new(CodexSetupState.Incompatible, "当前版本暂不兼容", "请等待看板的兼容更新；若选错程序，请手动选择后重试。普通看板可继续使用。",
                    install.Diagnostic + " 桌面版本：" + install.Version + "；未向未知版本发送内部观察表达式。"));
            var endpoint = config.GetValueOrDefault("codex.endpoint", "");
            var check = await environment.Check(install.Cli, endpoint, inspector);
            var diagnostics = install.Diagnostic + "\n桌面版本：" + install.Version + "\n" + check.Reason;
            if (!check.Catalog)
                return Result(new(CodexSetupState.CatalogUnavailable, "聊天目录尚不可用", "请确认 Codex 已安装并登录，或重新选择 CLI；已有服务地址也可在诊断中检查。", diagnostics));
            if (endpoint.Length > 0)
                return Result(new(CodexSetupState.ExistingService, "原服务配置已保留，完整监测待完成", "点击“改用原 Codex 桌面连接”后重新检查，或继续保留原服务。", diagnostics, true));
            if (!check.Desktop || !check.Pending)
            {
                if (check.Reason.Contains("inspector-port-occupied", StringComparison.Ordinal))
                    return Result(new(CodexSetupState.ConnectionUnavailable, "本机连接端口被其他程序占用", "请在诊断中更换本机观察端口，或关闭冲突程序后重新检查；不要中断正在使用的 Codex。", diagnostics, true));
                if (check.Reason.Contains("unsupported-desktop-module", StringComparison.Ordinal))
                    return Result(new(CodexSetupState.Incompatible, "当前版本暂不兼容", "桌面模块校验未通过，请等待兼容更新。", diagnostics, true));
                var running = await Task.Run(() => environment.DesktopRunning(install.Desktop));
                if (running)
                    return Result(new(CodexSetupState.NeedsReopen, "需要重新打开 Codex · 待完成", "请在方便时保存工作并自行退出 Codex，再点击“打开 Codex 并完成配置”。看板不会关闭正在运行的 Codex。", diagnostics, true));
                return Result(new(CodexSetupState.NeedsOpen, "Codex 未运行 · 待完成", "点击“打开 Codex 并完成配置”，确认后由看板带所需连接参数打开。", diagnostics, true));
            }
            return Result(new(CodexSetupState.Ready, "配置完成 · 连接已准备好", "请在任务阶段选择已有 Codex 聊天；具体聊天的状态、提问／批准及实际恢复需分别验证。", diagnostics, true, true));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or System.Text.Json.JsonException)
        { return Result(new(CodexSetupState.Failed, "初始化未完成", "请重新检查；仍失败时展开诊断，修复路径或连接配置。", e.GetType().Name + ": " + e.Message)); }
    }
}

internal sealed class CodexSetupEnvironment : ICodexSetupEnvironment
{
    public Task<CodexInstallation> Discover(string desktop, string cli) => CodexInstallationDiscovery.Find(desktop, cli);
    public bool Compatible(string desktop) => CodexInstallationDiscovery.Exists(desktop) &&
        CodexInstallationDiscovery.RegisteredDesktops().Contains(desktop, StringComparer.OrdinalIgnoreCase) &&
        string.Equals(CodexInstallationDiscovery.Hash(desktop), CodexDesktopBridge.ObservedExecutableHash, StringComparison.OrdinalIgnoreCase);
    public async Task<CodexConnectionCheck> Check(string cli, string endpoint, string inspector)
    {
        var catalog = false; var reason = "";
        try
        {
            await using var rpc = await CodexRpc.Start(cli, false, endpoint).ConfigureAwait(false);
            var result = await rpc.Call("thread/list", new { limit = 1, archived = false, useStateDbOnly = true }).ConfigureAwait(false);
            catalog = CodexState.Object(result, "data").ValueKind == System.Text.Json.JsonValueKind.Array;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException or System.Net.WebSockets.WebSocketException or System.ComponentModel.Win32Exception)
        { reason = "catalog-unavailable-" + e.GetType().Name; }
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
        if (!Compatible(desktop)) throw new InvalidOperationException("桌面程序不在已核实兼容范围，未打开。");
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
