using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed record CodexThread(string Id, string Name, long UpdatedAt)
{ public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "未命名会话" : Name; public string Caption => DisplayName + " · " + Id; }
internal sealed record CodexWatch(Guid TaskId, Guid StageId, Guid BindingId, string ThreadId, int Generation);
internal sealed record CodexThreadCapability(string ThreadId, bool Live, string Reason, string StatusType,
    bool PendingAvailable = false, string PendingReason = "not-checked");
internal sealed class CodexObserver : IAsyncDisposable
{
    private sealed class BindingState(string threadId)
    {
        internal readonly object Gate = new();
        internal readonly CodexTurnTracker Tracker = new(threadId);
        internal string LastSnapshot = "";
        internal readonly CodexFollowPolicy Follow = new();
        internal CodexActivity? Activity;
        internal string? ActivityError;
        internal CodexDesktopSnapshot? LastLiveSnapshot;
        // True when the last snapshot handed to the state machine came from the
        // persisted local record rather than from the live stream, so a capability
        // never claims a cache-based request set it did not actually read.
        internal bool SnapshotFromFile;
    }
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<CodexWatch, BindingState> trackers = new();
    private readonly CodexCatalog catalog;
    private readonly Func<CancellationToken, Task<ICodexLocalEvents>> connectLocal;
    private readonly Func<ICodexActivitySource> connectActivity;
    private readonly TimeSpan reconnectInterval;
    private readonly SemaphoreSlim reconnectSignal = new(0, 1);
    private readonly object workerGate = new();
    private long lastReconnectRequest;
    private CodexRpc? rpc;
    private ICodexLocalEvents? localEvents;
    private readonly object localApplyGate = new();
    internal string PendingSourceReason { get; private set; } = "disabled";
    private Task? worker;
    private CodexWatch[] watches = [];
    private CodexThreadCapability[] capabilities = [];
    private string executable = "";
    private string desktopExecutable = "", endpoint = "";
    private bool discoverDesktop = true;
    private bool allowCompatibilityPolling;
    private readonly TimeSpan compatibilityInterval;
    private readonly CodexReadBudget compatibilityBudget;
    internal static readonly TimeSpan LocalInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan DefaultCompatibilityInterval = TimeSpan.FromSeconds(30);
    // Discovery is bounded and never renewed by the same persisted turn. Once
    // a real snapshot proves an active turn, keep its questions/approvals live
    // until idle. This reduces idle retention; it is not a zero-side-effect API.
    internal static readonly long ActivityWindowMs = 30_000, FollowMaxMs = 90_000;
    internal CodexObserver(TimeSpan? compatibilityInterval = null, Func<Task<ICodexCatalogRpc>>? catalogConnect = null,
        Func<CancellationToken, Task<ICodexLocalEvents>>? localConnect = null, TimeSpan? reconnectInterval = null,
        Func<ICodexActivitySource>? activitySource = null)
    {
        this.compatibilityInterval = compatibilityInterval ?? DefaultCompatibilityInterval;
        compatibilityBudget = new(this.compatibilityInterval);
        this.reconnectInterval = reconnectInterval ?? TimeSpan.FromSeconds(5);
        connectLocal = localConnect ?? (async cancellation => await CodexLocalEvents.Connect(cancellation).ConfigureAwait(false));
        connectActivity = activitySource ?? (() => new CodexFileActivity());
        // RPC injection remains available to isolated protocol fixtures only.
        catalog = new(catalogConnect);
    }
    private long sequence, pollCount, turnReadCount, reconciledTerminalCount, localReadCount, compatibilityReadCount;
    private string epoch = "";
    internal string Epoch => epoch;
    internal bool Authoritative { get; private set; }
    internal bool TransportConnected { get; private set; }
    internal bool SourceIdentityVerified { get; private set; }
    internal string SourceKind { get; private set; } = "none";
    internal string ReadMode { get; private set; } = "waiting-local";
    internal CodexThreadCapability[] Capabilities => Volatile.Read(ref capabilities);
    internal long PollCount => Interlocked.Read(ref pollCount);
    internal long TurnReadCount => Interlocked.Read(ref turnReadCount);
    internal long LocalReadCount => Interlocked.Read(ref localReadCount);
    internal long CompatibilityReadCount => Interlocked.Read(ref compatibilityReadCount);
    internal long ReconciledTerminalCount => Interlocked.Read(ref reconciledTerminalCount);
    internal string Status { get; private set; } = "尚未连接 · 状态待确认";
    internal string ConnectionHint => !TransportConnected || !SourceIdentityVerified ? "Codex 未连接，正在自动重试" :
        catalog.Failed ? "Codex 会话列表暂不可用，选择会话时将重试" : "";
    internal event Action? Changed;
    internal event Action<CodexWatch, JsonElement, long, string, bool>? Event;
    internal void Watch(CodexWatch[] values)
    {
        Volatile.Write(ref watches, values);
        foreach (var watch in values) trackers.GetOrAdd(watch, value => new BindingState(value.ThreadId));
        foreach (var removed in trackers.Keys.Where(x => !values.Contains(x))) trackers.TryRemove(removed, out _);
    }
    internal void Start(string executable, string endpoint, string? inspector = null, bool discoverDesktop = true, bool allowCompatibilityPolling = false, string desktopExecutable = "")
    {
        this.executable = executable;
        this.desktopExecutable = desktopExecutable; this.endpoint = endpoint;
        this.discoverDesktop = discoverDesktop;
        this.allowCompatibilityPolling = allowCompatibilityPolling;
        EnsureWorker();
    }
    private void EnsureWorker()
    {
        lock (workerGate)
            if (!lifetime.IsCancellationRequested && (worker is null || worker.IsCompleted)) worker = Task.Run(() => Run(executable, endpoint));
    }
    internal void EnsureConnection()
    {
        if (lifetime.IsCancellationRequested) return;
        EnsureWorker();
        if (TransportConnected && SourceIdentityVerified) return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastReconnectRequest) < 1000) return;
        Interlocked.Exchange(ref lastReconnectRequest, now);
        try { reconnectSignal.Release(); } catch (SemaphoreFullException) { }
    }
    internal static string FindExecutable() => CodexInstallationDiscovery.FindCliExecutable();
    private void SetStatus(string value) { if (Status == value) return; Status = value; Changed?.Invoke(); }
    private void PublishCapabilities(IEnumerable<CodexThreadCapability> values)
    {
        var next = values.ToArray(); var changed = !Capabilities.SequenceEqual(next);
        Volatile.Write(ref capabilities, next);
        Authoritative = capabilities.Any(x => x.Live);
        var message = SourceKind switch
        {
            "desktop-native" => capabilities.Length == 0 ? "Codex 已连接 · 尚未关联聊天" : Authoritative
                ? "Codex 本地状态已连接 · 无后台聊天查询"
                : capabilities.Any(x => x.Reason == "unsupported-local-state-protocol") ? "Codex 已连接 · 当前状态格式暂不支持，请更新看板"
                : "Codex 已连接 · 部分聊天状态待确认",
            "shared-service" => Authoritative ? "共享服务已连接 · 绑定会话实时可读" : "共享服务已连接 · 绑定会话状态待确认",
            _ => "历史服务可读 · 实时状态待确认"
        };
        var sameStatus = Status == message; SetStatus(message);
        if (changed && sameStatus) Changed?.Invoke();
    }
    private async Task Run(string executable, string endpoint)
    {
        if (discoverDesktop) { await RunLocalEvents().ConfigureAwait(false); return; }
        // Explicit isolated protocol fixtures only. No setting/CLI switch in
        // normal product startup can choose this historical request transport.
        if (!allowCompatibilityPolling) return;
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                var shared = false;
                try { rpc = await CodexRpc.Start(executable, true, endpoint).ConfigureAwait(false); shared = true; }
                catch when (!lifetime.IsCancellationRequested && executable.Length > 0)
                { rpc = await CodexRpc.Start(executable, false, "").ConfigureAwait(false); }
                epoch = Guid.NewGuid().ToString("N"); sequence = 0;
                Volatile.Write(ref capabilities, Array.Empty<CodexThreadCapability>()); Authoritative = false;
                SourceKind = shared ? "shared-service" : "historical";
                TransportConnected = true; SourceIdentityVerified = false;
                var sourceEpoch = epoch; var connection = rpc;
                connection.Notification += message => Receive(connection, sourceEpoch, message, shared);
                while (!lifetime.IsCancellationRequested)
                {
                    var current = Volatile.Read(ref watches);
                    var groups = current.GroupBy(x => x.ThreadId, StringComparer.Ordinal).ToArray();
                    var next = new List<CodexThreadCapability>();
                    if (groups.Any(g => compatibilityBudget.IsDue(g.Key, Environment.TickCount64)))
                    {
                        var loaded = ReadLoaded(await connection.Call("thread/loaded/list", new { }).ConfigureAwait(false));
                        foreach (var group in groups)
                            next.AddRange(await PollSharedGroup(connection, group.ToArray(), sourceEpoch, shared && loaded.Contains(group.Key)).ConfigureAwait(false));
                    }
                    else next.AddRange(current.Select(PreviousCapability));
                    ReadMode = "isolated-protocol-fixture"; PublishCapabilities(next.DistinctBy(x => x.ThreadId));
                    await Task.Delay(compatibilityInterval, lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (Exception) when (!lifetime.IsCancellationRequested)
            {
                foreach (var watch in Volatile.Read(ref watches)) EmitUnknown(watch, epoch);
                Authoritative = TransportConnected = SourceIdentityVerified = false;
                Volatile.Write(ref capabilities, Array.Empty<CodexThreadCapability>());
                SetStatus("隔离协议连接中断");
            }
            finally
            {
                var old = rpc; rpc = null;
                if (old is not null) await old.DisposeAsync().ConfigureAwait(false);
                Authoritative = TransportConnected = SourceIdentityVerified = false;
            }
            if (!lifetime.IsCancellationRequested) await Task.Delay(5000, lifetime.Token).ConfigureAwait(false);
        }
    }
    private async Task RunLocalEvents()
    {
        while (!lifetime.IsCancellationRequested)
        {
            ICodexLocalEvents? source = null;
            try
            {
                SetStatus("正在连接 Codex 本地状态");
                source = await connectLocal(lifetime.Token).ConfigureAwait(false); localEvents = source;
                epoch = Guid.NewGuid().ToString("N"); sequence = 0;
                var sourceEpoch = epoch;
                SourceKind = "desktop-native"; ReadMode = "local-file-projection"; PendingSourceReason = "local-file-projection";
                TransportConnected = SourceIdentityVerified = true; Authoritative = false;
                Volatile.Write(ref capabilities, Array.Empty<CodexThreadCapability>());
                var activity = connectActivity();
                foreach (var known in trackers.Values) { known.Follow.Disconnect(); known.LastLiveSnapshot = null; }
                var following = new HashSet<string>(StringComparer.Ordinal);
                IReadOnlyDictionary<string, CodexActivity>? files = null; string? fileError = null;
                void Poll()
                {
                    var now = Environment.TickCount64;
                    var captured = Capture(Volatile.Read(ref watches));
                    var ids = captured.Select(x => x.Watch.ThreadId).Distinct(StringComparer.Ordinal).ToArray();
                    try { files = activity.Read(ids); fileError = null; }
                    catch (Exception failure) when (!lifetime.IsCancellationRequested) { files = null; fileError = failure.Message; }
                    lock (localApplyGate)
                    {
                        following.Clear();
                        foreach (var entry in captured)
                        {
                            var state = entry.State;
                            state.ActivityError = fileError;
                            state.Activity = files is not null && files.TryGetValue(entry.Watch.ThreadId, out var value) ? value : null;
                            if (state.Follow.Poll(state.Activity, fileError is not null, now)) following.Add(entry.Watch.ThreadId);
                        }
                    }
                }
                void Update()
                {
                    lock (localApplyGate)
                    {
                        if (!ReferenceEquals(localEvents, source) || epoch != sourceEpoch || lifetime.IsCancellationRequested) return;
                        var captured = Capture(Volatile.Read(ref watches));
                        var next = captured.Select(x =>
                        {
                            var live = following.Contains(x.Watch.ThreadId);
                            var snapshot = Snapshot(x, source, live);
                            return ApplySnapshot(x, snapshot, sourceEpoch, live && !x.State.SnapshotFromFile ? SnapshotKind.Live : SnapshotKind.File);
                        }).GroupBy(x => x.ThreadId, StringComparer.Ordinal)
                            .Select(group => group.OrderByDescending(x => x.Live).ThenByDescending(x => x.PendingAvailable).First()).ToArray();
                        Interlocked.Exchange(ref localReadCount, source.Frames);
                        ReadMode = following.Count == 0 ? "local-file-projection" : "local-events";
                        PendingSourceReason = ReadMode == "local-file-projection" ? "local-file-projection" : "local-state-stream";
                        PublishCapabilities(next);
                    }
                }
                source.Changed += _ => Update();
                while (!lifetime.IsCancellationRequested && source.Connected)
                {
                    Poll();
                    await source.Subscribe(following.ToArray()).ConfigureAwait(false);
                    Update();
                    // Files discover candidates only. They never fabricate an
                    // execution or an empty pending-request set.
                    await Task.Delay(LocalInterval, lifetime.Token).ConfigureAwait(false);
                }
                if (!lifetime.IsCancellationRequested) throw new IOException("Codex 本地状态连接中断");
            }
            catch (Exception) when (!lifetime.IsCancellationRequested)
            {
                foreach (var watch in Volatile.Read(ref watches)) EmitUnknown(watch, epoch);
                Authoritative = TransportConnected = SourceIdentityVerified = false;
                Volatile.Write(ref capabilities, Array.Empty<CodexThreadCapability>());
                ReadMode = "local-disconnected";
                SetStatus("Codex 本地连接暂不可用 · 自动重连中（无聊天查询）");
            }
            finally
            {
                localEvents = null;
                if (source is not null) await source.DisposeAsync().ConfigureAwait(false);
                Authoritative = TransportConnected = SourceIdentityVerified = false;
            }
            if (!lifetime.IsCancellationRequested) await reconnectSignal.WaitAsync(reconnectInterval, lifetime.Token).ConfigureAwait(false);
        }
    }
    private static HashSet<string> ReadLoaded(JsonElement result)
    {
        var data = CodexState.Object(result, "data");
        return data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : CodexState.Text(x, "id")).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal) : [];
    }
    private (CodexWatch Watch, BindingState State, long Revision)[] Capture(CodexWatch[] values) => values
        .Where(trackers.ContainsKey).Select(w => (Watch: w, State: trackers.GetValueOrDefault(w)))
        .Where(x => x.State is not null).Select(x => (x.Watch, x.State!, x.State!.Tracker.Revision)).ToArray();
    private void ApplyUnavailable(CodexWatch watch, string sourceEpoch)
    {
        if (trackers.TryGetValue(watch, out var state)) ApplyPoll(watch, state, state.Tracker.Revision, default, [], false, sourceEpoch);
    }
    private CodexThreadCapability ApplySnapshot((CodexWatch Watch, BindingState State, long Revision) entry,
        CodexDesktopSnapshot snapshot, string sourceEpoch, SnapshotKind kind = SnapshotKind.Compatibility)
    {
        Interlocked.Increment(ref pollCount);
        if (!ApplyPoll(entry.Watch, entry.State, entry.Revision, snapshot.Status, snapshot.Turns, snapshot.LiveCapable, sourceEpoch))
            return PreviousCapability(entry.Watch);
        var pending = CodexState.Object(snapshot.Status, "pendingSourceVersion").ValueKind == JsonValueKind.Number;
        return new(entry.Watch.ThreadId, kind != SnapshotKind.File && snapshot.LiveCapable, snapshot.Reason, CodexState.Text(snapshot.Status, "type"), pending,
            kind == SnapshotKind.File ? pending ? "local-file-pending" : "local-file-turn-state"
            : kind == SnapshotKind.Live ? pending ? "local-cache-pending" : "local-cache-no-active-request"
            : "compatibility-polling");
    }
    private async Task<CodexThreadCapability[]> PollSharedGroup(CodexRpc connection, CodexWatch[] group, string sourceEpoch, bool loaded)
    {
        var entries = Capture(group); if (entries.Length == 0) return [];
        if (!compatibilityBudget.Reserve(group[0].ThreadId, Environment.TickCount64)) return group.Select(PreviousCapability).ToArray();
        var threadId = group[0].ThreadId;
        Interlocked.Increment(ref compatibilityReadCount);
        try
        {
            var result = await connection.Call("thread/read", new { threadId, includeTurns = false }).ConfigureAwait(false);
            var thread = CodexState.Object(result, "thread"); var status = CodexState.Object(thread, "status");
            var type = CodexState.Text(status, "type"); var exact = CodexState.Text(thread, "id") == threadId;
            var live = loaded && exact && type is ("active" or "idle"); var turns = Array.Empty<CodexTurnMetadata>();
            if (live)
                try
                {
                    var targets = entries.Select(x => x.State.Tracker.CurrentTurnId).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
                    var page = await CodexTurnTracker.ReadTurnsAsync(async (method, parameters) =>
                    {
                        Interlocked.Increment(ref turnReadCount);
                        return await connection.Call(method, parameters).ConfigureAwait(false);
                    }, threadId, targets.FirstOrDefault(), lifetime.Token, targetTurnIds: targets).ConfigureAwait(false);
                    turns = page.Turns;
                }
                catch (Exception) when (!lifetime.IsCancellationRequested) { }
            var reason = live ? "loaded-thread-identity-status-verified" : !exact ? "thread-identity-mismatch" : SourceKind == "historical" ? "historical-only" : !loaded ? "thread-not-loaded" : "unknown-live-state";
            var snapshot = new CodexDesktopSnapshot(status, turns, live, reason, "");
            return entries.Select(x => ApplySnapshot(x, snapshot, sourceEpoch)).ToArray();
        }
        catch (Exception) when (!lifetime.IsCancellationRequested)
        {
            return entries.Select(x => ApplySnapshot(x, new(default, [], false, "thread-read-unavailable", ""), sourceEpoch)).ToArray();
        }
    }
    // File activity can discover a candidate, but only the original stream may
    // establish Running or replace a pending-request set. A persisted terminal
    // may finish the exact turn that this binding already observed live.
    private CodexDesktopSnapshot Snapshot((CodexWatch Watch, BindingState State, long Revision) entry,
        ICodexLocalEvents source, bool following)
    {
        var state = entry.State;
        if (following)
        {
            var snapshot = source.Snapshot(entry.Watch.ThreadId);
            state.Follow.Observe(snapshot, state.Activity, Environment.TickCount64);
            state.LastLiveSnapshot = snapshot.LiveCapable ? snapshot : null;
            if (snapshot.LiveCapable) { state.SnapshotFromFile = false; return snapshot; }
        }
        else state.LastLiveSnapshot = null;
        state.SnapshotFromFile = true;
        if (state.Activity is not { } current)
            return new(default, [], false, state.ActivityError is null ? "local-file-turn-unknown" : "local-file-projection-unavailable", "local");
        if (!current.ProjectionCaughtUp)
            return new(default, [], false, "local-file-projection-behind", "local");
        if (current.TurnId == state.Tracker.CurrentTurnId && current.Status is "completed" or "failed" or "interrupted")
            return new(JsonSerializer.SerializeToElement(new { type = "idle", activeFlags = Array.Empty<string>() }),
                [new CodexTurnMetadata(current.TurnId, current.Status)], true, "local-file-known-turn-terminal", "local");
        // First-seen/history-only turns have no trustworthy binding-time boundary.
        // InProgress also cannot distinguish actual execution from a pending input
        // or a crashed old runtime. Preserve confirmed waiting through Unknown.
        return new(default, [], false, "local-file-awaiting-live-evidence", "local");
    }
    // Settings diagnostics use only an already received structural snapshot.
    // They neither connect, subscribe, refresh nor change any task state.
    internal CodexDesktopSnapshot PeekSnapshot(string threadId)
    {
        lock (localApplyGate)
        {
            if (!TransportConnected || !SourceIdentityVerified || localEvents is not { Connected: true })
                return new(default, [], false, "local-state-disconnected", "local");
            var live = trackers.Where(x => x.Key.ThreadId == threadId && x.Value.Follow.IsFollowing)
                .Select(x => x.Value.LastLiveSnapshot).FirstOrDefault(x => x is { LiveCapable: true });
            return live ?? new(default, [], false, "local-state-not-observed", "local");
        }
    }
    private CodexThreadCapability PreviousCapability(CodexWatch watch) => Capabilities.FirstOrDefault(x => x.ThreadId == watch.ThreadId)
        ?? new(watch.ThreadId, false, "poll-superseded", "");
    private bool ApplyPoll(CodexWatch watch, BindingState state, long revision, JsonElement status,
        CodexTurnMetadata[] turns, bool live, string sourceEpoch)
    {
        lock (state.Gate)
        {
            if (sourceEpoch != epoch || !trackers.TryGetValue(watch, out var current) || current != state || state.Tracker.Revision != revision) return false;
            if (!live)
            {
                var unavailable = sourceEpoch + "|unavailable|" + SourceKind;
                if (state.LastSnapshot == unavailable) return true;
                state.LastSnapshot = unavailable;
                if (SourceKind == "historical")
                    Emit(watch, JsonSerializer.SerializeToElement(new { method = "probe/snapshot", @params = new { threadId = watch.ThreadId, status } }), false, sourceEpoch);
                else EmitUnknown(watch, sourceEpoch);
                return true;
            }
            foreach (var message in state.Tracker.Reconcile(status, turns, true, revision, SourceKind == "desktop-native" ? sourceEpoch : null))
            {
                if (CodexState.Text(message, "method") is "turn/completed" or "probe/currentTurnStopped") Interlocked.Increment(ref reconciledTerminalCount);
                // Observe start and its already-present requests atomically. A
                // temporary Running event here would clear a reminder before the
                // very next waiting snapshot puts the card back in Action.
                var projected = CodexState.Text(message, "method") == "turn/started"
                    ? JsonSerializer.SerializeToElement(new { method = "turn/started", @params = new {
                        threadId = watch.ThreadId, turn = CodexState.Object(CodexState.Object(message, "params"), "turn"), status } })
                    : message;
                Emit(watch, projected, true, sourceEpoch);
            }
            var flags = CodexState.Object(status, "activeFlags");
            var claimsExecution = CodexState.Text(status, "type") == "active" && flags.ValueKind == JsonValueKind.Array && flags.GetArrayLength() == 0;
            var hasCurrentExecution = state.Tracker.CurrentTurnRunning && turns.Any(x => x.Id == state.Tracker.CurrentTurnId && x.Status == "inProgress");
            var fingerprint = sourceEpoch + "|" + state.Tracker.Revision + "|" + (status.ValueKind == JsonValueKind.Undefined ? "" : status.GetRawText());
            if (state.LastSnapshot == fingerprint) return true;
            state.LastSnapshot = fingerprint;
            // A late active status after a terminal, or an unavailable/ambiguous
            // turn read, cannot falsely resume the finished turn and clear red dots.
            if (claimsExecution && !hasCurrentExecution) EmitUnknown(watch, sourceEpoch);
            else Emit(watch, JsonSerializer.SerializeToElement(new { method = "probe/snapshot", @params = new { threadId = watch.ThreadId, turnId = state.Tracker.CurrentTurnId, status } }), true, sourceEpoch);
            return true;
        }
    }
    private void EmitUnknown(CodexWatch watch, string sourceEpoch) => Emit(watch,
        JsonSerializer.SerializeToElement(new { method = "probe/disconnected", @params = new { threadId = watch.ThreadId } }), true, sourceEpoch);
    private void Emit(CodexWatch watch, JsonElement message, bool authoritative, string sourceEpoch)
    {
        if (sourceEpoch == epoch && Volatile.Read(ref watches).Contains(watch))
            Event?.Invoke(watch, message, Interlocked.Increment(ref sequence), sourceEpoch, authoritative);
    }
    private void Receive(CodexRpc source, string sourceEpoch, JsonElement message, bool shared)
    {
        if (!shared || !ReferenceEquals(rpc, source) || sourceEpoch != epoch) return;
        if (CodexState.Text(message, "method").StartsWith("probe/", StringComparison.Ordinal)) return;
        var parameters = CodexState.Object(message, "params"); var threadId = CodexState.Text(parameters, "threadId");
        if (threadId.Length == 0) threadId = CodexState.Text(CodexState.Object(parameters, "thread"), "id");
        if (!Capabilities.Any(x => x.ThreadId == threadId && x.Live)) return;
        foreach (var watch in Volatile.Read(ref watches).Where(x => x.ThreadId == threadId))
        {
            if (!trackers.TryGetValue(watch, out var state)) continue;
            lock (state.Gate)
                if (ReferenceEquals(rpc, source) && sourceEpoch == epoch && state.Tracker.ObserveNotification(message, true))
                    Emit(watch, message, true, sourceEpoch);
        }
    }
    internal async Task<CodexThread[]> Catalog()
    {
        try { return await catalog.Load().ConfigureAwait(false); }
        finally { if (!lifetime.IsCancellationRequested) Changed?.Invoke(); }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (localEvents is { } local) await local.DisposeAsync().ConfigureAwait(false);
        if (rpc is { } connection) await connection.DisposeAsync().ConfigureAwait(false);
        if (worker is not null) { try { await worker.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        await catalog.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Where one snapshot came from: the explicit isolated compatibility
/// fixture, the live local stream while a turn is actually running, or the
/// locally persisted turn record that is read while nothing is subscribed.</summary>
internal enum SnapshotKind { Compatibility, Live, File }

// A file is a discovery hint, never execution evidence. One persisted turn can
// open at most one unconfirmed window over this binding's lifetime, including
// transport reconnects. Only a real active snapshot promotes it to live tracking.
internal sealed class CodexFollowPolicy
{
    private readonly object gate = new();
    private readonly HashSet<string> attemptedTurns = new(StringComparer.Ordinal);
    private bool following, active, recoveryPending, recoveryEligible, knownLiveWindow;
    private long deadline, previousBytes = -1;
    internal bool IsFollowing { get { lock (gate) return following; } }
    internal long Deadline { get { lock (gate) return deadline; } }
    internal bool Poll(CodexActivity? current, bool failed, long now)
    {
        lock (gate)
        {
            var usable = !failed && current is { ProjectionCaughtUp: true, TurnId.Length: > 0 };
            var grew = usable && previousBytes >= 0 && current!.RolloutBytes > previousBytes;
            if (usable) previousBytes = current!.RolloutBytes;
            if (following)
            {
                if (active) return true;
                if ((usable || knownLiveWindow) && now < deadline) return true;
                following = knownLiveWindow = false;
                return false; // Never expire and immediately reopen in the same poll.
            }
            // Reconnect may re-establish a genuinely observed live turn once.
            // A failed recovery has no new live evidence and cannot arm itself.
            if (recoveryPending)
            {
                recoveryPending = recoveryEligible = false;
                following = knownLiveWindow = true;
                deadline = now + CodexObserver.FollowMaxMs;
                return true;
            }
            if (!usable) return false;
            var candidate = current!;
            if (attemptedTurns.Contains(candidate.TurnId) || candidate.Status != "inProgress" && !grew) return false;
            attemptedTurns.Add(candidate.TurnId);
            following = true;
            deadline = now + (candidate.Status == "inProgress" ? CodexObserver.FollowMaxMs : CodexObserver.ActivityWindowMs);
            return true;
        }
    }
    internal void Observe(CodexDesktopSnapshot snapshot, CodexActivity? file, long now)
    {
        lock (gate)
        {
            var type = CodexState.Text(snapshot.Status, "type");
            if (snapshot.LiveCapable && type == "idle")
            {
                following = active = knownLiveWindow = recoveryEligible = recoveryPending = false;
                if (file is not null) attemptedTurns.Add(file.TurnId);
                foreach (var turn in snapshot.Turns) attemptedTurns.Add(turn.Id);
            }
            else if (snapshot.LiveCapable && type == "active")
            {
                // Another binding may already own this thread's single local
                // subscription. Its exact snapshot is valid for this binding too.
                following = active = knownLiveWindow = true;
                recoveryEligible = true;
                foreach (var turn in snapshot.Turns) attemptedTurns.Add(turn.Id);
            }
            else if (active)
            {
                // Losing a once-trusted stream gets one fixed grace period,
                // not a deadline renewed by each missing frame or file write.
                active = false; deadline = now + CodexObserver.ActivityWindowMs;
            }
        }
    }
    internal void Disconnect()
    {
        lock (gate)
        {
            // A revision gap clears active before a subsequent pipe failure.
            // Preserve the independently earned, single recovery opportunity.
            recoveryPending |= recoveryEligible;
            following = active = false;
        }
        // attemptedTurns intentionally survives reconnects: an old stalled row
        // must not reset the desktop idle clock once per connection attempt.
    }
}
