using System.Reflection;
using System.Text.Json;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class PendingChecks
{
    internal static async Task Run(Action<bool, string> check, string output)
    {
        var active = JsonSerializer.SerializeToElement(new { type = "active", activeFlags = Array.Empty<string>() });
        CodexPendingSnapshot Pending(string turn, params CodexPendingRequest[] requests) => new("pending-fixture", turn, true, false, requests);
        foreach (var endpoint in new[] { "https://127.0.0.1:9229", "http://localhost:9229", "http://0.0.0.0:9229", "http://127.0.0.1:9229/a", "http://127.0.0.1:9229/?x=1", "http://x@127.0.0.1:9229" })
            check(!CodexPendingBridge.ValidEndpoint(endpoint, out _), "拒绝非显式IPv4回环或附加内容的观察端点：" + endpoint);
        check(CodexPendingBridge.ValidEndpoint("http://127.0.0.1:9229", out _), "观察桥只接受显式回环端点");
        var raw = JsonSerializer.SerializeToElement(new { schemaVersion = 1, hostId = "local", threadId = "pending-fixture", turnId = "turn-one",
            status = "inProgress", executing = true, complete = true, overflow = false,
            pending = new[] { new { id = "question:one", approval = false, text = "PRIVATE_QUESTION" } }, title = "PRIVATE_TITLE" });
        var projected = CodexPendingBridge.Parse(raw, "pending-fixture");
        check(projected?.Requests.Single().Id == "question:one" && !JsonSerializer.Serialize(projected).Contains("PRIVATE"), "观察投影只保留ID和类型，不保留问题、回答或标题");
        check(CodexPendingBridge.Parse(raw, "other-thread") is null, "观察投影拒绝其他聊天来源");
        var stale = CodexPendingBridge.Merge(active, [new("turn-two", "inProgress")], projected);
        check(!stale.TryGetProperty("pendingSourceVersion", out _), "另一轮次pending不能叠加到当前native快照");
        var ambiguous = CodexPendingBridge.Merge(active, [new("turn-one", "inProgress"), new("turn-two", "inProgress")], projected);
        check(!ambiguous.TryGetProperty("pendingSourceVersion", out _), "多个运行轮次不猜归属");

        await using var observer = new CodexObserver();
        var item = new WorkItem { Title = "隔离请求观察整链", Lane = Lane.Action, TimerUnread = true };
        item.Stages[0].IsRunning = true; item.Stages[0].Codex = new CodexBinding { ThreadId = "pending-fixture" };
        var binding = item.CurrentCodex!;
        var watch = new CodexWatch(item.Id, item.Stages[0].Id, binding.BindingId, binding.ThreadId, binding.Generation);
        observer.Watch([watch]);
        var type = typeof(CodexObserver); var access = BindingFlags.Instance | BindingFlags.NonPublic;
        var sourceEpoch = "pending-epoch";
        type.GetField("epoch", access)!.SetValue(observer, sourceEpoch);
        type.GetProperty("SourceKind", access)!.SetValue(observer, "desktop-native");
        var states = (System.Collections.IDictionary)type.GetField("trackers", access)!.GetValue(observer)!;
        var state = states[watch]!;
        var tracker = (CodexTurnTracker)state.GetType().GetField("Tracker", access)!.GetValue(state)!;
        var intermediateRunning = 0; var changed = false;
        observer.Event += (_, message, sequence, epoch, live) => {
            changed |= CodexState.Apply(item, watch.StageId, message, sequence, watch.Generation, epoch, live);
            if (item.Lane == Lane.Running) intermediateRunning++;
        };
        var apply = type.GetMethod("ApplyPoll", access)!;
        void Poll(string turn, CodexPendingSnapshot? pending = null, string status = "inProgress", string runtime = "active")
        {
            var turns = new[] { new CodexTurnMetadata(turn, status) };
            var nativeStatus = runtime == "active" ? active : JsonSerializer.SerializeToElement(new { type = runtime, activeFlags = Array.Empty<string>() });
            var merged = CodexPendingBridge.Merge(nativeStatus, turns, pending);
            apply.Invoke(observer, [watch, state, tracker.Revision, merged, turns, true, sourceEpoch]);
        }
        Poll("turn-one", Pending("turn-one", new("question:one", false), new("rpc:approval", true)));
        check(item.Lane == Lane.Action && binding.Phase == CodexPhase.WaitingForApproval && binding.PendingRequests.Count == 2,
            "async问题与批准共同入待操作，批准分类保持");
        check(intermediateRunning == 0 && item.TimerUnread, "首个准确运行轮次同时已有提问时不瞬间回进行中或清红点");
        Poll("turn-one");
        check(item.Lane == Lane.Action && binding.PendingRequests.Count == 2 && item.TimerUnread, "观察桥失联但native仍active不能吞掉已确认请求");
        Poll("turn-one", Pending("turn-one", new CodexPendingRequest("question:one", false)));
        check(binding.Phase == CodexPhase.WaitingForInput && item.Lane == Lane.Action && item.TimerUnread, "批准已处理但仍有问题时保持待操作");
        changed = false;
        Poll("turn-one", Pending("turn-one", new CodexPendingRequest("question:replacement", false)));
        check(changed && binding.PendingRequests.SequenceEqual(["question:replacement"]), "请求数量相同但ID替换也触发保存变更");
        var store = new AtomicStore(Path.Combine(output, "pending-protection"));
        await store.SaveAsync(new BoardState { Tasks = [item] });
        var loaded = store.Load().Tasks.Single();
        check(loaded.CurrentCodex!.PendingRequests.SequenceEqual(["question:replacement"]) && loaded.Lane == Lane.Action, "请求保护和准确ID经实际存储重读保留");
        var other = new Stage { IsRunning = true, Codex = new CodexBinding { ThreadId = "other-live-stage" } };
        item.Stages.Add(other);
        CodexState.Apply(item, other.Id, JsonSerializer.SerializeToElement(new { method = "turn/started", @params = new { threadId = "other-live-stage", turnId = "other-turn" } }), 1, 1, sourceEpoch, true);
        check(item.Lane == Lane.Action && item.TimerUnread, "另一阶段运行不能盖过准确异步问题保护");
        apply.Invoke(observer, [watch, state, tracker.Revision, default(JsonElement), Array.Empty<CodexTurnMetadata>(), false, sourceEpoch]);
        sourceEpoch = "pending-reconnected"; type.GetField("epoch", access)!.SetValue(observer, sourceEpoch);
        Poll("turn-one");
        check(item.Lane == Lane.Action && item.TimerUnread && binding.PendingRequests.SequenceEqual(["question:replacement"]),
            "跨重连native空active与另一运行阶段不能解除未答问题");
        var emittedBefore = intermediateRunning;
        apply.Invoke(observer, [watch, state, tracker.Revision, active, new[] { new CodexTurnMetadata("retired-source", "inProgress") }, true, "pending-epoch"]);
        check(intermediateRunning == emittedBefore && binding.TurnId == "turn-one", "旧epoch的观察状态不能覆盖重连后的问题");
        var edit = Copies.Of(item); edit.Title = "编辑时后台回答"; var original = Copies.Of(item);
        Poll("turn-one", Pending("turn-one") with { Executing = false });
        check(item.Lane == Lane.Action && item.TimerUnread, "全部回答但尚无执行确认不能伪造恢复");
        Poll("turn-one", Pending("turn-one"));
        check(item.Lane == Lane.Running && !item.TimerUnread && !binding.AwaitingUserAction && binding.PendingRequests.Count == 0,
            "准确空pending和同轮次真实执行共同解除等待并清提醒");
        var mergedEdit = BoardEngine.Merge(original, edit, item);
        check(mergedEdit.Lane == Lane.Running && !mergedEdit.TimerUnread && mergedEdit.CurrentCodex!.PendingRequests.Count == 0,
            "编辑保存保留回答后后台恢复，不复活旧请求");
        item.TimerUnread = true;
        Poll("turn-one", Pending("turn-one", new CodexPendingRequest("question:two", false)));
        Poll("turn-one", status: "completed", runtime: "idle");
        check(item.Lane == Lane.Action && item.TimerUnread && binding.Phase == CodexPhase.ReadyForReview && binding.PendingRequests.Count == 0,
            "回答前后直接结束保持待检查，不虚构执行恢复");
        Poll("turn-two", Pending("turn-two", new CodexPendingRequest("question:three", false)));
        check(item.Lane == Lane.Action && item.TimerUnread && binding.PendingRequests.Count == 1, "后续新轮次首帧已等待不清除旧提醒");
        Poll("turn-two", Pending("turn-two") with { Overflow = true });
        check(item.Lane == Lane.Action && binding.PendingRequestOverflow, "观察集合溢出保持等待保护");
        Poll("turn-two", Pending("turn-two"));
        check(item.Lane == Lane.Running && !item.TimerUnread && !binding.PendingRequestOverflow, "后续完整受限快照与执行共同解除溢出保护");
        foreach (var lane in new[] { Lane.Inspiration, Lane.Refining, Lane.Paused, Lane.Done })
        {
            var manual = new WorkItem { Title = "人工列", Lane = lane, TimerUnread = true };
            manual.Stages[0].IsRunning = true; manual.Stages[0].Codex = new CodexBinding { ThreadId = "pending-fixture" };
            var status = CodexPendingBridge.Merge(active, [new("turn-one", "inProgress")], Pending("turn-one", new CodexPendingRequest("question:manual", false)));
            CodexState.Apply(manual, manual.Stages[0].Id, JsonSerializer.SerializeToElement(new { method = "turn/started", @params = new { threadId = "pending-fixture", turnId = "turn-one", status } }), 1, 1, "manual", true);
            check(manual.Lane == lane && manual.TimerUnread, "观察到提问也不自动移动人工列" + lane);
        }
        var flagOnly = new WorkItem { Lane = Lane.Action, TimerUnread = true };
        flagOnly.Stages[0].IsRunning = true; flagOnly.Stages[0].Codex = new CodexBinding { ThreadId = "pending-fixture" };
        void ApplyFlag(JsonElement status, int sequence, string method = "probe/snapshot") => CodexState.Apply(flagOnly, flagOnly.Stages[0].Id,
            JsonSerializer.SerializeToElement(new { method, @params = new { threadId = "pending-fixture", turnId = "flag-turn", status } }), sequence, 1, "flag-epoch", true);
        ApplyFlag(JsonSerializer.SerializeToElement(new { type = "active", activeFlags = new[] { "waitingOnApproval" } }), 1, "turn/started");
        var unconfirmed = CodexPendingBridge.Merge(active, [new("flag-turn", "inProgress")], Pending("flag-turn") with { Executing = false });
        ApplyFlag(unconfirmed, 2);
        check(flagOnly.Lane == Lane.Action && flagOnly.TimerUnread && flagOnly.CurrentCodex!.AwaitingUserAction,
            "仅有原生等待标志而未取到ID时，空pending但执行未确认仍不能伪恢复");
        ApplyFlag(JsonSerializer.SerializeToElement(new { type = "active", activeFlags = new[] { 7 } }), 3);
        check(flagOnly.CurrentCodex!.Phase == CodexPhase.Unknown && flagOnly.Lane == Lane.Action && flagOnly.TimerUnread,
            "畸形非字符串flags不被过滤成空数组而误恢复");
    }
}
