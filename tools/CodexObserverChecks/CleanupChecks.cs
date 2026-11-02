using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Taskboard.Core;
using Taskboard.Desktop;

// 0.9.30 checks target the 0.9.28 counterexamples, not historical test totals.
// The exact production follow policy accepts explicit clock values; no current
// Codex, process memory, network, mouse, or user configuration is involved.
internal static class CleanupChecks
{
    private const string Thread = "11111111-1111-4111-8111-111111111111";
    private static CodexActivity Row(string status = "inProgress", long bytes = 100, bool caught = true,
        string turn = "turn-a", long ticks = 100) => new(turn, status, 10, caught, bytes, ticks);
    private static CodexDesktopSnapshot Missing => new(default, [], false, "synthetic-no-snapshot", "local");
    private static CodexDesktopSnapshot Active(bool waiting = false, bool approval = false, bool executing = true)
    {
        var turns = new[] { new CodexTurnMetadata("turn-a", "inProgress") };
        var raw = JsonSerializer.SerializeToElement(new { type = "active", activeFlags = Array.Empty<string>() });
        var requests = waiting ? new[] { new CodexPendingRequest("request-a", approval) } : [];
        var status = CodexPendingBridge.Merge(raw, turns, new(Thread, "turn-a", executing, false, requests));
        return new(status, turns, true, "synthetic-live", "local");
    }
    private static CodexDesktopSnapshot Idle => new(JsonSerializer.SerializeToElement(new { type = "idle", activeFlags = Array.Empty<string>() }),
        [new("turn-a", "completed")], true, "synthetic-idle", "local");

    internal static async Task Run(string output)
    {
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }

        var policy = new CodexFollowPolicy();
        Check(policy.Poll(Row(), false, 100), "本机inProgress仅开启一次发现窗口");
        var deadline = policy.Deadline;
        Check(!policy.Poll(Row(), false, deadline), "F1：90秒硬截止确实停止未确认跟随");
        Check(!policy.Poll(Row(), false, deadline + 1000), "F1：同一条残留记录不能续期");
        policy.Disconnect();
        Check(!policy.Poll(Row(), false, deadline + 2000), "F1：重连不重新探测同一条未确认旧记录");
        Check(!policy.Poll(Row(bytes: 200), false, deadline + 3000), "F1：同轮额外写入不能重复订退来续期");

        policy = new();
        Check(!policy.Poll(Row("completed"), false, 100), "历史终态首次读取只作辅助，不开启跟随");
        Check(!policy.Poll(Row("completed", ticks: 101), false, 200), "F4：写时间改变而字节未增长不会开启跟随");
        Check(policy.Poll(Row("completed", bytes: 101, ticks: 102), false, 300), "已追平的新字节允许一次有界发现");
        Check(!policy.Poll(Row("completed", bytes: 101, ticks: 102), false, 300 + CodexObserver.ActivityWindowMs), "F3：收尾发现30秒到期即停止");

        policy = new();
        Check(!policy.Poll(Row("completed", caught: false), false, 100) &&
            !policy.Poll(Row("completed", bytes: 200, caught: false, ticks: 101), false, 200), "F4：落后投影即使增长也不跟随补齐");
        Check(!policy.Poll(null, true, 300) && !policy.Poll(null, false, 400), "F5：读取失败和缺记录都不会自行启动跟随");

        policy = new();
        Check(policy.Poll(Row(), false, 100), "真实长轮次先进入发现");
        policy.Observe(Active(), Row(), 200);
        Check(policy.Poll(Row(), false, 100 + 10 * CodexObserver.FollowMaxMs), "可信实时长轮次不被90秒一刀切截断");
        policy.Observe(Active(waiting: true, approval: true), Row(), 1_000_000);
        Check(policy.Poll(Row(), false, 2_000_000), "可信批准等待持续观察，不因等待时间长而丢恢复");
        policy.Observe(Idle, Row("completed"), 2_000_001);
        Check(!policy.IsFollowing && !policy.Poll(Row(), false, 2_000_002), "真实idle立即结束且残留inProgress不能重新打开");

        policy = new(); policy.Poll(Row(), false, 100); policy.Observe(Active(), Row(), 200);
        policy.Observe(Missing, Row(), 300);
        deadline = policy.Deadline;
        policy.Observe(Missing, Row(bytes: 500), 500);
        Check(policy.Deadline == deadline && !policy.Poll(Row(bytes: 600), false, deadline), "丢失已验证流只有固定宽限，不被缺帧或文件增长续期");

        policy = new(); policy.Poll(Row(), false, 100); policy.Observe(Active(waiting: true), Row(), 200); policy.Disconnect();
        Check(policy.Poll(Row(), false, 300), "已实时确认的在途轮次允许一次重连恢复");
        policy.Disconnect();
        Check(!policy.Poll(Row(), false, 400), "恢复未取得新实时证据不能反复重试旧轮次");

        policy = new(); policy.Poll(Row(), false, 100); policy.Observe(Active(waiting: true), Row(), 200);
        policy.Observe(Missing, Row(), 300); policy.Disconnect();
        Check(policy.Poll(Row(), false, 400), "可信在途先缺帧再断线仍有一次重连恢复资格");
        policy.Observe(Missing, Row(), 500); policy.Disconnect();
        Check(!policy.Poll(Row(), false, 600), "缺帧后的失败恢复不会重新授予重连资格");
        policy = new(); policy.Poll(Row(), false, 100); policy.Observe(Active(waiting: true), Row(), 200);
        policy.Observe(Missing, Row(), 300); policy.Disconnect(); policy.Poll(Row(), false, 400);
        policy.Observe(Active(waiting: true), Row(), 500); policy.Observe(Missing, Row(), 600); policy.Disconnect();
        Check(policy.Poll(Row(), false, 700), "恢复取得新可信在途证据后可承担下一次真实断线");
        policy.Observe(Idle, Row("completed"), 800); policy.Disconnect();
        Check(!policy.Poll(Row(), false, 900), "真实idle清除所有恢复资格，断线不复活旧轮次");

        // File-only input must never supply Running, clear a wait/reminder, or
        // expose a source as live. Diagnostics must not subscribe on their own.
        await using (var c = new Case(Row()))
        {
            var binding = c.Item.CurrentCodex!;
            binding.SourceEpoch = "previous-connection"; binding.TurnId = "turn-a";
            binding.Phase = CodexPhase.WaitingForInput; binding.AwaitingUserAction = true;
            c.Item.Lane = Lane.Action;
            c.Start(); await Until(() => c.Local.Calls >= 2);
            Check(binding.Phase == CodexPhase.Unknown && c.Item.Lane == Lane.Action && c.Item.TimerUnread && binding.AwaitingUserAction,
                "F2/F6：文件inProgress不伪造执行，跨重连无ID等待和提醒保留");
            Check(!c.Observer.Capabilities.Single().Live && !c.Messages.Contains("turn/started"), "F2：文件记录不冒充实时能力或执行开始");
            var calls = c.Local.Calls; var messages = c.Messages.Count;
            var peek = c.Observer.PeekSnapshot(Thread);
            Check(!peek.LiveCapable && c.Local.Calls == calls && c.Messages.Count == messages, "只读诊断没有缓存时不新增连接/跟随/状态事件");
        }
        await using (var c = new Case(null))
        {
            c.Source.Error = new IOException("synthetic unsupported schema");
            c.Start(); await Until(() => c.Local.Calls >= 1);
            Check(c.Local.History.All(x => x.Length == 0), "F5：实际观察循环读取异常也不登记follower");
        }
        await using (var c = new Case(null))
        {
            c.Start(); await Until(() => c.Local.Calls >= 1);
            c.Source.Current = Row("completed");
            await Until(() => c.Local.Calls >= 3);
            Check(c.Item.CurrentCodex!.Phase == CodexPhase.Unknown && c.Item.Lane == Lane.Running && !c.Messages.Contains("probe/turnProjected"),
                "F7：首次落库终态没有可信新旧边界时保持未确认，不把旧历史当新结果");
        }
        await using (var c = new Case(Row()))
        {
            c.Local.Current = Active(waiting: true, approval: true);
            c.Start(); await Until(() => c.Item.CurrentCodex!.Phase == CodexPhase.WaitingForApproval);
            Check(c.Item.Lane == Lane.Action && c.Item.TimerUnread && c.Item.CurrentCodex!.PendingApprovals.Contains("request-a"),
                "可信首帧批准等待原子移列并保留提醒");
            var calls = c.Local.Calls; var messages = c.Messages.Count;
            Check(c.Observer.PeekSnapshot(Thread).LiveCapable && c.Local.Calls == calls && c.Messages.Count == messages,
                "只读诊断复用既有结构快照，不新订阅或修改任务");
            c.Local.Current = Active(executing: false);
            await Until(() => c.Local.Calls > calls);
            Check(c.Item.Lane == Lane.Action && c.Item.TimerUnread && c.Item.CurrentCodex!.AwaitingUserAction,
                "只批准或请求消失仍不冒充真正执行恢复");
            c.Local.Current = Active();
            await Until(() => c.Item.Lane == Lane.Running);
            Check(!c.Item.TimerUnread && !c.Item.CurrentCodex!.AwaitingUserAction, "完整请求与真实执行证据恢复进行中并清提醒");
            // Real snapshot disappears exactly as a known turn completes on disk.
            c.Local.Current = Missing; c.Source.Current = Row("completed", bytes: 101, ticks: 101);
            await Until(() => c.Item.CurrentCodex!.Phase == CodexPhase.ReadyForReview);
            Check(c.Item.Lane == Lane.Action && c.Item.CurrentCodex!.TurnId == "turn-a", "曾实时确认过的同轮终态可以安全从文件补齐");
            Check(c.Observer.TurnReadCount == 0 && c.Observer.CompatibilityReadCount == 0, "新接入全程没有聊天/历史查询回退");
        }
        await using (var c = new Case(Row()))
        {
            c.Local.Current = Active(waiting: true, approval: true);
            c.Start(); await Until(() => c.Item.Lane == Lane.Action);
            var second = new WorkItem { Title = "合成同聊天第二绑定", Lane = Lane.Running, TimerUnread = true };
            second.Stages[0].IsRunning = true; second.Stages[0].Codex = new() { ThreadId = Thread };
            c.Add(second);
            await Until(() => second.CurrentCodex!.Phase == CodexPhase.WaitingForApproval);
            Check(second.Lane == Lane.Action && c.Item.Lane == Lane.Action && c.Observer.Capabilities.Single().PendingAvailable,
                "同聊天多绑定各自保留等待，能力汇总不被文件历史盖掉");
            c.Remove(c.Item);
            var count = c.Local.Calls; await Until(() => c.Local.Calls > count);
            Check(c.Local.History.Last().SequenceEqual(new[] { Thread }) && second.CurrentCodex!.AwaitingUserAction,
                "移除原绑定后同聊天有效绑定继续观察且不清等待");
            var oldBinding = second.CurrentCodex!.BindingId;
            second.Stages[0].Codex = new() { ThreadId = Thread };
            c.RefreshWatches();
            await Until(() => second.CurrentCodex!.Phase == CodexPhase.WaitingForApproval);
            Check(second.CurrentCodex!.BindingId != oldBinding && second.CurrentCodex.PendingRequests.SequenceEqual(new[] { "request-a" }),
                "新绑定依据当前完整快照重建请求，不继承旧绑定代次");
        }
        var retired = new WorkItem { Lane = Lane.Running }; retired.Stages[0].IsRunning = true;
        retired.Stages[0].Codex = new() { ThreadId = Thread };
        var projected = JsonSerializer.SerializeToElement(new { method = "probe/turnProjected", @params = new { threadId = Thread, ordinal = 20, turn = new { id = "unseen", status = "completed" } } });
        Check(!CodexState.Apply(retired, retired.Stages[0].Id, projected, 1, retired.CurrentCodex!.Generation, "synthetic", true) && retired.Lane == Lane.Running,
            "撤出的跨未知轮次文件事件被核心明确拒绝");

        await CheckTerminalReconnect(Check);
        foreach (var invalidation in new[] { "generation", "binding", "thread", "saved-history", "newer-turn", "editor-merge", "editor-rebind" })
        {
            var item = new WorkItem { Lane = Lane.Running, TimerUnread = true };
            item.Stages[0].IsRunning = true; item.Stages[0].Codex = new() { ThreadId = Thread };
            var binding = item.CurrentCodex!;
            JsonElement Message(string method, string turn, string? status = null) => JsonSerializer.SerializeToElement(new
            { method, @params = new { threadId = binding.ThreadId, turn = new { id = turn, status } } });
            CodexState.Apply(item, item.Stages[0].Id, Message("turn/started", "turn-a"), 1, binding.Generation, "old", true);
            CodexState.Apply(item, item.Stages[0].Id, Message("probe/disconnected", ""), 1, binding.Generation, "new", true);
            switch (invalidation)
            {
                case "generation": binding.Generation++; break;
                case "binding": binding.BindingId = Guid.NewGuid(); break;
                case "thread": binding.ThreadId = "22222222-2222-4222-8222-222222222222"; break;
                case "saved-history": item = JsonSerializer.Deserialize<WorkItem>(JsonSerializer.Serialize(item))!; binding = item.CurrentCodex!; break;
                case "newer-turn": CodexState.Apply(item, item.Stages[0].Id, Message("turn/started", "turn-b"), 2, binding.Generation, "new", true); break;
                case "editor-merge": case "editor-rebind":
                    var original = Copies.Of(item); var edited = Copies.Of(item); edited.Title = "编辑后";
                    if (invalidation == "editor-rebind") edited.Stages[0].Codex = new() { ThreadId = "33333333-3333-4333-8333-333333333333" };
                    item = BoardEngine.Merge(original, edited, item); binding = item.CurrentCodex!; break;
            }
            var accepted = CodexState.Apply(item, item.Stages[0].Id, Message("turn/completed", "turn-a", "completed"), 3, binding.Generation, "new", true);
            Check(accepted == (invalidation == "editor-merge"), invalidation == "editor-merge"
                ? "同绑定编辑合并保留内存中已确认轮次但不把该证据写入存档"
                : "重连终态证据不能跨越边界：" + invalidation);
        }

        var report = new { passed = checks.Count, checks, syntheticOnly = true, policyClockInjected = true,
            originalCodexConnected = false, chatQueries = 0, productAutoMovesKeptForVerifiedLive = true,
            unobservedShortTerminalSupported = false, strictZeroSideEffectsVerified = false };
        await File.WriteAllTextAsync(Path.Combine(output, "cleanup-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, syntheticOnly = true }));
    }
    private static async Task Until(Func<bool> predicate)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate()) { if (clock.ElapsedMilliseconds > 7000) throw new TimeoutException("synthetic condition not reached"); await Task.Delay(20); }
    }
    private static async Task CheckTerminalReconnect(Action<bool, string> check)
    {
        var item = new WorkItem { Lane = Lane.Running, TimerUnread = true };
        item.Stages[0].IsRunning = true; item.Stages[0].Codex = new() { ThreadId = Thread };
        var binding = item.CurrentCodex!;
        var source = new Source(Row()); var first = new Local { Current = Active(waiting: true) }; var second = new Local();
        var connections = 0;
        await using var observer = new CodexObserver(localConnect: _ => Task.FromResult<ICodexLocalEvents>(Interlocked.Increment(ref connections) == 1 ? first : second),
            reconnectInterval: TimeSpan.FromMilliseconds(50), activitySource: () => source);
        observer.Event += (watch, message, sequence, epoch, live) => CodexState.Apply(item, watch.StageId, message, sequence, watch.Generation, epoch, live);
        observer.Watch([new(item.Id, item.Stages[0].Id, binding.BindingId, Thread, binding.Generation)]);
        observer.Start("", "");
        await Until(() => binding.Phase == CodexPhase.WaitingForInput);
        var oldEpoch = binding.SourceEpoch;
        first.Connected = false;
        await Until(() => connections >= 2 && binding.SourceEpoch != oldEpoch && binding.TurnId is null);
        check(binding.AwaitingUserAction && item.Lane == Lane.Action && item.TimerUnread, "跨重连未知帧保留等待和提醒");
        source.Current = Row("completed", bytes: 101, ticks: 101);
        await Until(() => binding.Phase == CodexPhase.ReadyForReview);
        check(binding.TurnId == "turn-a" && item.Lane == Lane.Action && binding.AwaitingUserAction,
            "跨重连先未知后同轮文件终态仍被核心接收");
        var calls = second.Calls;
        await Until(() => second.Calls > calls);
        check(binding.Phase == CodexPhase.ReadyForReview, "已接收同轮终态在后续文件轮询保持稳定");
    }
    private sealed class Source(CodexActivity? initial) : ICodexActivitySource
    {
        internal volatile CodexActivity? Current = initial;
        internal Exception? Error;
        public IReadOnlyDictionary<string, CodexActivity> Read(IReadOnlyCollection<string> ids)
        {
            if (Error is { } error) throw error;
            return Current is { } current ? new Dictionary<string, CodexActivity> { [Thread] = current } : new Dictionary<string, CodexActivity>();
        }
    }
    private sealed class Local : ICodexLocalEvents
    {
        internal volatile CodexDesktopSnapshot Current = Missing;
        internal readonly ConcurrentQueue<string[]> History = new();
        internal int Calls => History.Count;
        public long Frames => 0;
        public bool Connected { get; set; } = true;
        public event Action<string>? Changed { add { } remove { } }
        public Task Subscribe(string[] ids) { History.Enqueue(ids.ToArray()); return Task.CompletedTask; }
        public CodexDesktopSnapshot Snapshot(string id) => Current;
        public ValueTask DisposeAsync() { Connected = false; return ValueTask.CompletedTask; }
    }
    private sealed class Case : IAsyncDisposable
    {
        internal readonly Source Source;
        internal readonly Local Local = new();
        internal readonly CodexObserver Observer;
        internal readonly WorkItem Item = new() { Title = "合成0.9.30接入检查", Lane = Lane.Running, TimerUnread = true };
        internal readonly ConcurrentQueue<string> Messages = new();
        private readonly ConcurrentDictionary<Guid, WorkItem> items = new();
        internal Case(CodexActivity? row)
        {
            Source = new(row); Item.Stages[0].IsRunning = true; Item.Stages[0].Codex = new() { ThreadId = Thread };
            Observer = new(localConnect: _ => Task.FromResult<ICodexLocalEvents>(Local), activitySource: () => Source);
            Observer.Event += (watch, message, sequence, epoch, live) =>
            {
                Messages.Enqueue(CodexState.Text(message, "method"));
                if (items.TryGetValue(watch.TaskId, out var item) && item.CurrentCodex?.BindingId == watch.BindingId)
                    CodexState.Apply(item, watch.StageId, message, sequence, watch.Generation, epoch, live);
            };
            Add(Item);
        }
        internal void Add(WorkItem item) { items[item.Id] = item; RefreshWatches(); }
        internal void Remove(WorkItem item) { items.TryRemove(item.Id, out _); RefreshWatches(); }
        internal void RefreshWatches() => Observer.Watch(items.Values.Select(item => new CodexWatch(item.Id, item.Stages[0].Id,
            item.CurrentCodex!.BindingId, Thread, item.CurrentCodex.Generation)).ToArray());
        internal void Start() => Observer.Start("", "");
        public async ValueTask DisposeAsync() { await Observer.DisposeAsync(); await Local.DisposeAsync(); }
    }
}
