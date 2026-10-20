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
    }
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<CodexWatch, BindingState> trackers = new();
    private readonly SemaphoreSlim catalogGate = new(1, 1);
    private CodexRpc? rpc;
    private CodexRpc? catalogRpc;
    private CodexDesktopBridge? desktop;
    private CodexPendingBridge? pendingBridge;
    private DateTime nextPendingDiscovery;
    internal string PendingSourceReason { get; private set; } = "disabled";
    private Task? worker;
    private CodexWatch[] watches = [];
    private CodexThreadCapability[] capabilities = [];
    private string executable = "";
    private long sequence, pollCount, turnReadCount, reconciledTerminalCount;
    private string epoch = "";
    internal string Epoch => epoch;
    internal bool Authoritative { get; private set; }
    internal bool TransportConnected { get; private set; }
    internal bool SourceIdentityVerified { get; private set; }
    internal string SourceKind { get; private set; } = "none";
    internal CodexThreadCapability[] Capabilities => Volatile.Read(ref capabilities);
    internal long PollCount => Interlocked.Read(ref pollCount);
    internal long TurnReadCount => Interlocked.Read(ref turnReadCount);
    internal long ReconciledTerminalCount => Interlocked.Read(ref reconciledTerminalCount);
    internal string Status { get; private set; } = "尚未连接 · 状态待确认";
    internal event Action? Changed;
    internal event Action<CodexWatch, JsonElement, long, string, bool>? Event;
    internal void Watch(CodexWatch[] values)
    {
        Volatile.Write(ref watches, values);
        foreach (var watch in values) trackers.GetOrAdd(watch, value => new BindingState(value.ThreadId));
        foreach (var removed in trackers.Keys.Where(x => !values.Contains(x))) trackers.TryRemove(removed, out _);
    }
    internal void Start(string executable, string endpoint)
    {
        this.executable = executable;
        if (worker is null) worker = Task.Run(() => Run(executable, endpoint));
    }
    internal static string FindExecutable()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        { var candidate = Path.Combine(directory.Trim('"'), "codex.exe"); if (File.Exists(candidate)) return candidate; }
        var known = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(known)) return Directory.EnumerateFiles(known, "codex.exe", SearchOption.AllDirectories).FirstOrDefault() ?? "";
        return "";
    }
    private void SetStatus(string value) { Status = value; Changed?.Invoke(); }
    private void PublishCapabilities(IEnumerable<CodexThreadCapability> values)
    {
        Volatile.Write(ref capabilities, values.ToArray());
        Authoritative = capabilities.Any(x => x.Live);
        SetStatus(SourceKind switch
        {
            "desktop-native" => Authoritative ? "原Codex桌面已连接 · 绑定会话实时可读" : "原Codex桌面已连接 · 绑定会话状态待确认",
            "shared-service" => Authoritative ? "共享服务已连接 · 绑定会话实时可读" : "共享服务已连接 · 绑定会话状态待确认",
            _ => "历史服务可读 · 实时状态待确认"
        });
    }
    private async Task Run(string executable, string endpoint)
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                SetStatus("正在连接 · 状态待确认");
                if (endpoint.Length == 0) desktop = await CodexDesktopBridge.TryConnect(lifetime.Token).ConfigureAwait(false);
                var shared = false;
                if (desktop is null)
                {
                    if (executable.Length == 0 && endpoint.Length == 0)
                    {
                        SetStatus("未找到Codex · 状态待确认");
                        // Starting the board before Codex is not a permanent
                        // failure. Keep discovering without creating any chat.
                        await Task.Delay(10000, lifetime.Token).ConfigureAwait(false);
                        continue;
                    }
                    try { rpc = await CodexRpc.Start(executable, true, endpoint).ConfigureAwait(false); shared = true; }
                    catch when (!lifetime.IsCancellationRequested && endpoint.Length == 0)
                    { rpc = await CodexRpc.Start(executable, false, "").ConfigureAwait(false); }
                }
                epoch = Guid.NewGuid().ToString("N"); sequence = 0;
                SourceKind = desktop is not null ? "desktop-native" : shared ? "shared-service" : "historical";
                TransportConnected = true; SourceIdentityVerified = desktop is not null;
                var sourceEpoch = epoch; var connection = rpc;
                if (connection is not null) connection.Notification += message => Receive(connection, sourceEpoch, message, shared);
                var nextDesktopDiscovery = DateTime.UtcNow.AddSeconds(10);
                while (!lifetime.IsCancellationRequested)
                {
                    var values = new List<CodexThreadCapability>();
                    if (desktop is { } bridge)
                    {
                        foreach (var watch in Volatile.Read(ref watches))
                            values.Add(await PollDesktop(bridge, watch, sourceEpoch).ConfigureAwait(false));
                    }
                    else if (connection is not null)
                    {
                        // A loaded exact thread and its actual live status are checked per binding.
                        var loadedResult = await connection.Call("thread/loaded/list", new { }).ConfigureAwait(false);
                        var loaded = ReadLoaded(loadedResult);
                        foreach (var watch in Volatile.Read(ref watches))
                            values.Add(await PollShared(connection, watch, sourceEpoch, shared && loaded.Contains(watch.ThreadId)).ConfigureAwait(false));
                    }
                    var bound = Volatile.Read(ref watches).Select(x => x.ThreadId).ToHashSet(StringComparer.Ordinal);
                    PublishCapabilities(values.Where(x => bound.Contains(x.ThreadId)).DistinctBy(x => x.ThreadId));
                    // The board may start before the desktop. A healthy history
                    // reader must not prevent later discovery of the real source.
                    if (SourceKind == "historical" && endpoint.Length == 0 && DateTime.UtcNow >= nextDesktopDiscovery)
                    {
                        nextDesktopDiscovery = DateTime.UtcNow.AddSeconds(10);
                        var candidate = await CodexDesktopBridge.TryConnect(lifetime.Token).ConfigureAwait(false);
                        if (candidate is not null) { await candidate.DisposeAsync().ConfigureAwait(false); break; }
                    }
                    await Task.Delay(desktop is null ? 750 : 300, lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (Exception) when (!lifetime.IsCancellationRequested)
            {
                foreach (var watch in Volatile.Read(ref watches)) EmitUnknown(watch, epoch);
                Authoritative = false; TransportConnected = false; SourceIdentityVerified = false;
                Volatile.Write(ref capabilities, Array.Empty<CodexThreadCapability>());
                SetStatus("连接中断 · 状态待确认");
            }
            finally
            {
                var oldRpc = rpc; rpc = null;
                var oldDesktop = desktop; desktop = null;
                var oldPending = pendingBridge; pendingBridge = null;
                if (oldRpc is not null) await oldRpc.DisposeAsync().ConfigureAwait(false);
                if (oldDesktop is not null) await oldDesktop.DisposeAsync().ConfigureAwait(false);
                if (oldPending is not null) await oldPending.DisposeAsync().ConfigureAwait(false);
                Authoritative = false; TransportConnected = false; SourceIdentityVerified = false;
            }
            if (!lifetime.IsCancellationRequested) await Task.Delay(5000, lifetime.Token).ConfigureAwait(false);
        }
    }
    private static HashSet<string> ReadLoaded(JsonElement result)
    {
        var data = CodexState.Object(result, "data");
        return data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : CodexState.Text(x, "id")).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal) : [];
    }
    private async Task<CodexThreadCapability> PollDesktop(CodexDesktopBridge bridge, CodexWatch watch, string sourceEpoch)
    {
        if (!trackers.TryGetValue(watch, out var state)) return new(watch.ThreadId, false, "binding-removed", "");
        var revision = state.Tracker.Revision;
        Interlocked.Increment(ref pollCount); Interlocked.Increment(ref turnReadCount);
        var snapshot = await bridge.Read(watch.ThreadId, state.Tracker.CurrentTurnId, lifetime.Token).ConfigureAwait(false);
        var pendingAvailable = false;
        var pendingReason = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CodexPendingBridge.Setting)) ? "disabled" : "not-sampled";
        if (snapshot.LiveCapable && CodexState.Text(snapshot.Status, "type") == "active" && snapshot.Turns.Count(x => x.Status == "inProgress") == 1 &&
            Environment.GetEnvironmentVariable(CodexPendingBridge.Setting) is { Length: > 0 })
        {
            if (pendingBridge is null && DateTime.UtcNow >= nextPendingDiscovery) pendingBridge = new CodexPendingBridge();
            if (pendingBridge is { } pendingSource)
            {
                var pending = await pendingSource.Read(watch.ThreadId, bridge.ServerProcessId, lifetime.Token).ConfigureAwait(false);
                PendingSourceReason = pendingSource.Reason;
                snapshot = snapshot with { Status = CodexPendingBridge.Merge(snapshot.Status, snapshot.Turns, pending) };
                pendingAvailable = CodexState.Object(snapshot.Status, "pendingSourceVersion").ValueKind == JsonValueKind.Number;
                pendingReason = pendingAvailable ? pendingSource.Reason : pending is null ? pendingSource.Reason : "pending-native-turn-not-confirmed";
                if (pending is null && pendingSource.Reason != "thread-pending-unavailable")
                {
                    await pendingSource.DisposeAsync().ConfigureAwait(false); pendingBridge = null;
                    nextPendingDiscovery = DateTime.UtcNow.AddSeconds(5);
                }
            }
            else pendingReason = PendingSourceReason;
        }
        if (!ApplyPoll(watch, state, revision, snapshot.Status, snapshot.Turns, snapshot.LiveCapable, sourceEpoch)) return PreviousCapability(watch);
        return new(watch.ThreadId, snapshot.LiveCapable, snapshot.Reason, CodexState.Text(snapshot.Status, "type"), pendingAvailable, pendingReason);
    }
    private async Task<CodexThreadCapability> PollShared(CodexRpc connection, CodexWatch watch, string sourceEpoch, bool loaded)
    {
        if (!trackers.TryGetValue(watch, out var state)) return new(watch.ThreadId, false, "binding-removed", "");
        var revision = state.Tracker.Revision;
        Interlocked.Increment(ref pollCount);
        try
        {
            var result = await connection.Call("thread/read", new { threadId = watch.ThreadId, includeTurns = false }).ConfigureAwait(false);
            var thread = CodexState.Object(result, "thread"); var status = CodexState.Object(thread, "status");
            var type = CodexState.Text(status, "type");
            var exact = CodexState.Text(thread, "id") == watch.ThreadId;
            var live = loaded && exact && type is ("active" or "idle");
            var turns = Array.Empty<CodexTurnMetadata>();
            if (live)
            {
                try
                {
                    var page = await CodexTurnTracker.ReadTurnsAsync(async (method, parameters) =>
                    {
                        Interlocked.Increment(ref turnReadCount);
                        return await connection.Call(method, parameters).ConfigureAwait(false);
                    }, watch.ThreadId, state.Tracker.CurrentTurnId, lifetime.Token).ConfigureAwait(false);
                    turns = page.Turns;
                }
                catch (Exception) when (!lifetime.IsCancellationRequested) { /* No inferred completion on unavailable turns API. */ }
            }
            if (!ApplyPoll(watch, state, revision, status, turns, live, sourceEpoch)) return PreviousCapability(watch);
            return new(watch.ThreadId, live, live ? "loaded-thread-identity-status-verified" : !exact ? "thread-identity-mismatch" : SourceKind == "historical" ? "historical-only" : !loaded ? "thread-not-loaded" : "unknown-live-state", type);
        }
        catch (Exception) when (!lifetime.IsCancellationRequested)
        {
            if (!ApplyPoll(watch, state, revision, default, [], false, sourceEpoch)) return PreviousCapability(watch);
            return new(watch.ThreadId, false, "thread-read-unavailable", "");
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
        await catalogGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            var current = rpc;
            if (current is null)
            {
                if (executable.Length == 0) return [];
                catalogRpc ??= await CodexRpc.Start(executable, false, "").ConfigureAwait(false);
                current = catalogRpc;
            }
            var values = new List<CodexThread>(); string? cursor = null;
            var seen = new HashSet<string>();
            do
            {
                var result = await current.Call("thread/list", new { cursor, limit = 100, archived = false, sortKey = "updated_at", sortDirection = "desc", sourceKinds = new[] { "appServer", "cli", "vscode", "exec", "unknown" }, useStateDbOnly = true }).ConfigureAwait(false);
                var data = CodexState.Object(result, "data"); if (data.ValueKind != JsonValueKind.Array) break;
                values.AddRange(data.EnumerateArray().Select(x => new CodexThread(CodexState.Text(x, "id"),
                    CodexState.Text(x, "name") is { Length: > 0 } name ? name : CodexState.Text(x, "title"),
                    CodexState.Object(x, "updatedAt").ValueKind == JsonValueKind.Number && CodexState.Object(x, "updatedAt").TryGetInt64(out var updated) ? updated : 0)));
                cursor = CodexState.Text(result, "nextCursor");
            } while (!lifetime.IsCancellationRequested && cursor.Length > 0 && seen.Add(cursor));
            return values.DistinctBy(x => x.Id).ToArray();
        }
        catch
        {
            if (catalogRpc is not null) { await catalogRpc.DisposeAsync().ConfigureAwait(false); catalogRpc = null; }
            throw;
        }
        finally { catalogGate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (rpc is { } connection) await connection.DisposeAsync().ConfigureAwait(false);
        if (desktop is { } bridge) await bridge.DisposeAsync().ConfigureAwait(false);
        if (worker is not null) { try { await worker.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        if (pendingBridge is { } pendingSource) await pendingSource.DisposeAsync().ConfigureAwait(false);
        await catalogGate.WaitAsync().ConfigureAwait(false);
        try { if (catalogRpc is not null) { await catalogRpc.DisposeAsync().ConfigureAwait(false); catalogRpc = null; } }
        finally { catalogGate.Release(); }
    }
}
