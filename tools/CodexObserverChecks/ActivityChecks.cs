using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Taskboard.Core;
using Taskboard.Desktop;

/// <summary>Isolated checks for the file projection route: the turn record is
/// read from SQLite, nothing is subscribed while nothing runs, and the only
/// follower ever registered is the one for a chat that is really executing.</summary>
internal static class ActivityChecks
{
    private const string A = "11111111-1111-4111-8111-111111111111";
    private const string B = "22222222-2222-4222-8222-222222222222";
    private const string C = "33333333-3333-4333-8333-333333333333";
    private const string D = "44444444-4444-4444-8444-444444444444";
    private const string E = "55555555-5555-4555-8555-555555555555";
    private const string F = "66666666-6666-4666-8666-666666666666";
    private const string Z = "77777777-7777-4777-8777-777777777777";

    internal static async Task Run(string output)
    {
        var fixture = Environment.GetEnvironmentVariable("TASKBOARD_FILE_PROJECTION_FIXTURE")
            ?? throw new InvalidOperationException("必须由 tools/Verify-FileProjection.py 提供合成本机目录");
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }
        async Task Until(Func<bool> condition, string label, int timeout = 15000)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.ElapsedMilliseconds > timeout) throw new TimeoutException(label);
                await Task.Delay(25);
            }
        }
        string[] Fresh() => [];
        string Failure(Action action, string contains)
        {
            try { action(); }
            catch (IOException error) { Check(error.Message.Contains(contains, StringComparison.Ordinal), "失败说明可诊断：" + error.Message); return error.Message; }
            throw new Exception("预期明确失败，但没有失败：" + contains);
        }

        // ---- 1. Pristine fixture: pure read only, nothing is guessed ----------
        var readSource = new CodexFileActivity(fixture);
        var read = readSource.Read([A, B, C, D, E, F, Z, "not-a-chat-id", "99999999-9999-4999-8999-999999999999"]);
        Check(read.Count == 4, "只读投影只为同时具有本机轮次和 rollout 的聊天产出记录");
        Check(read[A] is { Status: "inProgress" } && read[A].ProjectionCaughtUp, "进行中轮次与已追尾投影被准确读出");
        Check(read[B] is { Status: "completed" } && read[B].ProjectionCaughtUp, "已完成轮次被准确读出");
        Check(read[D] is { ProjectionCaughtUp: false }, "落后于 rollout 文件的投影被明确标记为未追尾");
        Check(read[B].RolloutBytes > 0 && read[B].RolloutWriteTicks > 0, "rollout 字节数与写入时间被读取");
        Check(!read.ContainsKey(E) && !read.ContainsKey(F) && !read.ContainsKey(Z), "未知状态、缺轮次或缺目录行的聊天一律不猜测");
        Check(readSource.Read(Fresh()).Count == 0 && readSource.Read(["not-a-chat-id"]).Count == 0, "无有效聊天编号时不打开任何本机文件");
        Failure(() => new CodexFileActivity(Path.Combine(fixture, "missing-history")).Read([A]), "不订阅");
        Failure(() => new CodexFileActivity(Path.Combine(fixture, "unknown-schema")).Read([A]), "结构暂不支持");

        // ---- 2. Isolated working copy: only this copy is ever written ---------
        var work = Path.Combine(output, "work-home");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Copy(fixture, work);
        var stateFile = Path.Combine(work, "state_5.sqlite");
        var historyFile = Path.Combine(work, "thread_history_1.sqlite");
        // SQLite stores absolute rollout paths. Rebase the copied synthetic
        // database too; otherwise writes target work-home while the reader
        // silently stats the untouched original fixture.
        foreach (var (id, name) in new[] { (A, "a"), (B, "b"), (C, "c"), (D, "d"), (E, "e"), (F, "f") })
            Exec(stateFile, $"UPDATE threads SET rollout_path='{Path.Combine(work, "rollouts", name + ".jsonl").Replace("'", "''")}' WHERE id='{id}';");
        var rolloutA = Path.Combine(work, "rollouts", "a.jsonl");
        var rolloutC = Path.Combine(work, "rollouts", "c.jsonl");
        var originalWrite = File.GetLastWriteTimeUtc(rolloutC);

        await using var local = new Local();
        await using var observer = new CodexObserver(TimeSpan.FromMilliseconds(150),
            localConnect: _ => Task.FromResult<ICodexLocalEvents>(local),
            reconnectInterval: TimeSpan.FromMilliseconds(80),
            activitySource: () => new CodexFileActivity(work));
        var itemA = Bind(A, "文件投影进行中");
        var itemC = Bind(C, "文件投影新终态");
        var itemD = Bind(D, "文件投影落后");
        var items = new List<WorkItem> { itemA, itemC, itemD };
        var gate = new object();
        var methods = new List<string>();
        observer.Event += (watch, message, sequence, epoch, live) =>
        {
            lock (gate)
            {
                methods.Add(CodexState.Text(message, "method"));
                var item = items.FirstOrDefault(x => x.Id == watch.TaskId);
                if (item is not null && item.CurrentCodex?.BindingId == watch.BindingId)
                    CodexState.Apply(item, watch.StageId, message, sequence, watch.Generation, epoch, live);
            }
        };
        observer.Watch(items.Select(Watch).ToArray());
        observer.Start("", "");

        await Until(() => Capability(observer, A) is not null, "逐会话能力未就绪");
        lock (gate) Check(itemC.CurrentCodex!.Phase == CodexPhase.Unknown && itemD.CurrentCodex!.Phase == CodexPhase.Unknown,
            "首次观察不把绑定前的旧终态或落后投影当成当前状态");
        lock (gate) Check(itemA.CurrentCodex!.Phase is CodexPhase.Unknown or CodexPhase.Running,
            "进行中的聊天只可能是未知或进行中，不会被臆测为其它状态");
        Check(Capability(observer, D)!.Reason == "local-file-projection-behind" && !Capability(observer, D)!.Live,
            "投影落后明确不可用、不猜测当前轮次");
        await Until(() => local.EverSubscribed(A), "进行中的聊天未进入有界跟随");
        Check(Capability(observer, C) is { Live: false } && observer.ReadMode == "local-events", "文件历史不冒充聊天实时能力");
        await Until(() => itemA.CurrentCodex!.Phase == CodexPhase.Running && itemA.CurrentCodex.TurnId == "t-a",
            "有活动轮次的聊天未进入进行中", 20000);
        lock (gate) Check(itemA.Lane == Lane.Running && itemA.TimerUnread, "进行中轮次准确移列且不自行清红点");
        Check(!local.EverSubscribed(C) && !local.EverSubscribed(D), "空闲聊天与落后投影的聊天从未登记 follower");

        // Missing live evidence stays unknown. A persisted inProgress row must
        // not manufacture execution or clear a previously confirmed reminder.
        local.Quiet(A);
        await Task.Delay(1500);
        lock (gate) Check(itemA.CurrentCodex!.Phase == CodexPhase.Unknown && itemA.Lane == Lane.Running && itemA.TimerUnread,
            "流快照暂不可用时不伪报执行，并保留原列与提醒");
        Check(local.Last.Contains(A), "流快照暂不可用时不放弃有界跟随");
        local.Listen(A);

        // The turn really ends: the rollout grows, the record says completed, and
        // the live stream reports idle. Following must stop at once.
        File.AppendAllText(rolloutA, new string('x', 100) + "\n");
        Exec(historyFile, $"UPDATE thread_turns SET status='completed' WHERE thread_id='{A}';" +
            $"UPDATE thread_history_projection_state SET next_rollout_byte_offset={new FileInfo(rolloutA).Length} WHERE thread_id='{A}';");
        local.Idle(A);
        await Until(() => !local.Last.Contains(A), "收到真实 idle 后未立即退订", 20000);
        await Until(() => itemA.CurrentCodex!.Phase == CodexPhase.ReadyForReview, "由文件投影补齐的完成状态未进入待操作", 20000);
        lock (gate) Check(itemA.Lane == Lane.Action && itemA.CurrentCodex.TurnId == "t-a" && itemA.TimerUnread,
            "轮次完成后移入待操作并保留提醒");
        Check(observer.ReadMode == "local-file-projection" && observer.Capabilities.All(x => !x.Live),
            "空闲期没有订阅且文件历史不冒充实时可读");
        Check(!local.Last.Contains(A), "退订后订阅列表不再包含已结束的聊天");

        // A newly visible row can also be a delayed historical projection.
        // Without a live-observed turn, it must not move a card.
        Exec(historyFile, $"UPDATE thread_turns SET turn_id='t-c-new',status='completed',rollout_ordinal=2 WHERE thread_id='{C}';");
        File.SetLastWriteTimeUtc(rolloutC, originalWrite);
        await Task.Delay(1500);
        lock (gate) Check(itemC.CurrentCodex!.Phase == CodexPhase.Unknown && itemC.Lane == Lane.Running,
            "从未实时确认的文件终态保持未确认，不把延迟历史当新结果");
        lock (gate) Check(!methods.Contains("probe/turnProjected"), "撤出没有可信绑定边界的跨轮次终态事件");

        // Rebinding a chat whose terminal turn predates the binding must not move
        // the new card: an old terminal is not a new result.
        var rebind = Bind(A, "文件投影重绑");
        items.Add(rebind);
        observer.Watch(items.Select(Watch).ToArray());
        await Task.Delay(2500);
        lock (gate) Check(rebind.CurrentCodex!.Phase == CodexPhase.Unknown && rebind.Lane == Lane.Running && rebind.TimerUnread,
            "重绑不因绑定前的旧终态移列或清红点");
        Check(observer.TurnReadCount == 0 && observer.CompatibilityReadCount == 0, "全程没有 thread/read 或轮次查询");
        Check(local.History.All(x => x.All(id => id is A or C or D)), "订阅范围只包含已绑定的聊天");
        Check(local.History.All(x => x.Length <= 1), "同时最多跟随一个正在执行的聊天");
        await observer.DisposeAsync();
        await local.DisposeAsync();
        var report = new { passed = checks.Count, checks, readMode = observer.ReadMode, subscriptions = local.SubscriptionCount,
            chatQueries = observer.CompatibilityReadCount, turnQueries = observer.TurnReadCount, fileWrites = 3,
            syntheticHome = true, syntheticWorkHomeRebased = true, realCodexTouched = false };
        await File.WriteAllTextAsync(Path.Combine(output, "file-projection.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
    }

    private static CodexThreadCapability? Capability(CodexObserver observer, string threadId) =>
        observer.Capabilities.FirstOrDefault(x => x.ThreadId == threadId);
    private static WorkItem Bind(string threadId, string title)
    {
        var item = new WorkItem { Title = title, Lane = Lane.Running, TimerUnread = true };
        item.Stages[0].IsRunning = true; item.Stages[0].Codex = new CodexBinding { ThreadId = threadId };
        return item;
    }
    private static CodexWatch Watch(WorkItem item) =>
        new(item.Id, item.Stages[0].Id, item.CurrentCodex!.BindingId, item.CurrentCodex.ThreadId, item.CurrentCodex.Generation);
    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(from)) Copy(directory, Path.Combine(to, Path.GetFileName(directory)));
    }
    private static void Exec(string path, string sql)
    {
        var code = Native.sqlite3_open_v2(path, out var db, 2, IntPtr.Zero);
        if (code != 0) throw new IOException($"合成夹具无法写入（代码 {code}）");
        try
        {
            var result = Native.sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (result != 0) throw new IOException($"合成夹具语句失败（代码 {result}）：{sql}");
        }
        finally { Native.sqlite3_close(db); }
    }
    /// <summary>Read-only probe against the real local Codex home. It only
    /// queries structure (no chat text) and stats rollout files, so it can
    /// show whether the shipped file route works on this machine.</summary>
    internal static async Task Probe(string output)
    {
        var ids = (Environment.GetEnvironmentVariable("TASKBOARD_ACTIVITY_PROBE_IDS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var source = new CodexFileActivity();
        var rows = new List<object>();
        string? error = null;
        try
        {
            var found = source.Read(ids);
            foreach (var id in ids)
            {
                rows.Add(found.TryGetValue(id, out var a)
                    ? new { id, status = (string?)a.Status, turnId = (string?)a.TurnId, ordinal = a.Ordinal, caughtUp = a.ProjectionCaughtUp, bytes = a.RolloutBytes, writeTicks = a.RolloutWriteTicks }
                    : new { id, status = (string?)null, turnId = (string?)null, ordinal = -1L, caughtUp = false, bytes = 0L, writeTicks = 0L });
            }
        }
        catch (Exception problem) { error = problem.GetType().Name + ": " + problem.Message; }
        var report = new { requested = ids.Length, produced = rows.Count(x => x.GetType().GetProperty("status")!.GetValue(x) is string), rows, error, realHome = true, chatTextRead = false, networkUsed = false };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        await File.WriteAllTextAsync(Path.Combine(output, "activity-probe.json"), json);
        Console.WriteLine(json);
    }
    private static class Native
    {
        private const string Library = "winsqlite3.dll";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)] internal static extern int sqlite3_close(IntPtr db);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr argument, IntPtr message);
    }

    /// <summary>Synthetic local event stream: it records every subscription and
    /// only ever answers with a real idle snapshot for a chat the test closed.</summary>
    private sealed class Local : ICodexLocalEvents
    {
        private static readonly JsonElement IdleStatus = JsonSerializer.SerializeToElement(new { type = "idle", activeFlags = Array.Empty<string>() });
        private static readonly JsonElement Active = JsonSerializer.SerializeToElement(new { type = "active", activeFlags = Array.Empty<string>() });
        private readonly object gate = new();
        private readonly Dictionary<string, string> running = new(StringComparer.Ordinal) { [A] = "t-a" };
        private readonly HashSet<string> idle = new(StringComparer.Ordinal);
        private readonly HashSet<string> quiet = new(StringComparer.Ordinal);
        private readonly List<string[]> subscriptions = [];
        internal int SubscriptionCount { get { lock (gate) return subscriptions.Count; } }
        internal IReadOnlyList<string[]> History { get { lock (gate) return subscriptions.ToArray(); } }
        internal string[] Last { get { lock (gate) return subscriptions.Count == 0 ? [] : subscriptions[^1]; } }
        internal bool EverSubscribed(string id) { lock (gate) return subscriptions.Any(x => x.Contains(id)); }
        internal void Idle(string id) { lock (gate) { quiet.Remove(id); running.Remove(id); idle.Add(id); } }
        // Connected transport that cannot describe this chat yet.
        internal void Quiet(string id) { lock (gate) quiet.Add(id); }
        internal void Listen(string id) { lock (gate) quiet.Remove(id); }
        public long Frames => 0;
        public bool Connected { get; private set; } = true;
        public event Action<string>? Changed { add { } remove { } }
        public Task Subscribe(string[] ids)
        {
            lock (gate) subscriptions.Add([.. ids]);
            return Task.CompletedTask;
        }
        public CodexDesktopSnapshot Snapshot(string id)
        {
            lock (gate)
            {
                if (idle.Contains(id)) return new(IdleStatus, [], true, "local-cache-no-active-request", "local");
                if (quiet.Contains(id)) return new(default, [], false, "local-cache-unavailable", "local");
                return running.TryGetValue(id, out var turnId)
                    ? new(Active, [new CodexTurnMetadata(turnId, "inProgress")], true, "local-cache-active-turn", "local")
                    : new(default, [], false, "local-cache-unavailable", "local");
            }
        }
        public ValueTask DisposeAsync() { Connected = false; return ValueTask.CompletedTask; }
    }
}
