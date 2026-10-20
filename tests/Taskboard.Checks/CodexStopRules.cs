using System.Text.Json;
using Taskboard.Core;

internal static class CodexStopRules
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        static WorkItem Item(Lane lane = Lane.Running)
        {
            var item = new WorkItem { Title = "停止隔离夹具", Lane = lane, TimerUnread = true };
            item.Stages[0].IsRunning = true;
            item.Stages[0].Codex = new CodexBinding { ThreadId = "stop-a", TurnId = "turn-a", SourceEpoch = "epoch-a", Phase = CodexPhase.Running, LastSequence = 0 };
            return item;
        }
        static JsonElement Message(string method, object data, string? id = null) => JsonSerializer.SerializeToElement(new { method, id, @params = data });
        static JsonElement Terminal(string status, string turn = "turn-a", string thread = "stop-a") => Message("turn/completed", new { threadId = thread, turn = new { id = turn, status } });
        static JsonElement Active(string thread = "stop-a", string turn = "turn-a") => Message("probe/snapshot", new { threadId = thread, turnId = turn, status = new { type = "active", activeFlags = Array.Empty<string>() } });
        static JsonElement Request(string id, bool approval = false, string turn = "turn-a") => Message(approval ? "item/commandExecution/requestApproval" : "item/tool/requestUserInput", new { threadId = "stop-a", turnId = turn }, id);
        static JsonElement Resolved(string id) => Message("serverRequest/resolved", new { threadId = "stop-a", turnId = "turn-a", requestId = id });
        static JsonElement Started(string turn) => Message("turn/started", new { threadId = "stop-a", turn = new { id = turn } });
        static bool Apply(WorkItem item, JsonElement message, long sequence, string epoch = "epoch-a", int generation = 1, bool live = true) => CodexState.Apply(item, item.Stages[0].Id, message, sequence, generation, epoch, live);
        static JsonElement Error(string code, bool? retry = false, string turn = "turn-a", string thread = "stop-a") => Message("error", new { threadId = thread, turnId = turn, willRetry = retry, error = new { codexErrorInfo = code } });

        foreach (var status in new[] { "completed", "failed", "interrupted" })
        {
            var item = Item();
            Apply(item, Terminal(status), 1);
            check(item.Lane == Lane.Action && item.CurrentCodex!.AwaitingUserAction && item.TimerUnread,
                status + "明确当前终态进待操作并锁定保护、保留时间红点");
            check(item.CurrentCodex!.Phase == (status == "failed" ? CodexPhase.Failed : status == "interrupted" ? CodexPhase.Interrupted : CodexPhase.ReadyForReview),
                status + "保留独立阶段状态");
            item.Stages.Add(new Stage { Number = 1, IsRunning = true, Codex = new CodexBinding { ThreadId = "stop-b", TurnId = "turn-b", SourceEpoch = "epoch-a", Phase = CodexPhase.Running } });
            CodexState.Apply(item, item.Stages[1].Id, Active("stop-b", "turn-b"), 1, 1, "epoch-a", true);
            check(item.Lane == Lane.Action && item.TimerUnread, status + "不会被另一运行阶段覆盖");
            Apply(item, Message("probe/disconnected", new { threadId = "stop-a" }), 2);
            CodexState.Apply(item, item.Stages[1].Id, Active("stop-b", "turn-b"), 1, 1, "epoch-b", true);
            check(item.CurrentCodex!.Phase == CodexPhase.Unknown && item.CurrentCodex.AwaitingUserAction && item.Lane == Lane.Action && item.TimerUnread,
                status + "断线跨epoch仍优先待处理");
            var store = new AtomicStore(Path.Combine(root, "stop-" + status));
            await store.SaveAsync(new BoardState { Tasks = [item] }); item = store.Load().Tasks.Single();
            CodexState.Apply(item, item.Stages[1].Id, Active("stop-b", "turn-b"), 2, 1, "epoch-b", true);
            check(item.Lane == Lane.Action && item.CurrentCodex!.AwaitingUserAction, status + "正式存储重读保留待处理保护");
            Apply(item, Started("turn-new"), 1, "epoch-b");
            check(item.Lane == Lane.Running && !item.TimerUnread && !item.CurrentCodex!.AwaitingUserAction,
                status + "准确新轮次真正恢复整卡才清时间红点");
        }

        var task = Item();
        Apply(task, Request("question-one"), 1); Apply(task, Request("approval-two", true), 2);
        check(task.CurrentCodex!.PendingRequests.Count == 2 && task.CurrentCodex.PendingApprovals.SequenceEqual(["approval-two"]) && task.Lane == Lane.Action,
            "提问和权限请求分别追踪同轮次多个ID");
        Apply(task, Active(), 3);
        check(task.CurrentCodex.PendingRequests.Count == 2 && task.CurrentCodex.Phase == CodexPhase.WaitingForApproval && task.TimerUnread,
            "active空flags不能吞掉未解决请求");
        Apply(task, Resolved("approval-two"), 4); Apply(task, Active(), 5);
        check(task.CurrentCodex.PendingRequests.SequenceEqual(["question-one"]) && task.CurrentCodex.Phase == CodexPhase.WaitingForInput && task.Lane == Lane.Action,
            "单个权限resolved不会盖过剩余提问");
        Apply(task, Message("probe/disconnected", new { threadId = "stop-a" }), 6);
        Apply(task, Active(), 1, "epoch-b");
        check(task.CurrentCodex.PendingRequests.Count == 1 && task.Lane == Lane.Action && task.TimerUnread,
            "未决请求跨断线和重连active空flags保留");
        var requestsStore = new AtomicStore(Path.Combine(root, "stop-requests"));
        await requestsStore.SaveAsync(new BoardState { Tasks = [task] }); task = requestsStore.Load().Tasks.Single();
        Apply(task, Resolved("question-one"), 2, "epoch-b");
        check(task.CurrentCodex!.PendingRequests.Count == 0 && task.Lane == Lane.Action && task.TimerUnread,
            "存储重读后回答最后请求仍不虚构执行恢复");
        Apply(task, Active(), 3, "epoch-b");
        check(task.Lane == Lane.Running && !task.TimerUnread, "全部请求解决后的可信执行才恢复并清红点");

        task = Item(); Apply(task, Request("unanswered"), 1);
        check(!Apply(task, Started("turn-a"), 1, "epoch-b") && task.CurrentCodex!.PendingRequests.Count == 1 && task.Lane == Lane.Action,
            "跨epoch重复同轮次started不能清未决请求或虚构恢复");
        Apply(task, Started("next-turn"), 2, "epoch-b");
        check(task.CurrentCodex!.PendingRequests.Count == 0 && task.CurrentCodex.PendingApprovals.Count == 0 && task.Lane == Lane.Running,
            "明确新轮次可以清旧请求并恢复");
        task = Item(); Apply(task, Request("pending"), 1); Apply(task, Resolved("pending"), 2); Apply(task, Terminal("failed"), 3);
        check(task.Lane == Lane.Action && task.CurrentCodex!.Phase == CodexPhase.Failed && task.TimerUnread,
            "回答后直接失败保持待处理、不得虚构中间恢复");

        foreach (var reason in new[] { "usageLimitExceeded", "rateLimitExceeded" })
        {
            task = Item(); Apply(task, Error(reason), 1);
            check(task.Lane == Lane.Action && task.CurrentCodex!.Phase == CodexPhase.Failed && task.CurrentCodex.AwaitingUserAction,
                reason + "当前轮次明确不重试才映射失败待处理");
        }
        foreach (var data in new[] { Error("usageLimitExceeded", true), Error("usageLimitExceeded", null), Error("unknown"), Error("usageLimitExceeded", turn: "older-turn"), Error("usageLimitExceeded", turn: ""), Error("usageLimitExceeded", thread: "other") })
        {
            task = Item(); Apply(task, data, 1);
            check(task.Lane == Lane.Running && task.CurrentCodex!.Phase == CodexPhase.Running && !task.CurrentCodex.AwaitingUserAction,
                "临时重试/未知错误/缺失或错误轮次与会话不假停止：" + data.GetRawText());
        }
        task = Item();
        Apply(task, Message("error", new { threadId = "stop-a", turnId = "turn-a", willRetry = false, error = new { codexErrorInfo = new { usageLimitExceeded = new { } } } }), 1);
        check(task.Lane == Lane.Action && task.CurrentCodex!.Phase == CodexPhase.Failed, "受限结构化枚举对象可以映射额度停止且不保存正文");
        task = Item();
        Apply(task, Message("error", new { threadId = "stop-a", turnId = "turn-a", willRetry = false, error = new { codexErrorInfo = new { usageLimitExceeded = true, other = true } } }), 1);
        check(task.Lane == Lane.Running && !task.CurrentCodex!.AwaitingUserAction, "不接受混杂多键的伪额度枚举对象");
        task = Item(); Apply(task, Message("item/commandExecution/completed", new { threadId = "stop-a", turnId = "turn-a", exitCode = 1 }), 1);
        check(task.Lane == Lane.Running && !task.CurrentCodex!.AwaitingUserAction, "普通命令非零不等于整轮停止");
        task = Item(); Apply(task, Message("probe/disconnected", new { threadId = "stop-a" }), 1);
        check(task.Lane == Lane.Running && task.CurrentCodex!.Phase == CodexPhase.Unknown && !task.CurrentCodex.AwaitingUserAction,
            "纯断线未知不冒充失败或停止");
        task = Item(); Apply(task, Terminal("failed"), 1, "epoch-b");
        check(task.Lane == Lane.Action && task.CurrentCodex!.TurnId == "turn-a", "重连首帧可补查准确已观察轮次失败、不要求先active");
        foreach (var message in new[] { Terminal("failed"), Error("usageLimitExceeded") })
        {
            task = Item(); task.CurrentCodex!.SourceEpoch = "";
            check(!Apply(task, message, 1, "epoch-b") && task.Lane == Lane.Running && task.CurrentCodex.SourceEpoch == "",
                "新绑定或启动基线不能重放存储旧轮次终态");
        }
        task = Item(); var unchanged = JsonSerializer.Serialize(task);
        foreach (var message in new[] { Terminal("failed", turn: ""), Terminal("failed", turn: "old"), Terminal("interrupted", thread: "other"), Terminal("unknown") })
            check(!Apply(task, message, 1) && JsonSerializer.Serialize(task) == unchanged, "缺轮次/旧轮次/其他会话终态不触碰绑定或卡片");
        check(!Apply(task, Terminal("failed"), 1, generation: 2) && !Apply(task, Terminal("failed"), 1, live: false) && JsonSerializer.Serialize(task) == unchanged,
            "旧绑定代次和历史来源失败事件不触碰正式状态");
        Apply(task, Terminal("interrupted"), 2); task.Lane = Lane.Running;
        check(!Apply(task, Terminal("interrupted"), 2) && task.Lane == Lane.Running, "重复同序号停止通知不重放人工已选列表");

        task = Item();
        for (var i = 1; i <= 130; i++) Apply(task, Request("request-" + i, i % 2 == 0), i);
        check(task.CurrentCodex!.PendingRequests.Count == 128 && task.CurrentCodex.PendingApprovals.Count <= 128 && task.CurrentCodex.PendingRequestOverflow,
            "未决请求数量有界且超量保持保守待处理保护");
        for (var i = 1; i <= 128; i++) Apply(task, Resolved("request-" + i), 130 + i);
        Apply(task, Active(), 260);
        check(task.Lane == Lane.Action && task.CurrentCodex.PendingRequestOverflow, "超量未决请求不因已缓存ID全解决而错误恢复");
        Apply(task, Started("next"), 261);
        check(!task.CurrentCodex.PendingRequestOverflow && task.Lane == Lane.Running, "准确新轮次清理有界请求溢出保护");
        task = Item(); check(!Apply(task, Request(new string('x', 257)), 1) && task.CurrentCodex!.PendingRequests.Count == 0,
            "拒绝过长请求ID且不持久化无限内容");

        foreach (var lane in new[] { Lane.Inspiration, Lane.Refining, Lane.Paused, Lane.Done })
        {
            task = Item(lane); Apply(task, Terminal("failed"), 1);
            check(task.Lane == lane && task.TimerUnread && task.CurrentCodex!.AwaitingUserAction, "失败不移动人工列表：" + lane);
        }
        task = Item(); task.ColumnId = Guid.NewGuid(); Apply(task, Terminal("interrupted"), 1);
        check(task.Lane == Lane.Running && task.ColumnId is not null, "中断不移动自定义列");
        task = Item(); task.Stages[0].IsRunning = false; Apply(task, Terminal("failed"), 1);
        check(task.Lane == Lane.Running && task.CurrentCodex!.AwaitingUserAction, "暂停阶段失败只更新自身、不自动移整卡");
        task = Item(); var original = Copies.Of(task); var edited = Copies.Of(task); edited.Title = "用户编辑";
        Apply(task, Terminal("failed"), 1); var merged = BoardEngine.Merge(original, edited, task);
        check(merged.Title == "用户编辑" && merged.Lane == Lane.Action && merged.CurrentCodex!.AwaitingUserAction && merged.TimerUnread,
            "旧编辑提交保留后台失败移列和提醒红点");
        edited.Stages[0].Codex = new CodexBinding { ThreadId = "different" };
        var rebound = BoardEngine.Merge(original, edited, task);
        check(!rebound.CurrentCodex!.AwaitingUserAction && rebound.CurrentCodex.PendingRequests.Count == 0 && rebound.CurrentCodex.PendingApprovals.Count == 0 && rebound.CurrentCodex.Phase == CodexPhase.Unknown,
            "改绑不继承旧失败及未决请求保护");
        var board = new BoardEngine(new BoardState { Tasks = [task] });
        board.Move(task.Id, Lane.Paused, null); board.Archive(task.Id); var restored = board.Unarchive(task.Id);
        check(!restored.CurrentCodex!.AwaitingUserAction && restored.CurrentCodex.PendingRequests.Count == 0 && restored.CurrentCodex.PendingApprovals.Count == 0 && !restored.CurrentCodex.PendingRequestOverflow,
            "归档恢复重新绑定基线、不继承旧失败和请求保护");

        // Use the real reminder path, rather than assigning the target lane, to
        // distinguish ongoing execution from execution that actually resumes.
        task = Item(); var now = DateTime.Now;
        task.Reminder = new Reminder { Enabled = true, Repeat = Repeat.Once, NextLocal = now.AddSeconds(-1) };
        var timed = new BoardState { Tasks = [task] };
        check(ReminderClock.Tick(timed, now).SequenceEqual([task.Id]) && task.Lane == Lane.Action && task.TimerUnread && task.CurrentCodex!.Phase == CodexPhase.Running,
            "真实提醒时钟把正在执行任务移待操作并设红点");
        check(Apply(task, Active(), 1) && task.Lane == Lane.Running && task.TimerUnread,
            "提醒后的同轮active保留阶段聚合，但不虚构恢复清红点");
        Apply(task, Active(), 2);
        check(task.Lane == Lane.Running && task.TimerUnread && !task.CurrentCodex!.AwaitingUserAction,
            "重复多次运行轮询不清定时红点、也不伪造Codex待处理原因");
        Apply(task, Message("probe/disconnected", new { threadId = "stop-a" }), 3);
        Apply(task, Active(), 1, "epoch-reminder-reconnected");
        check(task.CurrentCodex!.Phase == CodexPhase.Running && task.Lane == Lane.Running && task.TimerUnread,
            "仅断线Unknown后同准确轮次active不冒充实际恢复清红点");
        var reminderStore = new AtomicStore(Path.Combine(root, "reminder-running-poll"));
        await reminderStore.SaveAsync(timed); task = reminderStore.Load().Tasks.Single();
        Apply(task, Active(), 2, "epoch-reminder-reconnected");
        check(task.Lane == Lane.Running && task.TimerUnread, "提醒与运行状态保存重读后重复poll仍保留红点");
        task.Reminder.Enabled = true; task.Reminder.NextLocal = now.AddSeconds(-2);
        ReminderClock.Tick(new BoardState { Tasks = [task] }, now);
        Apply(task, Started("turn-after-reminder"), 3, "epoch-reminder-reconnected");
        check(task.Lane == Lane.Running && !task.TimerUnread, "提醒后准确新轮次实际执行才回进行中并清红点");

        task = Item(Lane.Action); task.CurrentCodex!.Phase = CodexPhase.WaitingForInput;
        Apply(task, Message("probe/disconnected", new { threadId = "stop-a" }), 1);
        Apply(task, Active(), 1, "epoch-real-resume");
        check(task.Lane == Lane.Running && !task.TimerUnread && !task.CurrentCodex.AwaitingUserAction,
            "已确认等待跨断线后实际执行仍可解除保护并清红点");
        task = Item(Lane.Action); Apply(task, Request("resume-question"), 1);
        Apply(task, Resolved("resume-question"), 2);
        Apply(task, Message("probe/disconnected", new { threadId = "stop-a" }), 3);
        Apply(task, Active(), 1, "epoch-answer-resume");
        check(task.Lane == Lane.Running && !task.TimerUnread,
            "请求resolved后跨重连真正执行可恢复，区别仅断线的重复运行");
    }
}
