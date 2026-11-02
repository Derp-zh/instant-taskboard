using System.Diagnostics;
using System.Text.Json;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class LocalChecks
{
    internal static async Task Run(string output, string mode)
    {
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }
        if (mode == "--local-live")
        {
            var id = Environment.GetEnvironmentVariable("CODEX_THREAD_ID") ?? throw new Exception("显式当前聊天缺失");
            var item = new WorkItem { Title = "本机事件隔离验证", Lane = Lane.Action, TimerUnread = true };
            item.Stages[0].IsRunning = true; item.Stages[0].Codex = new() { ThreadId = id };
            var binding = item.Stages[0].Codex!;
            await using var observer = new CodexObserver(); var gate = new object(); long events = 0;
            observer.Event += (watch, message, sequence, epoch, live) => { lock (gate) { CodexState.Apply(item, watch.StageId, message, sequence, watch.Generation, epoch, live); events++; } };
            observer.Watch([new(item.Id, item.Stages[0].Id, binding.BindingId, id, binding.Generation)]);
            observer.Start("", "");
            var start = Stopwatch.StartNew();
            while (start.ElapsedMilliseconds < 12000 && !observer.Capabilities.Any(x => x.Live)) await Task.Delay(100);
            Check(observer.TransportConnected && observer.SourceIdentityVerified, "原桌面本机管道同SID/Session/包身份通过");
            Check(observer.Capabilities.Any(x => x.Live), "准确当前聊天的原内存状态可读");
            await Task.Delay(15000);
            Check(observer.ReadMode == "local-events" && observer.CompatibilityReadCount == 0 && observer.TurnReadCount == 0, "普通默认启动无CDP/CLI/聊天查询");
            Check(observer.LocalReadCount > 0 && events > 0, "原桌面真实状态变化进入产品Observer");
            lock (gate)
            {
                Check(binding.TurnId?.Length > 0, "真实准确轮次进入隔离任务阶段");
                Check(item.Lane == Lane.Running && binding.Phase == CodexPhase.Running, "当前实际执行驱动隔离任务回进行中");
            }
            var board = new BoardState(); board.Tasks.Add(item);
            var store = new AtomicStore(output); await store.SaveAsync(Copies.Of(board));
            var reopened = store.Load().Tasks.Single();
            Check(reopened.CurrentCodex?.ThreadId == id && reopened.CurrentCodex.TurnId == binding.TurnId && reopened.Lane == Lane.Running,
                "准确聊天/轮次/移列隔离落盘并独立回读");
            await observer.DisposeAsync();
            await using var restarted = new CodexObserver();
            restarted.Event += (watch, message, sequence, epoch, live) => CodexState.Apply(reopened, watch.StageId, message, sequence, watch.Generation, epoch, live);
            var rebound = reopened.Stages[0].Codex!;
            restarted.Watch([new(reopened.Id, reopened.Stages[0].Id, rebound.BindingId, id, rebound.Generation)]);
            restarted.Start("", ""); var reopenClock = Stopwatch.StartNew();
            while (reopenClock.ElapsedMilliseconds < 10000 && !restarted.Capabilities.Any(x => x.Live)) await Task.Delay(100);
            Check(restarted.SourceIdentityVerified && restarted.Capabilities.Any(x => x.Live) && reopened.CurrentCodex!.ThreadId == id,
                "从隔离落盘重开后自动重建本机连接及准确绑定");
            Check(restarted.CompatibilityReadCount == 0 && restarted.TurnReadCount == 0, "重开同样没有聊天查询");
            var report = new { passed = checks.Count, checks, readMode = observer.ReadMode, localFrames = observer.LocalReadCount,
                chatQueries = observer.CompatibilityReadCount, turnQueries = observer.TurnReadCount, events, lifecycleVerified = false,
                noMessagesSent = true, noUserProcessesChanged = true, noMouseOrClipboard = true };
            await File.WriteAllTextAsync(Path.Combine(output, "local-live.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(report)); return;
        }
        if (mode != "--local-unit") throw new ArgumentException("unknown isolated mode");
        const string tid = "11111111-1111-4111-8111-111111111111", owner = "22222222-2222-4222-8222-222222222222";
        object Agent(string id, object[]? questions = null) => new { id, type = "agentMessage", text = "MUST_NOT_RETAIN_BODY", questions = questions ?? [] };
        object Raw(object[] items, object[]? requests = null, string turnStatus = "inProgress", string type = "active") => new
        { id = tid, hostId = "local", title = "MUST_NOT_RETAIN_TITLE", requests = requests ?? [], threadRuntimeStatus = new { type, activeFlags = Array.Empty<string>() },
            turns = new[] { new { turnId = "current-turn", status = turnStatus, turnStartedAtMs = 1792753200000L, items } } };
        var s = new CodexLocalState(tid); long revision = 0;
        void Snapshot(object raw, int version = 11, string source = owner) => s.Apply(JsonSerializer.SerializeToElement(version), source,
            JsonSerializer.SerializeToElement(new { type = "snapshot", revision = ++revision, conversationState = raw }));
        void Patch(object[] path, object? value, string op = "add") => s.Apply(JsonSerializer.SerializeToElement(11), owner,
            JsonSerializer.SerializeToElement(new { type = "patches", baseRevision = revision, revision = ++revision, patches = new[] { new { op, path, value } } }));
        string[] Flags() => CodexState.Object(s.Snapshot().Status, "activeFlags").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Snapshot(Raw([Agent("a")]));
        Check(s.Snapshot().LiveCapable && Flags().Length == 0, "本机现有执行快照可读");
        Check(!s.RetainedMetadata.Contains("MUST_NOT_RETAIN"), "结构投影不留标题/正文/问题答案");
        Snapshot(Raw([Agent("q", [new { question = "MUST_NOT_RETAIN_QUESTION" }])]));
        Check(Flags().Contains("waitingOnUserInput"), "异步问题来自真实questions索引");
        Patch(["turns", 0, "items", 1], Agent("work-during-question"));
        Check(Flags().Contains("waitingOnUserInput"), "提问期间执行不能盖过等待");
        var questionId = "[\"request_user_input_async\",\"q\",0]";
        var envelope = "<send_user_message_question_reply>" + JsonSerializer.Serialize(new[] { new { questionItemId = questionId, question = "MUST_NOT_RETAIN_QUESTION", answer = "MUST_NOT_RETAIN_ANSWER" } }) + "</send_user_message_question_reply>";
        Patch(["turns", 0, "items", 2], new { id = "reply", type = "steeringUserMessage", status = "queued", input = new[] { new { type = "text", text = envelope } } });
        Check(Flags().Contains("waitingOnUserInput"), "未接受的答复不解除问题");
        Patch(["turns", 0, "items", 2, "status"], "accepted", "replace");
        Check(Flags().Contains("pendingExecutionUnconfirmed"), "仅回答保留等待且不复用之前执行证据");
        Check(!s.RetainedMetadata.Contains("MUST_NOT_RETAIN"), "答复仅保留结构化问题ID");
        Patch(["turns", 0, "items", 3], new { id = "new-tool", type = "commandExecution", status = "inProgress", command = "MUST_NOT_RETAIN_COMMAND" });
        Check(Flags().Length == 0, "回答后真正新执行才恢复");
        Snapshot(Raw([new { id = "plan", type = "planImplementation", isCompleted = false }]));
        Check(Flags().Contains("waitingOnUserInput"), "未完成Plan条目单独进入等待");
        Snapshot(Raw([new { id = "input", type = "userInputResponse", requestId = 17, completed = false }]));
        Check(Flags().Contains("waitingOnUserInput"), "未完成输入响应单独进入等待");
        Snapshot(Raw([], [new { id = 18, method = "item/commandExecution/requestApproval", @params = new { threadId = tid, turnId = "current-turn" } }]));
        Check(Flags().Contains("waitingOnApproval"), "批准请求准确归属");
        Patch(["requests", 0], null, "remove");
        Check(Flags().Contains("pendingExecutionUnconfirmed"), "批准本身不算实际恢复");
        Patch(["turns", 0, "items", 0], Agent("after-approval"));
        Check(Flags().Length == 0, "批准后实际执行恢复");
        Snapshot(Raw([], turnStatus: "completed", type: "idle"));
        Check(s.Snapshot().Turns.Single().Status == "completed", "当前正常终态结构保留");
        Snapshot(Raw([], turnStatus: "interrupted", type: "idle"));
        Check(s.Snapshot().Turns.Single().Status == "interrupted", "当前停止结构保留");
        Snapshot(Raw([], turnStatus: "failed", type: "idle"));
        Check(s.Snapshot().Turns.Single().Status == "failed", "当前失败结构保留");
        Snapshot(Raw([]), version: 12);
        Check(!s.Snapshot().LiveCapable, "未知协议不猜状态");
        Snapshot(Raw([]));
        Check(s.Snapshot().LiveCapable, "有效快照恢复来源");
        s.Apply(JsonSerializer.SerializeToElement(11), owner, JsonSerializer.SerializeToElement(new { type = "patches", baseRevision = revision - 2, revision = revision + 1, patches = Array.Empty<object>() }));
        Check(!s.Snapshot().LiveCapable, "缺帧不当空请求/运行");
        s.Apply(JsonSerializer.SerializeToElement(11), owner, JsonSerializer.SerializeToElement(new { type = "snapshot", revision, conversationState = Raw([]) }));
        Check(s.Snapshot().LiveCapable, "缺帧后同水位完整本机快照可重新同步");
        var canonical = new { kind = "canonical", history = new { entitiesByKey = new Dictionary<string, object> { ["key-current"] = new { turnId = "current-turn", status = "inProgress", items = new object[] { Agent("a") } } },
            islands = new[] { new { newerBoundary = new { status = "exhausted" }, entries = new[] { new { value = "key-current" } } } } } };
        Snapshot(new { id = tid, hostId = "local", threadRuntimeStatus = new { type = "active", activeFlags = Array.Empty<string>() }, requests = Array.Empty<object>(),
            turnHistory = canonical, turns = new[] { new { turnId = "current-turn", status = "inProgress", items = new object[] { Agent("legacy-question", [new { question = "do not retain" }]) } } } });
        Check(Flags().Contains("waitingOnUserInput"), "canonical与legacy同轮补充问题不漏等待");
        Snapshot(new { id = tid, hostId = "local", threadRuntimeStatus = new { type = "active", activeFlags = Array.Empty<string>() }, requests = Array.Empty<object>(),
            turns = new object[] { new { turnId = "current-turn", status = "inProgress", items = new object[] { Agent("a") } }, new { turnId = (string?)null, status = "completed", items = Array.Empty<object>() } } });
        Check(s.Snapshot().LiveCapable && s.Snapshot().Turns.Any(x => x.Id == "current-turn"), "尾部无编号已完成条目不遮住真实控制轮");
        Snapshot(Raw([], [new { id = "input-tool", method = "item/tool/call", @params = new { threadId = tid, turnId = "current-turn", tool = "request_option_picker", arguments = new { question = "MUST_NOT_RETAIN", options = new[] { new { label = "MUST_NOT_RETAIN" } } } } }]));
        Check(Flags().Contains("waitingOnUserInput") && !s.RetainedMetadata.Contains("MUST_NOT_RETAIN"), "已核实动态输入工具从结构判等待且不留参数正文");
        Patch(["requests", 0], null, "remove");
        Patch(["turns", 0, "commandExecutionStartedAtMsById"], new Dictionary<string, long> { ["command"] = 1792753200100L });
        Check(Flags().Contains("pendingExecutionUnconfirmed"), "整个旧启动映射的补快照不被猜作新执行");
        Patch(["turns", 0, "commandExecutionStartedAtMsById", "command-2"], 1792753200200L);
        Check(Flags().Length == 0, "同轮明确新命令启动增量可确认真实恢复");
        var result = new { passed = checks.Count, checks, synthetic = true, desktopLifecycleVerified = false };
        await File.WriteAllTextAsync(Path.Combine(output, "local-unit.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
