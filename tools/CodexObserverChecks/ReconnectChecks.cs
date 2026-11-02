using System.Diagnostics;
using System.Text.Json;
using Taskboard.Core;
using Taskboard.Desktop;

internal static class ReconnectChecks
{
    internal static async Task Run(string output)
    {
        var checks = new List<string>();
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks.Add(text); }
        async Task Until(Func<bool> predicate, string text)
        {
            var clock = Stopwatch.StartNew();
            while (!predicate()) { if (clock.ElapsedMilliseconds > 5000) throw new Exception(text); await Task.Delay(20); }
        }
        var first = new Reader("first"); var second = new Reader("second"); var connections = 0;
        await using (var catalog = new CodexCatalog(() => Task.FromResult<ICodexCatalogRpc>(++connections == 1 ? first : second)))
        {
            Check((await catalog.Load()).Single().Id == "first", "本地目录首次成功");
            first.Connected = false;
            Check((await catalog.Load()).Single().Id == "second" && first.Disposed && connections == 2, "已断开的目录reader不复用，重新发现并连接");
            Check(first.Methods.Concat(second.Methods).All(x => x == "thread/list"), "恢复目录从不读取聊天或轮次");
            Check(first.LocalOnly && second.LocalOnly, "目录限定useStateDbOnly本地元数据");
        }
        connections = 0;
        await using (var catalog = new CodexCatalog(() => ++connections == 1 ? throw new IOException("stale path") : Task.FromResult<ICodexCatalogRpc>(new Reader("recovered"))))
            Check((await catalog.Load()).Single().Id == "recovered" && connections == 2, "第一次连接失败自动重新发现并重试一次");
        var available = false; connections = 0;
        await using (var catalog = new CodexCatalog(() => { connections++; return available ? Task.FromResult<ICodexCatalogRpc>(new Reader("late")) : throw new IOException("not ready"); }))
        {
            try { await catalog.Load(); throw new Exception("expected unavailable"); } catch (IOException) { }
            Check(catalog.Failed && connections == 2, "持续失败有界重试并保留失败状态");
            available = true;
            Check((await catalog.Load()).Single().Id == "late" && !catalog.Failed, "失败后再次操作恢复，旧错误不永久缓存");
        }
        const string tid = "11111111-1111-4111-8111-111111111111";
        var running = false; var attempts = 0; Local? peer = null; var waiting = false;
        Task<ICodexLocalEvents> Connect(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); attempts++;
            if (!running) throw new IOException("simulated Codex not started");
            peer = new Local(tid, () => waiting); return Task.FromResult<ICodexLocalEvents>(peer);
        }
        var item = new WorkItem { Title = "仅隔离重连", Lane = Lane.Running, TimerUnread = true };
        item.Stages[0].IsRunning = true; item.Stages[0].Codex = new() { ThreadId = tid };
        var binding = item.Stages[0].Codex!;
        await using (var observer = new CodexObserver(localConnect: Connect, reconnectInterval: TimeSpan.FromMilliseconds(80), activitySource: () => new EmptyActivity()))
        {
            observer.Event += (watch, message, sequence, epoch, live) => CodexState.Apply(item, watch.StageId, message, sequence, watch.Generation, epoch, live);
            observer.Watch([new(item.Id, item.Stages[0].Id, binding.BindingId, tid, binding.Generation)]);
            observer.Start("", "");
            await Until(() => attempts >= 2, "not retrying before Codex startup");
            Check(!observer.TransportConnected && observer.ConnectionHint.Contains("未连接"), "看板先启动会持续重连且给出低调状态文字");
            running = true; observer.EnsureConnection();
            await Until(() => observer.Capabilities.Any(x => x.Live), "late Codex never connected");
            Check(observer.TransportConnected && observer.SourceIdentityVerified && observer.ConnectionHint.Length == 0, "Codex后启动可自动连接，恢复后提示消失");
            Check(binding.Phase == CodexPhase.Running, "恢复连接后准确在途轮次进入阶段");
            waiting = true;
            await Until(() => item.Lane == Lane.Action, "waiting not applied");
            Check(binding.AwaitingUserAction, "断线前已确认等待");
            running = false; var oldPeer = peer!; oldPeer.Connected = false;
            await Until(() => !observer.TransportConnected, "disconnect not observed");
            Check(item.Lane == Lane.Action && binding.AwaitingUserAction, "断线不清已确认等待及待操作");
            running = true; observer.EnsureConnection();
            await Until(() => observer.TransportConnected && peer != oldPeer && observer.Capabilities.Any(x => x.Live), "not reconnected after drop");
            Check(oldPeer.Disposed && binding.AwaitingUserAction && item.Lane == Lane.Action, "重连释放旧连接，等待保护继续保持");
            Check(observer.TurnReadCount == 0 && observer.CompatibilityReadCount == 0, "启动先后及断线恢复都不回退聊天查询");
        }
        await using (var restarted = new CodexObserver(localConnect: Connect, reconnectInterval: TimeSpan.FromMilliseconds(80), activitySource: () => new EmptyActivity()))
        {
            restarted.Start("", "");
            await Until(() => restarted.TransportConnected, "Codex-first startup not connected");
            Check(restarted.SourceIdentityVerified && restarted.ConnectionHint.Length == 0, "Codex先启动及看板重启均自动连接，无需重新初始化");
        }
        var report = new { passed = checks.Count, checks, syntheticOnly = true, codexRestarted = false, formalDataModified = false };
        await File.WriteAllTextAsync(Path.Combine(output, "reconnect-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed = checks.Count, syntheticOnly = true }));
    }
    private sealed class Reader(string id) : ICodexCatalogRpc
    {
        internal bool Connected = true, Disposed; internal bool LocalOnly = true;
        internal readonly List<string> Methods = [];
        public bool IsConnected => Connected;
        public Task<JsonElement> Call(string method, object parameters, int timeoutMs = 4000)
        {
            Methods.Add(method); LocalOnly &= JsonSerializer.SerializeToElement(parameters).GetProperty("useStateDbOnly").GetBoolean();
            return Task.FromResult(JsonSerializer.SerializeToElement(new { data = new[] { new { id, name = "local title" } } }));
        }
        public ValueTask DisposeAsync() { Disposed = true; Connected = false; return ValueTask.CompletedTask; }
    }
    private sealed class EmptyActivity : ICodexActivitySource
    {
        public IReadOnlyDictionary<string, CodexActivity> Read(IReadOnlyCollection<string> threadIds) => threadIds.ToDictionary(x => x,
            _ => new CodexActivity("turn", "inProgress", 1, true, 100, 100));
    }
    private sealed class Local(string id, Func<bool> waiting) : ICodexLocalEvents
    {
        internal bool Disposed;
        public bool Connected { get; set; } = true;
        public long Frames => 1;
        public event Action<string>? Changed { add { } remove { } }
        public Task Subscribe(string[] ids) => Task.CompletedTask;
        public CodexDesktopSnapshot Snapshot(string thread) => new(JsonSerializer.SerializeToElement(new { type = "active",
            activeFlags = waiting() ? new[] { "waitingOnUserInput" } : Array.Empty<string>(), pendingSourceVersion = 1, pendingTurnId = "turn",
            pendingOverflow = false, pendingRequests = waiting() ? new[] { new { id = "question", approval = false } } : [] }),
            [new("turn", "inProgress")], thread == id, "fixture", "local");
        public ValueTask DisposeAsync() { Disposed = true; Connected = false; return ValueTask.CompletedTask; }
    }
}
