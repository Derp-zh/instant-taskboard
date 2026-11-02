using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class CodexSetup0930Checks
{
    internal static async Task Run(BoardController controller)
    {
        if (!controller.Isolated) throw new InvalidOperationException("必须使用隔离数据。");
        var checks = new List<string>();
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
        var fixture = new EnvironmentFixture();
        var service = new CodexInitialization(fixture);
        var config = new Dictionary<string, string> { [CodexInitialization.CliKey] = "removed-optional-cli.exe", ["codex.endpoint"] = "ws://external.invalid:1234", [CodexInitialization.InspectorKey] = "http://external.invalid:9339" };
        var outcome = await service.Run(config);
        Check(outcome.Report.Catalog && outcome.Report.DesktopConnected && !outcome.Report.SourceReady,
            "本机握手和目录可用不冒充完整提问批准观察");
        Check(outcome.Report.State == CodexSetupState.Ready && outcome.Report.Message == "Codex 已连接", "通道准备好可以正常关联聊天");
        Check(outcome.Config[CodexInitialization.CliKey] == config[CodexInitialization.CliKey], "无效旧CLI配置不阻断连接且不被清空");
        Check(outcome.Config["codex.endpoint"] == config["codex.endpoint"] && outcome.Config[CodexInitialization.InspectorKey] == config[CodexInitialization.InspectorKey],
            "旧服务观察地址仅保留原值不作连接前置条件");
        Check(fixture.Opens == 0, "初始化不启动或重启Codex");
        var unknown = new CodexDesktopSnapshot(default, [], false, "unknown", "local");
        Check(CodexSetupEnvironment.DescribeSnapshot(unknown, 0).Contains("状态待确认"), "未知状态仅报告未确认");
        var activeMissing = JsonSerializer.SerializeToElement(new { type = "active", activeFlags = Array.Empty<string>() });
        Check(!CodexSetupEnvironment.DescribeSnapshot(new(activeMissing, [], true, "fixture", "local"), 0).Contains("确认正在执行"),
            "缺完整版本化请求信息不能把空active当作执行证明");
        var waiting = JsonSerializer.SerializeToElement(new { type = "active", activeFlags = new[] { "waitingOnApproval" }, pendingSourceVersion = 1 });
        Check(CodexSetupEnvironment.DescribeSnapshot(new(waiting, [], true, "fixture", "local"), 0).Contains("等待批准"), "已有可信批准状态可读显示");
        var incomplete = JsonSerializer.SerializeToElement(new { type = "active", pendingSourceVersion = 1 });
        Check(!CodexSetupEnvironment.DescribeSnapshot(new(incomplete, [], true, "fixture", "local"), 0).Contains("确认正在执行"),
            "只给版本号但缺轮次请求和flags仍不能确认执行");
        var complete = JsonSerializer.SerializeToElement(new { type = "active", activeFlags = Array.Empty<string>(), pendingSourceVersion = 1,
            pendingTurnId = "turn-one", pendingOverflow = false, pendingRequests = Array.Empty<object>() });
        Check(CodexSetupEnvironment.DescribeSnapshot(new(complete, [new("turn-one", "inProgress")], true, "fixture", "local"), 0).Contains("确认正在执行"),
            "同轮完整请求集合与有效执行状态才显示确认执行");
        var session = controller.Engine.NewSession(Lane.Running);
        session.Working.Title = "只查看已有状态";
        session.Working.Stages[0].IsRunning = true;
        session.Working.Stages[0].Codex = new CodexBinding { ThreadId = "11111111-1111-4111-8111-111111111111", Phase = CodexPhase.WaitingForInput, AwaitingUserAction = true };
        controller.Engine.Submit(session);
        var before = JsonSerializer.Serialize(controller.Engine.State);
        var readCount = controller.Codex.LocalReadCount;
        await controller.TestAiConversations();
        Check(before == JsonSerializer.Serialize(controller.Engine.State), "设置真实测试入口不改任务或等待保护");
        Check(!controller.Codex.TransportConnected && readCount == controller.Codex.LocalReadCount && controller.AiConversationTest.Contains("未额外连接聊天"),
            "测试仅查观察器已有结果不另开本机连接或关注");
        await CheckTerminalDisplay(controller, Check);
        File.WriteAllText(Path.Combine(controller.Store.DirectoryPath, "codex-setup0930-report.json"), JsonSerializer.Serialize(new
        { passed = checks.Count, checks, isolated = true, realCodexConnected = false, taskTestUsedExistingObserverOnly = true }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task CheckTerminalDisplay(BoardController controller, Action<bool, string> check)
    {
        var observer = controller.Codex;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var fields = new[] { "epoch", "capabilities", "<TransportConnected>k__BackingField", "<SourceIdentityVerified>k__BackingField",
            "<Authoritative>k__BackingField", "<SourceKind>k__BackingField", "<Status>k__BackingField", "Changed", "Event" }
            .Select(name => typeof(CodexObserver).GetField(name, flags)!).ToDictionary(field => field, field => field.GetValue(observer));
        void Set(string name, object value) => typeof(CodexObserver).GetField(name, flags)!.SetValue(observer, value);
        var item = new WorkItem { Title = "合成终态显示检查", Lane = Lane.Running, TimerUnread = true };
        item.Stages[0].IsRunning = true; item.Stages[0].Codex = new() { ThreadId = "11111111-1111-4111-8111-111111111111" };
        controller.Engine.State.Tasks.Add(item);
        var binding = item.CurrentCodex!;
        var watch = new CodexWatch(item.Id, item.Stages[0].Id, binding.BindingId, binding.ThreadId, binding.Generation);
        async Task Emit(string method, long sequence, string epoch, string turn = "", string? status = null)
        {
            var emit = (Action<CodexWatch, JsonElement, long, string, bool>)typeof(CodexObserver).GetField("Event", flags)!.GetValue(observer)!;
            emit(watch, JsonSerializer.SerializeToElement(new { method, @params = new { threadId = binding.ThreadId, turn = new { id = turn, status } } }), sequence, epoch, true);
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        async Task Refresh(string reason)
        {
            typeof(CodexObserver).GetMethod("PublishCapabilities", flags)!.Invoke(observer,
                [new[] { new CodexThreadCapability(binding.ThreadId, false, reason, "") }]);
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        try
        {
            typeof(BoardController).GetMethod("AttachCodex", flags)!.Invoke(controller, null);
            Set("epoch", "before"); Set("<TransportConnected>k__BackingField", true); Set("<SourceIdentityVerified>k__BackingField", true);
            Set("<SourceKind>k__BackingField", "desktop-native");
            await Emit("turn/started", 1, "before", "turn-one");
            Set("epoch", "after");
            await Emit("probe/disconnected", 1, "after");
            await Emit("turn/completed", 2, "after", "turn-one", "completed");
            await Refresh("file-terminal");
            check(binding.Phase == CodexPhase.ReadyForReview && item.Lane == Lane.Action && binding.AwaitingUserAction,
                "真实Controller回调保留跨重连补到的同轮已完成，不被无实时能力汇总覆盖");
            binding.SourceEpoch = "old";
            await Refresh("old-epoch");
            check(binding.Phase == CodexPhase.Unknown && binding.AwaitingUserAction, "旧连接终态不冒充当前已确认状态且不清等待保护");
            binding.SourceEpoch = "after"; binding.Phase = CodexPhase.ReadyForReview;
            Set("<TransportConnected>k__BackingField", false);
            await Refresh("disconnected");
            check(binding.Phase == CodexPhase.Unknown && binding.AwaitingUserAction && item.Lane == Lane.Action,
                "真实断线仍降为待确认并保留待操作和等待保护");
        }
        finally
        {
            controller.Engine.State.Tasks.Remove(item);
            foreach (var pair in fields) pair.Key.SetValue(observer, pair.Value);
        }
    }

    private sealed class EnvironmentFixture : ICodexSetupEnvironment
    {
        internal int Opens;
        public Task<CodexInstallation> Discover(string desktop, string cli) => Task.FromResult(new CodexInstallation("desktop.exe", "", "fixture", [], "fixture"));
        public Task<CodexConnectionCheck> Check(string cli, string endpoint, string inspector) => Task.FromResult(new CodexConnectionCheck(true, true, false, "local-transport-only"));
        public bool CanOpenDesktop(string desktop) => true;
        public bool DesktopRunning(string desktop) => true;
        public Task OpenDesktop(string desktop, string inspector) { Opens++; return Task.CompletedTask; }
    }
}
