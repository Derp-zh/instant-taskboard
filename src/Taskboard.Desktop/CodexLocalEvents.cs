using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

// Connect to the already running desktop router. Never start a router, acquire
// thread ownership, hydrate history, read_thread, answer, approve or send a turn.
internal interface ICodexLocalEvents : IAsyncDisposable
{
    long Frames { get; }
    bool Connected { get; }
    event Action<string>? Changed;
    Task Subscribe(string[] threadIds);
    CodexDesktopSnapshot Snapshot(string id);
}

internal sealed class CodexLocalEvents : ICodexLocalEvents
{
    private readonly NamedPipeClientStream pipe;
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, CodexLocalState> states = new(StringComparer.Ordinal);
    private readonly HashSet<string> subscribed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> refreshAfter = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private Task? reader;
    private int disposed;
    private string clientId = "initializing-client";
    internal string IdentityKind { get; }
    public long Frames { get; private set; }
    internal long Subscriptions { get; private set; }
    public bool Connected => initialized.Task.IsCompletedSuccessfully && reader is { IsCompleted: false };
    public event Action<string>? Changed;
    private CodexLocalEvents(NamedPipeClientStream pipe, string identity) { this.pipe = pipe; IdentityKind = identity; }
    internal static async Task<CodexLocalEvents> Connect(CancellationToken cancellation = default)
    {
        var pipe = new NamedPipeClientStream(".", "codex-ipc", PipeDirection.InOut, PipeOptions.Asynchronous,
            System.Security.Principal.TokenImpersonationLevel.Identification);
        CodexLocalEvents? result = null;
        try
        {
            await pipe.ConnectAsync(1500, cancellation).ConfigureAwait(false);
            if (!CodexDesktopBridge.VerifyServer(pipe.SafePipeHandle, null, out var identity))
                throw new IOException("Codex 本地状态来源身份未通过核验：" + identity);
            result = new CodexLocalEvents(pipe, identity);
            result.reader = Task.Run(result.Read);
            await result.Send(new { type = "request", method = "initialize", version = 0, requestId = Guid.NewGuid().ToString(),
                sourceClientId = result.clientId, @params = new { clientType = "instant-taskboard-local-observer" } }).ConfigureAwait(false);
            await result.initialized.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellation).ConfigureAwait(false);
            return result;
        }
        catch { if (result is not null) await result.DisposeAsync().ConfigureAwait(false); else pipe.Dispose(); throw; }
    }
    public async Task Subscribe(string[] threadIds)
    {
        var wanted = threadIds.Where(x => Guid.TryParse(x, out _)).ToHashSet(StringComparer.Ordinal);
        string[] add, remove, refresh;
        lock (gate)
        {
            add = wanted.Except(subscribed).ToArray(); remove = subscribed.Except(wanted).ToArray();
            foreach (var id in remove) { subscribed.Remove(id); states.Remove(id); refreshAfter.Remove(id); }
            foreach (var id in add) { subscribed.Add(id); states[id] = new(id); }
            refresh = states.Where(x => x.Value.NeedsSnapshot && refreshAfter.GetValueOrDefault(x.Key) <= Environment.TickCount64).Select(x => x.Key).ToArray();
            foreach (var id in refresh) refreshAfter[id] = Environment.TickCount64 + 10000;
        }
        foreach (var id in remove) await Follow(id, false).ConfigureAwait(false);
        foreach (var id in add) { await Follow(id, true).ConfigureAwait(false); Subscriptions++; }
        foreach (var id in refresh) await Follow(id, true).ConfigureAwait(false);
    }
    private Task Follow(string id, bool following) => Send(new { type = "broadcast", method = "thread-stream-following-changed",
        version = 1, sourceClientId = clientId, @params = new { conversationId = id, hostId = "local", following } });
    public CodexDesktopSnapshot Snapshot(string id)
    {
        lock (gate) return states.TryGetValue(id, out var state) ? state.Snapshot()
            : new(default, [], false, "local-chat-not-cached", "local");
    }
    private async Task Send(object message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message); var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await writes.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try { await pipe.WriteAsync(header, lifetime.Token).ConfigureAwait(false); await pipe.WriteAsync(bytes, lifetime.Token).ConfigureAwait(false); await pipe.FlushAsync(lifetime.Token).ConfigureAwait(false); }
        finally { writes.Release(); }
    }
    private async Task Read()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var header = new byte[4]; await pipe.ReadExactlyAsync(header, lifetime.Token).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is <= 0 or > 64 * 1024 * 1024) throw new IOException("Codex 本地状态帧超出范围");
                var bytes = new byte[length]; await pipe.ReadExactlyAsync(bytes, lifetime.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes); var message = document.RootElement; Frames++;
                var type = Text(message, "type");
                if (type == "response" && Text(message, "method") == "initialize")
                {
                    var id = Text(Object(message, "result"), "clientId");
                    if (Text(message, "resultType") != "success" || !Guid.TryParse(id, out _)) throw new IOException("Codex 本地状态握手无效");
                    clientId = id; initialized.TrySetResult(); continue;
                }
                if (type == "client-discovery-request")
                {
                    // Explicitly decline ALL commands. The board owns no chat.
                    await Send(new { type = "client-discovery-response", requestId = Text(message, "requestId"), response = new { canHandle = false } }).ConfigureAwait(false);
                    continue;
                }
                if (type != "broadcast" || Text(message, "method") != "thread-stream-state-changed") continue;
                var targets = Object(message, "targetClientIds");
                if (targets.ValueKind == JsonValueKind.Array && !targets.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == clientId)) continue;
                var parameters = Object(message, "params"); var threadId = Text(parameters, "conversationId");
                if (Text(parameters, "hostId") != "local") continue;
                var source = Text(message, "sourceClientId"); bool changed;
                lock (gate)
                {
                    if (!states.TryGetValue(threadId, out var state)) continue;
                    changed = state.Apply(Object(message, "version"), source, Object(parameters, "change"));
                }
                if (changed) Changed?.Invoke(threadId);
                // JsonDocument and receive buffer are transient. Only the bounded
                // structural projection survives, never transcript/tool contents.
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or JsonException or InvalidOperationException)
        { initialized.TrySetException(new IOException("Codex 本地状态连接中断", e)); }
    }
    private static JsonElement Object(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) ? v : default;
    private static string Text(JsonElement e, string k) => Object(e, k) is { ValueKind: JsonValueKind.String } v ? v.GetString()! : "";
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (Connected)
        {
            string[] ids; lock (gate) ids = subscribed.ToArray();
            using var timeout = new CancellationTokenSource(500);
            foreach (var id in ids) try { await Follow(id, false).WaitAsync(timeout.Token).ConfigureAwait(false); } catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { break; }
        }
        lifetime.Cancel(); pipe.Dispose(); if (reader is not null) await reader.ConfigureAwait(false);
        lock (gate) { states.Clear(); subscribed.Clear(); }
    }
}
