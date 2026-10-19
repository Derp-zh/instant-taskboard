using System.Text.Json;
using Taskboard.Core;

internal static class CodexRecoveryRules
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        static WorkItem Item(Lane lane = Lane.Action)
        {
            var task = new WorkItem { Title = "Codex恢复隔离检查", Lane = lane, TimerUnread = true };
            task.Stages[0].IsRunning = true;
            task.Stages[0].Codex = new CodexBinding
            {
                ThreadId = "recovery-a", TurnId = "turn-a", SourceEpoch = "live-source",
                Phase = CodexPhase.WaitingForInput, LastSequence = 0, PendingRequests = ["question-a"]
            };
            return task;
        }
        static JsonElement Message(string method, object value) => JsonSerializer.SerializeToElement(new { method, @params = value });
        static JsonElement Active(string thread = "recovery-a", string turn = "turn-a") => Message("thread/status/changed",
            new { threadId = thread, turnId = turn, status = new { type = "active", activeFlags = Array.Empty<string>() } });
        static bool Apply(WorkItem task, JsonElement message, long sequence, bool authoritative = true, int generation = 1, string epoch = "live-source") =>
            CodexState.Apply(task, task.Stages[0].Id, message, sequence, generation, epoch, authoritative);

        var task = Item();
        var resolved = Message("serverRequest/resolved", new { threadId = "recovery-a", turnId = "turn-a", requestId = "question-a" });
        check(Apply(task, resolved, 1) && task.CurrentCodex!.PendingRequests.Count == 0 && task.CurrentCodex.Phase == CodexPhase.WaitingForInput && task.Lane == Lane.Action && task.TimerUnread,
            "回答请求已处理不冒充恢复、不清时间红点");
        task.CurrentCodex!.Phase = CodexPhase.WaitingForApproval;
        task.CurrentCodex.PendingRequests.Add("approval-a");
        Apply(task, Message("serverRequest/resolved", new { threadId = "recovery-a", requestId = "approval-a" }), 2);
        check(task.CurrentCodex.Phase == CodexPhase.WaitingForApproval && task.Lane == Lane.Action && task.TimerUnread,
            "批准请求已处理仍待真实恢复、不清时间红点");
        check(Apply(task, Active(), 3) && task.Lane == Lane.Running && !task.TimerUnread,
            "可信实际恢复使待操作回进行中并同时清时间红点");

        // The timer may move a still-running card to Action between otherwise unrelated messages.
        task.Lane = Lane.Action; task.TimerUnread = true;
        check(!Apply(task, resolved, 4) && task.Lane == Lane.Action && task.TimerUnread,
            "请求处理通知不借缓存运行状态拉回时间提醒任务");
        check(!Apply(task, Message("probe/snapshot", new { threadId = "recovery-a", status = new { type = "idle" } }), 5) && task.Lane == Lane.Action && task.TimerUnread,
            "仅空闲快照不借缓存运行状态移列或清红点");
        check(!Apply(task, Message("unknown/event", new { threadId = "recovery-a" }), 6) && task.Lane == Lane.Action && task.TimerUnread,
            "未知通知不借缓存运行状态移列或清红点");
        check(Apply(task, Active(), 7) && task.Lane == Lane.Running && !task.TimerUnread,
            "相同运行阶段的可信新快照仍报告实际移列与红点变化");
        task.TimerUnread = true;
        check(!Apply(task, Active(), 8) && task.Lane == Lane.Running && task.TimerUnread,
            "已在进行中的后台运行刷新不自行清除红点");

        task = Item();
        Apply(task, Message("turn/completed", new { threadId = "recovery-a", turn = new { id = "turn-a", status = "completed" } }), 1);
        check(task.CurrentCodex!.Phase == CodexPhase.ReadyForReview && task.Lane == Lane.Action && task.TimerUnread,
            "回答后直接结束只进入结果待检查、不虚构恢复或清红点");
        Apply(task, Message("turn/started", new { threadId = "recovery-a", turn = new { id = "turn-b" } }), 2);
        check(task.Lane == Lane.Running && !task.TimerUnread && task.CurrentCodex.TurnId == "turn-b",
            "可信新轮次开始可从待检查恢复并清红点");

        task = Item();
        task.Stages.Add(new Stage { Number = 1, IsRunning = true, Codex = new CodexBinding
            { ThreadId = "recovery-b", SourceEpoch = "live-source", TurnId = "turn-b", Phase = CodexPhase.WaitingForApproval } });
        Apply(task, Active(), 1);
        check(task.Lane == Lane.Action && task.TimerUnread && task.Stages[0].Codex!.Phase == CodexPhase.Running,
            "另一个运行阶段仍待批准时待操作优先、红点保留");
        task.Stages[1].Codex!.Phase = CodexPhase.ReadyForReview;
        Apply(task, Active(), 2);
        check(task.Lane == Lane.Action && task.TimerUnread,
            "另一个运行阶段结果待检查时不被恢复事件盖过");
        task.Stages[1].IsRunning = false;
        Apply(task, Active(), 3);
        check(task.Lane == Lane.Running && !task.TimerUnread,
            "暂停阶段不参与待处理聚合、可信运行移列才清红点");

        // A real desktop reconnect used to erase B's review phase, allowing a
        // still-running A snapshot to pull the multi-stage card back to Running.
        task = Item();
        task.Stages.Add(new Stage { Number = 1, IsRunning = true, Codex = new CodexBinding
            { ThreadId = "recovery-b", SourceEpoch = "live-source", TurnId = "turn-b", Phase = CodexPhase.ReadyForReview, AwaitingUserAction = true } });
        task.Stages[1].Codex!.Phase = CodexPhase.Unknown; // BoardController disconnected projection
        Apply(task, Active(), 1, epoch: "reconnected-source");
        check(task.Lane == Lane.Action && task.TimerUnread && task.Stages[1].Codex!.AwaitingUserAction,
            "重连后B待检查暂为未知时仍优先待操作、不被A运行覆盖");
        var waitStore = new AtomicStore(Path.Combine(root, "codex-disconnected-wait"));
        await waitStore.SaveAsync(new BoardState { Tasks = [task] });
        task = waitStore.Load().Tasks.Single();
        Apply(task, Active(), 2, epoch: "reconnected-source");
        check(task.Lane == Lane.Action && task.Stages[1].Codex!.AwaitingUserAction,
            "重连待处理保护通过正式存储保存重读仍有效");
        CodexState.Apply(task, task.Stages[1].Id, Active("recovery-b", "turn-new-b"), 1, 1, "reconnected-source", true);
        check(task.Lane == Lane.Running && !task.TimerUnread && !task.Stages[1].Codex!.AwaitingUserAction,
            "B也确认恢复后才解除多阶段待处理保护并清红点");
        foreach (var waiting in new[] { CodexPhase.WaitingForInput, CodexPhase.WaitingForApproval })
        {
            task = Item(); task.CurrentCodex!.Phase = waiting;
            Apply(task, Message("probe/disconnected", new { threadId = "recovery-a" }), 1);
            check(task.CurrentCodex!.Phase == CodexPhase.Unknown && task.CurrentCodex.AwaitingUserAction && task.Lane == Lane.Action,
                "断线保留" + waiting + "的待处理保护且不虚构恢复");
        }

        foreach (var lane in new[] { Lane.Inspiration, Lane.Refining, Lane.Paused, Lane.Done })
        {
            task = Item(lane); Apply(task, Active(), 1);
            check(task.Lane == lane && task.TimerUnread && task.CurrentCodex!.Phase == CodexPhase.Running,
                $"可信运行不改变{lane}人工列表及时间红点");
        }
        task = Item(); task.ColumnId = Guid.NewGuid(); var column = task.ColumnId;
        Apply(task, Active(), 1);
        check(task.ColumnId == column && task.Lane == Lane.Action && task.TimerUnread,
            "自定义列表不自动移列或清除时间红点");
        task = Item(); task.Stages[0].IsRunning = false;
        Apply(task, Active(), 1);
        check(task.Lane == Lane.Action && task.TimerUnread && task.CurrentCodex!.Phase == CodexPhase.Running,
            "人工暂停阶段只更新自身状态、不移列或清时间红点");

        task = Item();
        var original = Copies.Of(task); var edited = Copies.Of(task); edited.Title = "编辑器待提交标题";
        Apply(task, Active(), 1);
        var merged = BoardEngine.Merge(original, edited, task);
        check(merged.Title == edited.Title && merged.Lane == Lane.Running && !merged.TimerUnread && merged.CurrentCodex!.Phase == CodexPhase.Running,
            "旧编辑草稿提交不覆盖后台恢复移列与已清除红点");
        var store = new AtomicStore(Path.Combine(root, "codex-recovery"));
        await store.SaveAsync(new BoardState { Tasks = [merged] });
        var restored = store.Load().Tasks.Single();
        check(restored.Lane == Lane.Running && !restored.TimerUnread && restored.CurrentCodex!.TurnId == "turn-a",
            "恢复移列及红点清除通过正式存储重读保持");

        task = Item(); var unchanged = JsonSerializer.Serialize(task);
        check(!Apply(task, Active(), 1, authoritative: false) && JsonSerializer.Serialize(task) == unchanged,
            "历史只读来源不能恢复任务或清除红点");
        check(!Apply(task, Active(), 1, generation: 2) && JsonSerializer.Serialize(task) == unchanged,
            "旧绑定代次不能恢复任务或清除红点");
        check(!Apply(task, Active(turn: "older-turn"), 1) && JsonSerializer.Serialize(task) == unchanged,
            "其他轮次运行快照不能恢复当前轮次");
        check(!Apply(task, Active(thread: "other-thread"), 1) && JsonSerializer.Serialize(task) == unchanged,
            "其他会话运行信号不能恢复绑定任务");
        Apply(task, Active(), 5); task.Lane = Lane.Action; task.TimerUnread = true;
        check(!Apply(task, Active(), 4) && task.Lane == Lane.Action && task.TimerUnread,
            "乱序运行信号不能重放恢复或清除红点");
        check(!Apply(task, Message("turn/started", new { threadId = "recovery-a", turn = new { id = "turn-a" } }), 6) && task.Lane == Lane.Action && task.TimerUnread,
            "同一轮次重复开始不能重放恢复");
        Apply(task, Message("probe/disconnected", new { threadId = "recovery-a" }), 7);
        check(task.CurrentCodex!.Phase == CodexPhase.Unknown && task.Lane == Lane.Action && task.TimerUnread,
            "断线不等同结束或恢复、不清时间红点");
        Apply(task, Active(turn: "turn-new"), 1, epoch: "new-live-source");
        check(task.CurrentCodex!.SourceEpoch == "new-live-source" && task.CurrentCodex.LastSequence == 1 && task.Lane == Lane.Running && !task.TimerUnread,
            "已验证新连接epoch重新确认状态、序号独立并按实际恢复清红点");
    }
}
