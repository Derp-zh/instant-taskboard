using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Taskboard.Core;
using Taskboard.Desktop;

if (args.Length != 2 || args[0] != "--data-dir") throw new ArgumentException("必须指定隔离 --data-dir");
var output = Path.GetFullPath(args[1]);
if (!output.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
    throw new ArgumentException("必须使用test-output下新的隔离目录");
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string text) { if (!value) throw new Exception(text); checks.Add(text); }
async Task Until(Func<bool> condition, string text, int timeout = 10000)
{
    var clock = Stopwatch.StartNew();
    while (!condition()) { if (clock.ElapsedMilliseconds > timeout) throw new TimeoutException(text); await Task.Delay(25); }
}
await using var fixture = new Fixture();
await using var observer = new CodexObserver();
var task = new WorkItem { Title = "仅合成观察器回归", Lane = Lane.Running, TimerUnread = true };
task.Stages[0].IsRunning = true; task.Stages[0].Codex = new CodexBinding { ThreadId = "fixture-a" };
var taskB = new WorkItem { Title = "合成交错B", Lane = Lane.Running };
taskB.Stages[0].IsRunning = true; taskB.Stages[0].Codex = new CodexBinding { ThreadId = "fixture-b" };
var missing = new WorkItem { Title = "合成未加载", Lane = Lane.Running };
missing.Stages[0].IsRunning = true; missing.Stages[0].Codex = new CodexBinding { ThreadId = "fixture-missing" };
static CodexWatch Watch(WorkItem item) => new(item.Id, item.Stages[0].Id, item.CurrentCodex!.BindingId, item.CurrentCodex.ThreadId, item.CurrentCodex.Generation);
var items = new[] { task, taskB, missing }; var gate = new object();
var events = new ConcurrentQueue<(string Thread, string Method, bool Live)>();
observer.Event += (watch, message, sequence, epoch, live) =>
{
    lock (gate)
    {
        events.Enqueue((watch.ThreadId, CodexState.Text(message, "method"), live));
        var item = items.FirstOrDefault(x => x.Id == watch.TaskId);
        if (item is not null && item.CurrentCodex?.BindingId == watch.BindingId)
            CodexState.Apply(item, watch.StageId, message, sequence, watch.Generation, epoch, live);
    }
};
observer.Watch(items.Select(Watch).ToArray());
observer.Start("", fixture.Endpoint, discoverDesktop: false);
await Until(() => observer.Capabilities.Length == 3, "首次逐会话能力未就绪");
lock (gate)
{
    Check(observer.TransportConnected && observer.SourceKind == "shared-service" && observer.Authoritative && !observer.SourceIdentityVerified,
        "共享传输与逐会话live分别报告、不冒称原桌面身份");
    Check(task.Lane == Lane.Running && task.CurrentCodex!.Phase == CodexPhase.Unknown && task.TimerUnread,
        "首次idle仅有旧完成记录不误移列或清红点");
    Check(taskB.Lane == Lane.Action && taskB.CurrentCodex!.Phase == CodexPhase.WaitingForApproval,
        "交错B批准等待准确归属B、不污染A");
    Check(observer.Capabilities.Single(x => x.ThreadId == "fixture-missing") is { Live: false } && observer.Authoritative,
        "单个未加载会话不可用不杀其他会话实时能力");
}
fixture.Phase = 1;
await Until(() => task.CurrentCodex!.Phase == CodexPhase.Running, "当前真实轮次未观察");
Check(task.CurrentCodex!.TurnId == "current-a" && task.Lane == Lane.Running && task.TimerUnread,
    "active准确轮次合成开始、已在进行中不自行清时间红点");
fixture.Phase = 2;
await Until(() => task.CurrentCodex!.Phase == CodexPhase.WaitingForInput, "问题等待未传播");
Check(task.Lane == Lane.Action && task.TimerUnread, "等待问题进入待操作且保留红点");
await fixture.Notify(new { method = "serverRequest/resolved", @params = new { threadId = "fixture-a", turnId = "current-a", requestId = "question" } });
await Until(() => events.Any(x => x.Method == "serverRequest/resolved"), "原生resolved通知未转发");
Check(task.Lane == Lane.Action && task.TimerUnread && task.CurrentCodex!.Phase == CodexPhase.WaitingForInput,
    "原生请求resolved本身不冒称执行恢复");
fixture.Phase = 3;
await Until(() => task.Lane == Lane.Running, "实际恢复未移回进行中");
Check(!task.TimerUnread && taskB.Lane == Lane.Action, "可信恢复清A红点且不覆盖B等待");
fixture.Phase = 4;
await Until(() => task.CurrentCodex!.Phase == CodexPhase.ReadyForReview, "缺完成事件终态未补查");
Check(task.Lane == Lane.Action && observer.ReconciledTerminalCount == 1 && observer.TurnReadCount > 0,
    "没有完成通知仍由准确当前轮次补查移待操作");
var terminalCount = events.Count(x => x.Method == "turn/completed");
await Task.Delay(1700);
Check(observer.ReconciledTerminalCount == 1 && events.Count(x => x.Method == "turn/completed") == terminalCount,
    "重复idle补查当前终态幂等、不重复结束");
lock (gate)
{
    task.CurrentCodex!.BindingId = Guid.NewGuid(); task.CurrentCodex.Generation++;
    task.CurrentCodex.Phase = CodexPhase.Unknown; task.CurrentCodex.TurnId = null;
    task.CurrentCodex.SourceEpoch = ""; task.CurrentCodex.LastSequence = -1;
    task.Lane = Lane.Running; task.TimerUnread = true;
    observer.Watch(items.Select(Watch).ToArray());
}
var beforePoll = observer.PollCount;
await Until(() => observer.PollCount >= beforePoll + 6, "重绑后轮询未发生");
Check(task.Lane == Lane.Running && task.TimerUnread && task.CurrentCodex!.Phase == CodexPhase.Unknown,
    "解绑重绑销毁旧轮次记忆、旧完成记录不重放");
fixture.Phase = 5;
await Until(() => task.CurrentCodex!.TurnId == "next-a" && task.CurrentCodex.Phase == CodexPhase.Running, "重绑后新轮次未识别");
var priorEpoch = observer.Epoch;
fixture.Phase = 6; await fixture.DropConnection();
await Until(() => observer.Epoch != priorEpoch && observer.TransportConnected, "传输重连未完成", 18000);
await Until(() => task.CurrentCodex!.Phase == CodexPhase.ReadyForReview, "重连后已观察当前轮次终态未对账");
Check(task.Lane == Lane.Action && observer.ReconciledTerminalCount == 2,
    "重连保留绑定当前轮次、补查准确终态且不重放旧轮次");
await fixture.Notify(new { method = "probe/currentTurnStopped", @params = new { threadId = "fixture-a", baselineStartedAt = 1,
    turn = new { id = "injected-failed", status = "failed", startedAt = 2 } } });
await Task.Delay(350);
Check(task.CurrentCodex!.TurnId == "next-a" && !events.Any(x => x.Method == "probe/currentTurnStopped"),
    "外部共享通知不能伪造观察器内部快停止事件");
Check(fixture.Requests.All(x => CodexRpc.Allowed(x)) && !fixture.Requests.Contains("turn/start"),
    "观察器只读、未接管聊天或启动轮次");
Check(fixture.TurnArguments.All(x => x.Direction == "desc" && x.View == "notLoaded"),
    "轮次补查使用已核实降序和notLoaded元数据协议");
// Target the race deterministically: a native notification arriving during a read
// must invalidate the complete earlier poll, even its otherwise credible snapshot.
await using (var race = new CodexObserver())
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var type = typeof(CodexObserver); var watch = Watch(task);
    race.Watch([watch]);
    type.GetField("epoch", flags)!.SetValue(race, "race-epoch");
    type.GetProperty("SourceKind", flags)!.SetValue(race, "shared-service");
    var dictionary = (System.Collections.IDictionary)type.GetField("trackers", flags)!.GetValue(race)!;
    var state = dictionary[watch]!;
    var tracker = (CodexTurnTracker)state.GetType().GetField("Tracker", flags)!.GetValue(state)!;
    var oldRevision = tracker.Revision;
    var notified = JsonSerializer.SerializeToElement(new { method = "turn/started", @params = new { threadId = watch.ThreadId, turn = new { id = "notification-current" } } });
    tracker.ObserveNotification(notified, true);
    var count = 0; var allHistorical = true;
    race.Event += (_, _, _, _, live) => { count++; allHistorical &= !live; };
    var status = JsonSerializer.SerializeToElement(new { type = "active", activeFlags = Array.Empty<string>() });
    var apply = type.GetMethod("ApplyPoll", flags)!;
    var accepted = (bool)apply.Invoke(race, [watch, state, oldRevision, status, new[] { new CodexTurnMetadata("stale-poll-turn", "inProgress") }, true, "race-epoch"])!;
    Check(!accepted && count == 0 && tracker.CurrentTurnId == "notification-current",
        "读中收到新事件使整批旧轮询含snapshot全部丢弃");
    type.GetProperty("SourceKind", flags)!.SetValue(race, "historical");
    apply.Invoke(race, [watch, state, tracker.Revision, status, Array.Empty<CodexTurnMetadata>(), false, "race-epoch"]);
    Check(count == 1 && allHistorical, "历史只读降级的快照始终非权威、不合成运行事件");
    count = 0;
    apply.Invoke(race, [watch, state, tracker.Revision, status, Array.Empty<CodexTurnMetadata>(), true, "retired-epoch"]);
    Check(count == 0, "旧连接epoch的整批轮询不能污染新连接");
    race.Watch([]);
    apply.Invoke(race, [watch, state, tracker.Revision, status, Array.Empty<CodexTurnMetadata>(), true, "race-epoch"]);
    Check(count == 0 && dictionary.Count == 0, "解绑删除Tracker并拒绝解绑前已在途的轮询");
}
// Native chronology is exercised with synthetic polls only. This is not a
// claim that the original desktop's quota rejection has been observed.
await using (var native = new CodexObserver())
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var type = typeof(CodexObserver);
    var item = new WorkItem { Title = "合成本机元数据", Lane = Lane.Running, TimerUnread = true };
    item.Stages[0].IsRunning = true; item.Stages[0].Codex = new CodexBinding { ThreadId = "fixture-native" };
    var watch = Watch(item); native.Watch([watch]);
    type.GetField("epoch", flags)!.SetValue(native, "native-fixture-epoch");
    type.GetProperty("SourceKind", flags)!.SetValue(native, "desktop-native");
    var dictionary = (System.Collections.IDictionary)type.GetField("trackers", flags)!.GetValue(native)!;
    var state = dictionary[watch]!;
    var tracker = (CodexTurnTracker)state.GetType().GetField("Tracker", flags)!.GetValue(state)!;
    var emitted = new List<string>();
    native.Event += (_, message, sequence, epoch, live) => {
        emitted.Add(CodexState.Text(message, "method"));
        CodexState.Apply(item, watch.StageId, message, sequence, watch.Generation, epoch, live);
    };
    var apply = type.GetMethod("ApplyPoll", flags)!;
    void Poll(string status, params CodexTurnMetadata[] turns) => apply.Invoke(native, [watch, state, tracker.Revision,
        JsonSerializer.SerializeToElement(new { type = status, activeFlags = Array.Empty<string>() }), turns, true, "native-fixture-epoch"]);
    Poll("active", new CodexTurnMetadata("known-native", "inProgress", StartedAt: 100));
    Check(item.CurrentCodex!.Phase == CodexPhase.Running && item.TimerUnread, "观察到运行不等于从待处理真实恢复");
    Poll("idle", new CodexTurnMetadata("known-native", "failed", "usageLimitExceeded", 100));
    Check(item.Lane == Lane.Action && item.CurrentCodex!.Phase == CodexPhase.Failed && item.CurrentCodex.AwaitingUserAction && item.TimerUnread,
        "合成准确当前失败经观察器整链入待操作并保留红点");
    Poll("active", new CodexTurnMetadata("known-native", "failed", StartedAt: 100));
    Check(item.Lane == Lane.Action && item.CurrentCodex!.Phase == CodexPhase.Unknown && item.CurrentCodex.AwaitingUserAction && item.TimerUnread,
        "终态之后的迟到active不能伪造恢复或清提醒");
    Poll("idle", new CodexTurnMetadata("fast-native", "failed", "usageLimitExceeded", 200));
    Check(item.CurrentCodex!.TurnId == "fast-native" && item.CurrentCodex.Phase == CodexPhase.Failed && item.CurrentCodex.AwaitingUserAction,
        "同epoch准确递增最新失败经观察器快停止分支保护");
    Check(emitted.Count(x => x == "turn/started") == 1 && emitted.Count(x => x == "probe/currentTurnStopped") == 1 && item.TimerUnread,
        "快停止不伪造开始也不清提醒");
    Poll("idle", new CodexTurnMetadata("fast-native", "failed", StartedAt: 200));
    Check(native.ReconciledTerminalCount == 2 && emitted.Count(x => x == "probe/currentTurnStopped") == 1, "快停止重复快照幂等");
    Poll("active", new CodexTurnMetadata("recovered-native", "inProgress", StartedAt: 300));
    Check(item.Lane == Lane.Running && !item.TimerUnread && !item.CurrentCodex!.AwaitingUserAction,
        "之后实际新轮次恢复才回进行中并清提醒");
}
await PendingChecks.Run(Check, output);
await PendingTransportChecks.Run(Check);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed = checks.Count, checks, syntheticFixture = true, desktopLifecycleVerified = false,
    observer.SourceKind, observer.TransportConnected, observer.SourceIdentityVerified, observer.PollCount,
    observer.TurnReadCount, observer.ReconciledTerminalCount,
    readOnlyRequests = fixture.Requests.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count()),
    sourceHashes = new[] { "CodexObserver.cs", "CodexRpc.cs", "CodexTurnTracker.cs", "CodexDesktopBridge.cs", "CodexPendingBridge.cs" }.ToDictionary(x => x,
        x => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine("src/Taskboard.Desktop", x)))))
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, syntheticFixture = true, desktopLifecycleVerified = false }));

internal sealed class Fixture : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim writes = new(1, 1);
    private WebSocket? socket;
    private readonly Task worker;
    private int phase;
    internal int Phase { get => Volatile.Read(ref phase); set => Volatile.Write(ref phase, value); }
    internal ConcurrentBag<string> Requests { get; } = [];
    internal ConcurrentBag<(string Direction, string View)> TurnArguments { get; } = [];
    internal string Endpoint { get; }
    internal Fixture()
    {
        var reserve = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); reserve.Start();
        var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        Endpoint = $"ws://127.0.0.1:{port}/"; worker = Task.Run(Run);
    }
    private async Task Run()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                var context = await listener.GetContextAsync().WaitAsync(lifetime.Token);
                var accepted = await context.AcceptWebSocketAsync(null); socket = accepted.WebSocket;
                while (socket.State == WebSocketState.Open)
                {
                    var bytes = new byte[16384]; var result = await socket.ReceiveAsync(bytes.AsMemory(), lifetime.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    using var json = JsonDocument.Parse(bytes.AsMemory(0, result.Count)); var request = json.RootElement;
                    if (!request.TryGetProperty("id", out var id)) continue;
                    var method = request.GetProperty("method").GetString()!; Requests.Add(method);
                    var parameters = request.GetProperty("params");
                    var thread = CodexState.Text(parameters, "threadId"); var value = Phase;
                    var type = value is 0 or 4 or 6 ? "idle" : "active";
                    var flags = value == 2 ? new[] { "waitingOnUserInput" } : Array.Empty<string>();
                    if (thread == "fixture-b") { type = "active"; flags = ["waitingOnApproval"]; }
                    object response;
                    if (method == "initialize") response = new { userAgent = "Synthetic observer fixture" };
                    else if (method == "thread/loaded/list") response = new { data = new[] { "fixture-a", "fixture-b" } };
                    else if (method == "thread/read") response = new { thread = new { id = thread, status = new { type = thread == "fixture-missing" ? "notLoaded" : type, activeFlags = flags } } };
                    else if (method == "thread/turns/list")
                    {
                        TurnArguments.Add((CodexState.Text(parameters, "sortDirection"), CodexState.Text(parameters, "itemsView")));
                        var turn = value >= 5 ? "next-a" : "current-a";
                        var status = value is 4 or 6 ? "completed" : "inProgress";
                        response = new { data = thread == "fixture-b" ? new[] { new { id = "current-b", status = "inProgress" } } : value == 0 ? new[] { new { id = "older-a", status = "completed" } } : new[] { new { id = turn, status }, new { id = "older-a", status = "completed" } }, nextCursor = (string?)null };
                    }
                    else response = new { data = Array.Empty<object>() };
                    await Notify(new { id = id.GetInt32(), result = response });
                }
            }
            catch (Exception) when (!lifetime.IsCancellationRequested) { }
        }
    }
    internal async Task Notify(object value)
    {
        await writes.WaitAsync(lifetime.Token);
        try { if (socket is { State: WebSocketState.Open } current) await current.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value), WebSocketMessageType.Text, true, lifetime.Token); }
        finally { writes.Release(); }
    }
    internal Task DropConnection() { socket?.Abort(); return Task.CompletedTask; }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); socket?.Abort(); listener.Stop(); listener.Close();
        try { await worker; } catch (Exception e) when (e is OperationCanceledException or HttpListenerException or ObjectDisposedException) { }
    }
}
