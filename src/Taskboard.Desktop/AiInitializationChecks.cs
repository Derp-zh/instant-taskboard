using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Offscreen controls + injected connection fixtures. Never launches the user's
// Codex, changes the foreground window, moves the pointer, or sends a chat RPC.
internal static class AiInitializationChecks
{
    internal static async Task InspectEnvironment(string root)
    {
        Directory.CreateDirectory(root);
        var install = await CodexInstallationDiscovery.Find("", "");
        var helper = Path.Combine(Path.GetDirectoryName(install.Desktop) ?? "", "Codex.exe");
        var desktopRejectedAsCli = !await CodexInstallationDiscovery.IsCli(install.Desktop);
        var helperRejectedAsCli = !File.Exists(helper) || !await CodexInstallationDiscovery.IsCli(helper);
        if (!desktopRejectedAsCli || !helperRejectedAsCli) throw new InvalidOperationException("桌面或启动辅助程序错误识别为CLI");
        var outcome = await new CodexInitialization(new CodexSetupEnvironment()).Run(new());
        File.WriteAllText(Path.Combine(root, "ai-initialization-environment.json"), JsonSerializer.Serialize(new
        {
            inspectionCompleted = true, install.Desktop, install.Cli, install.Version, install.RegisteredDesktops,
            desktopFound = CodexInstallationDiscovery.Exists(install.Desktop), cliVersionCheck = install.Cli.Length > 0,
            registeredSignatureTrusted = CodexInstallationDiscovery.TrustedDesktop(install.Desktop),
            desktopRejectedAsCli, helperRejectedAsCli,
            state = outcome.Report.State.ToString(), outcome.Report.Message, outcome.Report.Next, outcome.Report.Diagnostic,
            outcome.Report.Catalog, outcome.Report.SourceReady, outcome.Report.DesktopConnected,
            originalConversationLifecycleVerified = false, desktopStartedOrStopped = false, originalChatsReadOrMutated = false,
            scope = "实际系统登记、文件位置、签名、CLI版本与目录、原桌面只读通道；不选择具体聊天，不启动或重启Codex。观察源和聊天生命周期分别保留。"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static async Task Run(BoardController controller)
    {
        var root = controller.Store.DirectoryPath;
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        var preferences = controller.Engine.State.Preferences;
        Check(CodexInitialization.ShouldPrompt(preferences), "旧配置没有初始化记录时首次提示");
        var prompt = await controller.PromptAiInitialization(false);
        Check(prompt is not null, "首次提示实际构造成功");
        var later = Logical<Button>(prompt!.Root).Single(x => Equals(x.Tag, "AiInitializeLater"));
        Check(Logical<Button>(prompt.Root).Any(x => Equals(x.Tag, "AiInitializeNow")), "首次提示包含一键初始化和稍后配置两个动作");
        Check(Logical<TextBlock>(prompt.Root).Any(x => x.Text == "关联 Codex 聊天后，看板可以根据执行状态自动移列。"), "首次提示说明用途");
        later.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await controller.Flush();
        Check(preferences.ConnectionConfigs[CodexInitialization.PromptKey] == "deferred", "稍后配置动作持久化");
        Check(await controller.PromptAiInitialization(false) is null, "同次运行不重复首次提示");
        var reopened = new AtomicStore(root, controller.Store.ConfigurationDirectory).Load();
        Check(!CodexInitialization.ShouldPrompt(reopened.Preferences), "独立回读配置后再次启动不重复提示");
        Check(reopened.Tasks.Count == 0 && reopened.Sessions.Count == 0, "稍后配置不创建任务或聊天绑定");

        var environment = new Fixture(); var stages = new List<CodexSetupState>();
        var service = new CodexInitialization(environment);
        var seed = new Dictionary<string, string> { ["unrelated"] = "keep" };
        var ready = await service.Run(seed, report => stages.Add(report.State));
        Check(stages.SequenceEqual(new[] { CodexSetupState.Finding, CodexSetupState.Checking, CodexSetupState.Ready }), "发现与检查进度顺序正确");
        Check(ready.Report.SourceReady && ready.Report.Catalog && ready.Report.Next.Contains("具体聊天"), "连接准备好仍明确具体聊天需另行验证");
        Check(seed.Count == 1 && ready.Config["unrelated"] == "keep", "检查不原地篡改原配置且保留无关配置");
        Check(ready.Config[CodexInitialization.DesktopKey] != ready.Config[CodexInitialization.CliKey], "桌面程序与CLI分别保存");

        environment.Connection = new(true, true, false, "inspector-peer-mismatch"); environment.Running = true;
        var pending = await service.Run(seed);
        Check(pending.Report.State == CodexSetupState.NeedsReopen && !pending.Report.SourceReady && pending.Report.Catalog, "目录与桌面均可读但提问观察不可用必须待重开，不能假成功");
        Check(environment.OpenCount == 0, "一键初始化从不擅自打开或重启Codex");
        environment.Running = false;
        Check((await service.Run(seed)).Report.State == CodexSetupState.NeedsOpen, "Codex未运行提供主动打开下一步");
        environment.Supported = false; var beforeCheck = environment.CheckCount;
        environment.Install = environment.Install with { Version = "future-version" };
        environment.Connection = new(true, true, true, "unchanged-contract");
        Check((await service.Run(seed)).Report.SourceReady && environment.CheckCount == beforeCheck + 1, "新版本先实际连接，结构仍可用时不被版本号或启动信任白名单拒绝");
        environment.Connection = new(true, true, false, "unsupported-desktop-module");
        var changedModule = await service.Run(seed);
        Check(changedModule.Report.State == CodexSetupState.Incompatible && changedModule.Report.DesktopConnected && !changedModule.Report.SourceReady, "连接成功但观察结构变更时只报告缺少的观察能力");
        environment.Supported = true;
        environment.Connection = new(false, true, true, "catalog-unavailable");
        Check((await service.Run(seed)).Report.State == CodexSetupState.CatalogUnavailable, "观察源可用但目录失败仍不完成");
        environment.Connection = new(true, false, false, "pipe-not-found");
        Check(!(await service.Run(seed)).Report.SourceReady, "单独历史服务可读不冒充原桌面完整监测");
        environment.Connection = new(true, true, false, "inspector-port-occupied");
        Check((await service.Run(seed)).Report.State == CodexSetupState.ConnectionUnavailable, "端口冲突提供修复端口动作而非盲目要求重启");
        environment.Connection = new(true, true, true, "fixture-ready");
        var original = new Dictionary<string, string> { [CodexInitialization.DesktopKey] = "manual-desktop.exe", [CodexInitialization.CliKey] = "manual-cli.exe", [CodexInitialization.InspectorKey] = "http://127.0.0.1:19439", ["codex.endpoint"] = "ws://127.0.0.1:19440", ["other"] = "unchanged" };
        var preserved = await service.Run(original);
        Check(preserved.Report.SourceReady && original.All(x => preserved.Config[x.Key] == x.Value), "有效手动路径和原服务地址沿用，实际连接可用时无需用户切换连接路线");
        environment.RejectManual = true;
        var invalid = await service.Run(original);
        Check(invalid.Report.State == CodexSetupState.InvalidPath && invalid.Config[CodexInitialization.CliKey] == "manual-cli.exe", "失效原路径保留并给出手动修复入口");
        environment.RejectManual = false;
        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        var oldRoot = Path.Combine(windowsApps, "OpenAI.Codex_1.0.0.0_x64__2p2nqsd0c76g0", "app");
        var newRoot = Path.Combine(windowsApps, "OpenAI.Codex_2.0.0.0_x64__2p2nqsd0c76g0", "app");
        var updatedDesktop = Path.Combine(newRoot, "ChatGPT.exe"); var updatedCli = Path.Combine(newRoot, "resources", "codex.exe");
        environment.Install = new(updatedDesktop, updatedCli, "new", [updatedDesktop], "fixture-registration"); environment.RejectManual = true;
        var storePrevious = new Dictionary<string, string> { [CodexInitialization.DesktopKey] = Path.Combine(oldRoot, "ChatGPT.exe"), [CodexInitialization.CliKey] = Path.Combine(oldRoot, "resources", "codex.exe") };
        var repaired = await service.Run(storePrevious);
        Check(repaired.Report.SourceReady && repaired.Config[CodexInitialization.DesktopKey] == updatedDesktop && repaired.Config[CodexInitialization.DesktopKey + ".previous"] == storePrevious[CodexInitialization.DesktopKey], "商店升级后自动刷新同包相对路径并备份旧路径，不要求重新手动查找");
        Check(!CodexInstallationDiscovery.ReplacedStorePath(Path.Combine(oldRoot, "other.exe"), updatedDesktop) && !CodexInstallationDiscovery.ReplacedStorePath("C:/Temp/ChatGPT.exe", updatedDesktop), "其他程序和任意失效手动路径不被升级路径自动修复规则接管");
        environment.RejectManual = false;
        environment.Install = new("", "", "", [], "not-installed");
        Check((await service.Run(seed)).Report.State == CodexSetupState.NotFound, "未找到桌面程序提供手动选择");
        environment.Install = new("desktop.exe", "", "known", [], "no-cli");
        Check((await service.Run(seed)).Report.State == CodexSetupState.MissingCli, "发现桌面不等于CLI可用");
        environment.Install = new("desktop.exe", "cli.exe", "known", [], "fixture");
        var badAddress = await service.Run(new() { [CodexInitialization.InspectorKey] = "http://external.invalid:9339" });
        Check(badAddress.Report.State == CodexSetupState.Failed && !badAddress.Report.SourceReady, "原观察地址无效时保留且拒绝远程来源");

        await controller.InitializeAiPlatform(environment);
        var saved = new AtomicStore(root, controller.Store.ConfigurationDirectory).Load();
        Check(saved.Preferences.ConnectionConfigs[CodexInitialization.CliKey] == "cli.exe" && saved.Preferences.ConnectionConfigs[CodexInitialization.DesktopKey] == "desktop.exe" && saved.Preferences.ConnectionConfigs[CodexInitialization.InspectorKey] == CodexInitialization.DefaultInspector, "初始化后的桌面CLI与观察配置实际落盘并独立重读");
        Check(controller.Engine.State.Tasks.Count == 0 && controller.AiConversationVerification.Contains("尚未验证"), "初始化成功不自动选择聊天且具体聊天显示未验证");
        var settings = new SettingsSurface(controller); settings.SelectSection("AI平台接入情况");
        Layout(settings.Root, 760, 680);
        var buttons = Logical<Button>(settings.Root).ToArray();
        Check(buttons.Any(x => Equals(x.Tag, "AiInitialize")) && buttons.Any(x => Equals(x.Tag, "AiChooseDesktop")) && buttons.Any(x => Equals(x.Tag, "AiChooseCli")), "设置提供重新检查和分别手动选择桌面CLI入口");
        Check(buttons.Any(x => Equals(x.Tag, "AiTestConversations")) && !buttons.Any(x => Equals(x.Content, "改用原 Codex 桌面连接")), "设置提供只读测试入口且移除内部路线切换按钮");
        await controller.TestAiConversations();
        Check(controller.AiConversationTest.Contains("尚未关联") && controller.Engine.State.Tasks.Count == 0, "无聊天时测试不创建或接管聊天");
        Check(Visual<TextBlock>(settings.Root).Any(x => Equals(x.Tag, "AiConversationVerification") && x.Text.Contains("尚未验证")), "设置明确显示具体聊天尚未验证");
        Check(Visual<TextBlock>(settings.Root).Any(x => Equals(x.Tag, "AiInitializationResult") && x.Text.Contains("当前连接待确认")), "上次检查成功而当前无真实连接时，界面仍显示待确认");
        var snapshot = new RenderTargetBitmap(760, 680, 96, 96, PixelFormats.Pbgra32); snapshot.Render(settings.Root);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(snapshot)); using (var stream = File.Create(Path.Combine(root, "ai-initialization-settings.png"))) png.Save(stream);
        await controller.InitializeAiPlatform(environment);
        Check(controller.AiSetupReport.SourceReady && !controller.AiSetupBusy, "设置重复初始化完成后可继续使用");

        var session = controller.Engine.NewSession(Lane.Running); session.Working.Title = "隔离初始化后手动绑定";
        var stage = session.Working.Stages[0];
        using var picker = new StageConversationPicker(controller, stage, () => { }, true);
        picker.SetCatalog([new("fixture-exact-a", "初始化会话甲", 1), new("fixture-exact-b", "初始化会话乙", 2)]);
        picker.Input.Text = "会话乙";
        Check(picker.Matches.Length == 1 && stage.Codex is null, "初始化后的原选择器按名称筛选，搜索不自动绑定");
        picker.Select(picker.Matches[0]);
        var item = controller.Engine.Submit(session); await controller.Flush();
        var binding = item.Stages[0].Codex!;
        Check(binding.ThreadId == "fixture-exact-b" && new AtomicStore(root, controller.Store.ConfigurationDirectory).Load().Tasks.Single().Stages[0].Codex!.ThreadId == "fixture-exact-b", "用户手动选择准确聊天后绑定与任务落盘保持");
        var beforeTest = JsonSerializer.Serialize(controller.Engine.State);
        await controller.TestAiConversations((ids, address) => Task.FromResult(new[] { ids.SequenceEqual(new[] { "fixture-exact-b" }) ? "准确绑定实时可读，正在等待回答" : "错误来源" }));
        Check(controller.AiConversationTest.Contains("正在等待回答") && JsonSerializer.Serialize(controller.Engine.State) == beforeTest, "用户主动测试只读取准确绑定，不修改任务状态、请求保护或配置");
        var delayedTest = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var testing = controller.TestAiConversations((_, _) => delayedTest.Task);
        binding.Generation++; delayedTest.SetResult(["过期的聊天测试成功"]); await testing;
        Check(controller.AiConversationTest.Contains("已变化") && !controller.AiConversationTest.Contains("过期"), "测试期间改绑时丢弃旧绑定结果，不能把旧成功套到新聊天");
        binding.Generation--;
        var epoch = "fixture-init-epoch";
        JsonElement Event(string method, string status, string[] flags) => JsonSerializer.SerializeToElement(new { method, @params = new { threadId = binding.ThreadId, status = new { type = status, activeFlags = flags }, turn = new { id = "fixture-turn", status = "inProgress" } } });
        CodexState.Apply(item, stage.Id, Event("thread/status/changed", "active", ["waitingOnUserInput"]), 1, binding.Generation, epoch, true);
        Check(item.Lane == Lane.Action, "初始化后准确聊天提问信号使实际任务进入待操作（合成信号）");
        CodexState.Apply(item, stage.Id, Event("thread/status/changed", "active", []), 2, binding.Generation, epoch, true);
        Check(item.Lane == Lane.Running, "初始化后实际执行状态使任务回进行中（合成信号）");
        controller.Engine.Notify([item.Id]); await controller.Flush();
        Check(new AtomicStore(root, controller.Store.ConfigurationDirectory).Load().Tasks.Single().Lane == Lane.Running, "状态变化和初始化配置一起持久恢复");
        Check(environment.OpenCount == 0, "整套隔离夹具未调用桌面启动动作");
        environment.DiscoveryPause = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var initializing = controller.InitializeAiPlatform(environment);
        preferences.ConnectionConfigs[CodexInitialization.InspectorKey] = "http://127.0.0.1:19999";
        // This is an existing key kept by the service, so use a changed discovered path too.
        preferences.ConnectionConfigs.Remove(CodexInitialization.DesktopKey);
        environment.DiscoveryPause.TrySetResult(true); await initializing;
        Check(!preferences.ConnectionConfigs.ContainsKey(CodexInitialization.DesktopKey) && preferences.ConnectionConfigs[CodexInitialization.InspectorKey].EndsWith(":19999", StringComparison.Ordinal) && controller.AiSetupReport.State == CodexSetupState.Failed, "异步初始化期间的新配置变更不会被旧检查覆盖或误报成功");
        settings.Destroy(false);
        File.WriteAllText(Path.Combine(root, "ai-initialization-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, offscreenControls = true, simulatedConnections = true, originalDesktopVerified = false, mouseMoved = false, codexStartedOrStopped = false, productionDataChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Layout(FrameworkElement element, double width, double height)
    { element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); }
    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject
    { if (root is T found) yield return found; foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var value in Logical<T>(child)) yield return value; }
    private static IEnumerable<T> Visual<T>(DependencyObject root) where T : DependencyObject
    { if (root is T found) yield return found; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Visual<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private sealed class Fixture : ICodexSetupEnvironment
    {
        internal CodexInstallation Install = new("desktop.exe", "cli.exe", "known", [], "fixture");
        internal CodexConnectionCheck Connection = new(true, true, true, "fixture-ready");
        internal bool Supported = true, Running, RejectManual;
        internal int OpenCount, CheckCount;
        internal TaskCompletionSource<bool>? DiscoveryPause;
        public async Task<CodexInstallation> Discover(string desktop, string cli)
        {
            if (DiscoveryPause is not null) await DiscoveryPause.Task;
            return Install with { Desktop = desktop.Length > 0 && !RejectManual ? desktop : Install.Desktop, Cli = cli.Length > 0 && !RejectManual ? cli : Install.Cli };
        }
        public bool CanOpenDesktop(string desktop) => Supported;
        public Task<CodexConnectionCheck> Check(string cli, string endpoint, string inspector) { CheckCount++; return Task.FromResult(Connection); }
        public bool DesktopRunning(string desktop) => Running;
        public Task OpenDesktop(string desktop, string inspector) { OpenCount++; return Task.CompletedTask; }
    }
}
