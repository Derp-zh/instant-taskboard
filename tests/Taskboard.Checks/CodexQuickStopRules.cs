using System.Text.Json;
using Taskboard.Core;

internal static class CodexQuickStopRules
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        static WorkItem Item()
        {
            var item = new WorkItem { Title = "快速停止合成夹具", Lane = Lane.Running, TimerUnread = true };
            item.Stages[0].IsRunning = true;
            item.Stages[0].Codex = new CodexBinding { ThreadId = "quick-a", TurnId = "observed-turn", SourceEpoch = "quick-epoch", Phase = CodexPhase.Running, LastSequence = 0 };
            return item;
        }
        static JsonElement Stop(string status = "failed", string id = "new-stop", object? started = null, object? baseline = null, string thread = "quick-a") =>
            JsonSerializer.SerializeToElement(new { method = "probe/currentTurnStopped", @params = new { threadId = thread, turn = new { id, status, startedAt = started ?? 101L }, baselineStartedAt = baseline ?? 100L } });
        static bool Apply(WorkItem item, JsonElement message, long sequence = 1, string epoch = "quick-epoch", bool live = true, int generation = 1) =>
            CodexState.Apply(item, item.Stages[0].Id, message, sequence, generation, epoch, live);
        foreach (var status in new[] { "failed", "interrupted" })
        {
            var item = Item(); item.CurrentCodex!.PendingRequests.Add("old-question"); item.CurrentCodex.PendingApprovals.Add("old-question");
            check(Apply(item, Stop(status)) && item.CurrentCodex.TurnId == "new-stop" && item.CurrentCodex.Phase == (status == "failed" ? CodexPhase.Failed : CodexPhase.Interrupted) && item.CurrentCodex.AwaitingUserAction,
                "快速" + status + "在同源时间水位后准确绑定新停止轮次并保护");
            check(item.Lane == Lane.Action && item.TimerUnread && item.CurrentCodex.PendingRequests.Count == 0 && item.CurrentCodex.PendingApprovals.Count == 0,
                "快速" + status + "清旧请求并移待操作，不虚构开始或清时间红点");
            item.Stages.Add(new Stage { Number = 1, IsRunning = true, Codex = new CodexBinding { ThreadId = "quick-b", TurnId = "b-turn", SourceEpoch = "quick-epoch", Phase = CodexPhase.Running } });
            var active = JsonSerializer.SerializeToElement(new { method = "probe/snapshot", @params = new { threadId = "quick-b", turnId = "b-turn", status = new { type = "active", activeFlags = Array.Empty<string>() } } });
            CodexState.Apply(item, item.Stages[1].Id, active, 1, 1, "quick-epoch", true);
            check(item.Lane == Lane.Action && item.TimerUnread, "快速" + status + "仍优先另一运行阶段，不被盖过");
            var store = new AtomicStore(Path.Combine(root, "quick-stop-" + status));
            await store.SaveAsync(new BoardState { Tasks = [item] }); item = store.Load().Tasks.Single();
            check(item.CurrentCodex!.TurnId == "new-stop" && item.CurrentCodex.AwaitingUserAction && item.Lane == Lane.Action && item.TimerUnread,
                "快速" + status + "通过正式存储重读保持轮次保护和红点");
            var started = JsonSerializer.SerializeToElement(new { method = "turn/started", @params = new { threadId = "quick-a", turn = new { id = "actually-resumed" } } });
            Apply(item, started, 2);
            check(item.Lane == Lane.Running && !item.TimerUnread && !item.CurrentCodex.AwaitingUserAction,
                "快速" + status + "后的真实新轮次恢复整卡才清红点");
        }
        var rejected = new[]
        {
            Stop(status: "completed"), Stop(status: "unknown"), Stop(id: ""), Stop(id: "observed-turn"),
            Stop(started: 100L), Stop(started: 99L), Stop(started: -1L), Stop(baseline: -1L),
            Stop(started: 253402300800L), Stop(baseline: 253402300800L), Stop(started: "101"), Stop(baseline: "100"),
            Stop(started: 101.5), Stop(baseline: 100.5), Stop(thread: "other-thread"),
            JsonSerializer.SerializeToElement(new { method = "probe/currentTurnStopped", @params = new { threadId = "quick-a", turn = new { id = "new-stop", status = "failed", startedAt = 101L } } }),
            JsonSerializer.SerializeToElement(new { method = "probe/currentTurnStopped", @params = new { threadId = "quick-a", turn = new { id = "new-stop", status = "failed" }, baselineStartedAt = 100L } })
        };
        foreach (var signal in rejected)
        {
            var item = Item(); var before = JsonSerializer.Serialize(item);
            check(!Apply(item, signal) && JsonSerializer.Serialize(item) == before,
                "拒绝缺失/不准确水位或非新明确停止：" + signal.GetRawText());
        }
        foreach (var source in new[] { "", "another-epoch" })
        {
            var item = Item(); item.CurrentCodex!.SourceEpoch = source; var before = JsonSerializer.Serialize(item);
            check(!Apply(item, Stop()) && JsonSerializer.Serialize(item) == before, "快速停止拒绝初次历史或错epoch，不借机重置绑定：" + source);
        }
        foreach (var turn in new string?[] { null, "" })
        {
            var item = Item(); item.CurrentCodex!.TurnId = turn; var before = JsonSerializer.Serialize(item);
            check(!Apply(item, Stop()) && JsonSerializer.Serialize(item) == before, "快速停止缺少已观察当前轮次时不猜测");
        }
        var task = Item(); var unchanged = JsonSerializer.Serialize(task);
        check(!Apply(task, Stop(), live: false) && !Apply(task, Stop(), generation: 2) && JsonSerializer.Serialize(task) == unchanged,
            "快速停止拒绝历史来源及旧绑定代次");
        Apply(task, Stop()); task.Lane = Lane.Running;
        check(!Apply(task, Stop(), 2) && task.Lane == Lane.Running, "快速停止相同轮次不重放，即使序号更高");
        task = Item(); Apply(task, Stop(started: 253402300799L, baseline: 0L));
        check(task.Lane == Lane.Action && task.TimerUnread, "Unix秒水位接受明确的合法上下边界");
        task = Item(); task.Lane = Lane.Paused; Apply(task, Stop());
        check(task.Lane == Lane.Paused && task.CurrentCodex!.AwaitingUserAction, "快速停止不移动人工暂停列表");
        task = Item(); task.ColumnId = Guid.NewGuid(); Apply(task, Stop());
        check(task.Lane == Lane.Running && task.ColumnId is not null, "快速停止不移动自定义列表");
        task = Item(); task.TimerUnread = false; Apply(task, Stop());
        check(task.Lane == Lane.Action && !task.TimerUnread, "快速停止不会无故创建定时红点");
    }
}
