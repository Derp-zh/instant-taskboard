using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

// Optional, version-pinned observation of the ORIGINAL desktop's in-memory
// request index. Never enables an inspector, launches/restarts Codex, hydrates a
// thread, changes a setting, answers a request or evaluates caller-supplied code.
internal sealed class CodexPendingBridge : IAsyncDisposable
{
    internal const string Setting = "INSTANT_TASKBOARD_CODEX_INSPECTOR";
    internal const string ModuleName = "app-initial-5851df3f6cb1.js";
    internal const string ModuleHash = "c70d95da34ba7e5962a559dbf68d0102d10b4b47b877f5d9933629bb5a1f3a10";
    internal const string BaseModuleName = "app-shared-bbfeca5e5737.js";
    internal const string BaseModuleHash = "a6579611db8acf9c9908e2a29e70a88687eaa1d8f8dbcdc258de58f286051e9d";
    private const string ModuleUrl = "app://-/assets/" + ModuleName;
    private const string BaseModuleUrl = "app://-/assets/" + BaseModuleName;
    // A profile describes the actual observation contract, not the app version.
    // An unchanged bundle remains usable after an app update. Unknown bundles
    // are connected and identified, but never receive guessed object queries.
    internal sealed record Profile(string Module, string ModuleHash, string BaseModule, string BaseHash, string Manager, string Export);
    internal static readonly Profile[] Profiles = [
        new(ModuleName, ModuleHash, BaseModuleName, BaseModuleHash, "Hfn", "Dot"),
        new("app-initial-83973da15da9.js", "1fb9718e75ae6b4adc0dfd030028d570846d098892e774564db5efe491ce4691",
            "app-shared-efae91d19fbd.js", "f3d9baa29fa49cbe6a0894ece757a4d5f88d99bdc4bf855bb8444e12e99158ca", "imn", "nct")
    ];
    private Profile? profile;
    private readonly System.Collections.Generic.Dictionary<string, (string Id, string Url)> scripts = new();
    private readonly ClientWebSocket socket = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string setting;
    internal CodexPendingBridge(string? endpoint = null) => setting = endpoint ?? Environment.GetEnvironmentVariable(Setting) ?? "";
    internal bool Ready => managers is not null && moduleScriptId is not null && baseScriptId is not null && socket.State == WebSocketState.Open;
    internal async Task<bool> Probe(int verifiedDesktopPid, CancellationToken cancellation = default)
    {
        await Read("", verifiedDesktopPid, cancellation).ConfigureAwait(false);
        return Ready;
    }
    private string? managers;
    private string? moduleScriptId;
    private string? baseScriptId;
    private int nextId, processId;
    internal string Reason { get; private set; } = "disabled";
    internal string ReadStage { get; private set; } = "not-started";

    // Only an explicitly selected IPv4 loopback inspector is supported. Verify
    // the LISTEN socket owner against the already authenticated app-tools peer
    // BEFORE sending any evaluation, not against a self-reported Runtime PID.
    internal static bool ValidEndpoint(string value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != "http" ||
            parsed.Host != "127.0.0.1" || parsed.Port is < 1 or > 65535 ||
            parsed.UserInfo.Length != 0 || parsed.AbsolutePath != "/" || parsed.Query.Length != 0 || parsed.Fragment.Length != 0) return false;
        uri = parsed; return true;
    }

    internal async Task<CodexPendingSnapshot?> Read(string threadId, int verifiedDesktopPid, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(setting)) { Reason = "disabled"; return null; }
        if (!ValidEndpoint(setting, out var endpoint)) { Reason = "invalid-loopback-endpoint"; return null; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(managers is null ? 20000 : 2000);
        await gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            if (socket.State != WebSocketState.Open)
            {
                if (socket.State != WebSocketState.None) { Reason = "inspector-disconnected"; return null; }
                if (!OwnsListener(endpoint!.Port, verifiedDesktopPid)) { Reason = "inspector-peer-mismatch"; return null; }
                ReadStage = "page-discovery";
                using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
                using var response = await client.GetAsync(new Uri(endpoint, "json/list"), HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                var data = await ReadBounded(body, 65536, timeout.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(data);
                var targets = document.RootElement.EnumerateArray().Where(x => Text(x, "type") == "page" &&
                    Uri.TryCreate(Text(x, "url"), UriKind.Absolute, out var page) && page.Scheme == "app" && page.Host == "-" &&
                    page.AbsolutePath == "/index.html" && page.UserInfo.Length == 0 && page.Query.Length == 0 && page.Fragment.Length == 0).Take(2).ToArray();
                if (targets.Length != 1 || !Uri.TryCreate(Text(targets[0], "webSocketDebuggerUrl"), UriKind.Absolute, out var ws) ||
                    ws.Scheme != "ws" || ws.Host != "127.0.0.1" || ws.Port != endpoint.Port || ws.UserInfo.Length != 0 ||
                    ws.Query.Length != 0 || ws.Fragment.Length != 0 || !OwnsListener(endpoint.Port, verifiedDesktopPid))
                { Reason = "ambiguous-inspector-target"; return null; }
                await socket.ConnectAsync(ws, timeout.Token).ConfigureAwait(false);
                processId = verifiedDesktopPid;
                // The packaged app disables Node --inspect and keeps its live
                // managers in the renderer. Enable only this CDP session's
                // read domain: never pause, set breakpoints or replace source.
                await Call("Debugger.enable", new { }, timeout.Token).ConfigureAwait(false);
                profile = Profiles.SingleOrDefault(p => scripts.ContainsKey(p.ModuleHash) && scripts.ContainsKey(p.BaseHash));
                if (profile is null) { Reason = "unsupported-desktop-module"; await Call("Debugger.disable", new { }, timeout.Token).ConfigureAwait(false); return null; }
                moduleScriptId = scripts[profile.ModuleHash].Id; baseScriptId = scripts[profile.BaseHash].Id;
                // Import resolves the ALREADY parsed, exact-hash ESM namespace;
                // no initializer, hook, app factory or mutation method is called.
                // Hfn extends Er imported as Dot from the exact shared bundle.
                // OXn in app-initial exports a different class named $Er.
                // Querying its prototype finds derived instances without reading
                // the application's module scope, tokens, messages or globals.
                var expression = "document.readyState === 'complete' ? import('" + scripts[profile.BaseHash].Url + "').then(m => m." + profile.Export + "?.prototype) : null";
                var prototype = await Call("Runtime.evaluate", new { expression, awaitPromise = true, objectGroup = "instant-taskboard-pending", silent = true, timeout = 1000 }, timeout.Token).ConfigureAwait(false);
                var prototypeId = Text(Object(prototype, "result"), "objectId");
                if (prototypeId.Length == 0) { Reason = "manager-prototype-unavailable"; return null; }
                var objects = await Call("Runtime.queryObjects", new { prototypeObjectId = prototypeId, objectGroup = "instant-taskboard-pending" }, timeout.Token).ConfigureAwait(false);
                managers = Text(Object(objects, "objects"), "objectId");
                if (managers.Length == 0) { Reason = "manager-unavailable"; return null; }
                await Call("Debugger.disable", new { }, timeout.Token).ConfigureAwait(false);
            }
            if (processId != verifiedDesktopPid) { Reason = "desktop-process-changed"; return null; }
            if (managers is null) return null;
            // Setup verifies the pinned observation source without selecting,
            // hydrating, or reading any conversation, even when none are bound.
            if (threadId.Length == 0)
            {
                var available = await Call("Runtime.callFunctionOn", new { objectId = managers,
                    functionDeclaration = "function(){return this.filter(m => m.constructor?.name === '" + profile!.Manager + "' && m.disposed === false && m.getHostId?.() === 'local' && typeof m.getConversation === 'function' && typeof m.threadStore?.stateReaders?.getListState === 'function').length === 1;}",
                    returnByValue = true, silent = true }, timeout.Token).ConfigureAwait(false);
                if (Object(Object(available, "result"), "value").ValueKind != JsonValueKind.True)
                { managers = null; Reason = "manager-unavailable"; return null; }
                Reason = "connection-ready-no-thread-tested"; return null;
            }
            var result = await Call("Runtime.callFunctionOn", new
            {
                objectId = managers, functionDeclaration = ProjectionFor(profile!),
                arguments = new[] { new { value = threadId } }, returnByValue = true, silent = true
            }, timeout.Token).ConfigureAwait(false);
            var snapshot = Parse(Object(Object(result, "result"), "value"), threadId);
            Reason = snapshot is null ? "thread-pending-unavailable" : "pinned-original-renderer-pending";
            return snapshot;
        }
        catch (Exception error) when (error is IOException or HttpRequestException or WebSocketException or JsonException or
            OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Reason = "pending-source-unavailable";
            socket.Abort();
            if (cancellation.IsCancellationRequested) cancellation.ThrowIfCancellationRequested();
            return null;
        }
        finally { gate.Release(); }
    }

    // This is a read of the desktop's OWN structured index. In particular,
    // unansweredAsyncQuestions is not reconstructed from chat prose, tool
    // output, question wording, elapsed time or visible UI text.
    internal const string Projection = """
        function(threadId) {
          const matches = this.filter(m => m.constructor?.name === 'Hfn' && m.disposed === false && m.getHostId?.() === 'local' && m.getConversation?.(threadId) != null);
          if (matches.length !== 1) return null;
          const m = matches[0], conversation = m.getConversation(threadId);
          if (conversation.id !== threadId || !m.threadStore?.stateReaders?.getListState) return null;
          const state = m.threadStore.stateReaders.getListState(conversation);
          if (!state || typeof state.latestTurnId !== 'string' || state.latestTurnStatus !== 'inProgress') return null;
          const turnId = state.latestTurnId, pending = [], methods = new Map([
            ['item/tool/requestUserInput', false], ['tool/requestUserInput', false],
            ['mcpServer/elicitation/request', false], ['item/commandExecution/requestApproval', true],
            ['item/fileChange/requestApproval', true], ['item/permissions/requestApproval', true],
            ['item/tool/requestOptionPicker', false]
          ]);
          if (!Array.isArray(state.requests) || typeof state.isResponseInProgress !== 'boolean') return null;
          for (const request of state.requests) {
            if (!methods.has(request.method) || request.completed === true) continue;
            if (request.params?.threadId !== threadId || request.params?.turnId !== turnId ||
                !['number','string'].includes(typeof request.id)) return null;
            pending.push({id: 'rpc:' + JSON.stringify(request.id), approval: methods.get(request.method)});
          }
          const async = state.unansweredAsyncQuestions;
          if (async != null) {
            if (async.turnId !== turnId || !Array.isArray(async.questionIds) || async.questionIds.some(id => typeof id !== 'string')) return null;
            for (const id of async.questionIds) pending.push({id: 'question:' + id, approval: false});
          }
          // An indexed blocking input not represented in requests needs its
          // exact request ID; never manufacture an approval or ignore an unknown kind.
          if (state.pendingRequest != null && pending.length === 0) {
            const p = state.pendingRequest;
            if (p.kind !== 'userInput' || !['number','string'].includes(typeof p.userInputRequestId)) return null;
            pending.push({id: 'rpc:' + JSON.stringify(p.userInputRequestId), approval: false});
          }
          return {schemaVersion: 1, hostId: 'local', threadId, turnId,
            status: state.latestTurnStatus, executing: state.isResponseInProgress === true,
            complete: true, overflow: pending.length > 128, pending: pending.slice(0,128)};
        }
        """;

    internal static string ProjectionFor(Profile selected) => Projection.Replace("'Hfn'", "'" + selected.Manager + "'", StringComparison.Ordinal);

    internal static CodexPendingSnapshot? Parse(JsonElement value, string threadId)
    {
        if (Object(value, "schemaVersion").ValueKind != JsonValueKind.Number || !Object(value, "schemaVersion").TryGetInt32(out var version) || version != 1 ||
            Text(value, "hostId") != "local" || Text(value, "threadId") != threadId || Text(value, "turnId").Length is 0 or > 256 ||
            Text(value, "status") != "inProgress" || Object(value, "complete").ValueKind != JsonValueKind.True ||
            Object(value, "executing").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            Object(value, "overflow").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
        var requests = Object(value, "pending");
        if (requests.ValueKind != JsonValueKind.Array || requests.GetArrayLength() > 128 || requests.EnumerateArray().Any(x =>
            Text(x, "id").Length is 0 or > 256 || Object(x, "approval").ValueKind is not (JsonValueKind.True or JsonValueKind.False))) return null;
        var items = requests.EnumerateArray().Select(x => new CodexPendingRequest(Text(x, "id"), Object(x, "approval").ValueKind == JsonValueKind.True)).ToArray();
        if (items.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != items.Length) return null;
        return new(threadId, Text(value, "turnId"), Object(value, "executing").ValueKind == JsonValueKind.True,
            Object(value, "overflow").ValueKind == JsonValueKind.True, items);
    }

    internal static JsonElement Merge(JsonElement status, CodexTurnMetadata[] turns, CodexPendingSnapshot? pending)
    {
        if (pending is null || Text(status, "type") != "active" ||
            turns.Count(x => x.Status == "inProgress") != 1 || !turns.Any(x => x.Id == pending.TurnId && x.Status == "inProgress")) return status;
        // Empty pending is actionable only together with actual in-progress
        // execution. A resolved answer on its own is never recovery evidence.
        var original = Object(status, "activeFlags");
        var flags = original.ValueKind == JsonValueKind.Array ? original.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        if (pending.Requests.Length == 0 && !pending.Overflow && !pending.Executing)
            return JsonSerializer.SerializeToElement(new { type = "active", activeFlags = flags.Append("pendingExecutionUnconfirmed").Distinct().ToArray() });
        if (pending.Requests.Any(x => x.Approval)) flags.Add("waitingOnApproval");
        else if (pending.Requests.Length > 0 || pending.Overflow) flags.Add("waitingOnUserInput");
        return JsonSerializer.SerializeToElement(new { type = "active", activeFlags = flags.Distinct().ToArray(),
            pendingSourceVersion = 1, pendingTurnId = pending.TurnId, pendingOverflow = pending.Overflow,
            pendingRequests = pending.Requests.Select(x => new { id = x.Id, approval = x.Approval }).ToArray() });
    }

    private async Task<JsonElement> Call(string method, object parameters, CancellationToken cancellation)
    {
        ReadStage = method;
        if (!Allowed(method)) throw new InvalidOperationException("Only fixed observation methods are allowed");
        var id = ++nextId;
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters }).AsMemory(), WebSocketMessageType.Text, true, cancellation).ConfigureAwait(false);
        var buffer = new byte[8192];
        while (true)
        {
            using var stream = new MemoryStream(); ValueWebSocketReceiveResult part;
            do
            {
                part = await socket.ReceiveAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false);
                if (part.MessageType != WebSocketMessageType.Text) throw new IOException("Inspector response unavailable");
                stream.Write(buffer, 0, part.Count);
                if (stream.Length > 262144) throw new IOException("Inspector response too large");
            } while (!part.EndOfMessage);
            using var document = JsonDocument.Parse(stream.ToArray()); var root = document.RootElement;
            if (Text(root, "method") == "Debugger.scriptParsed")
            {
                var script = Object(root, "params");
                var url = Text(script, "url"); var hash = Text(script, "hash");
                if (Object(script, "isModule").ValueKind == JsonValueKind.True &&
                    Uri.TryCreate(url, UriKind.Absolute, out var module) && module.Scheme == "app" && module.Host == "-" &&
                    module.Query.Length == 0 && module.Fragment.Length == 0 && module.UserInfo.Length == 0 &&
                    module.AbsolutePath.StartsWith("/assets/app-", StringComparison.Ordinal) && module.AbsolutePath.EndsWith(".js", StringComparison.Ordinal) &&
                    Profiles.Any(p => p.ModuleHash == hash || p.BaseHash == hash))
                    scripts[hash] = (Text(script, "scriptId"), url);
                if (Text(script, "url") == ModuleUrl && Text(script, "hash") == ModuleHash && Object(script, "isModule").ValueKind == JsonValueKind.True)
                    moduleScriptId = Text(script, "scriptId");
                if (Text(script, "url") == BaseModuleUrl && Text(script, "hash") == BaseModuleHash && Object(script, "isModule").ValueKind == JsonValueKind.True)
                    baseScriptId = Text(script, "scriptId");
            }
            if (Object(root, "id").ValueKind != JsonValueKind.Number || !Object(root, "id").TryGetInt32(out var responseId) || responseId != id) continue;
            if (Object(root, "error").ValueKind != JsonValueKind.Undefined || Object(Object(root, "result"), "exceptionDetails").ValueKind != JsonValueKind.Undefined)
                throw new IOException("Inspector read unavailable");
            return Object(root, "result").Clone();
        }
    }
    internal static bool Allowed(string method) => method is "Runtime.evaluate" or "Runtime.queryObjects" or
        "Runtime.callFunctionOn" or "Debugger.enable" or "Debugger.disable";
    private static async Task<byte[]> ReadBounded(Stream stream, int maximum, CancellationToken cancellation)
    {
        using var memory = new MemoryStream(); var buffer = new byte[4096]; int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
        { memory.Write(buffer, 0, count); if (memory.Length > maximum) throw new IOException("Inspector discovery too large"); }
        return memory.ToArray();
    }
    private static bool OwnsListener(int port, int expectedPid)
    {
        if (!OperatingSystem.IsWindows() || expectedPid <= 0) return false;
        var size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        if (size is < 4 or > 1048576) return false;
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(memory, ref size, false, 2, 3, 0) != 0) return false;
            var bytes = new byte[size]; Marshal.Copy(memory, bytes, 0, size);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            if (count > (size - 4) / 24) return false;
            for (var i = 0; i < count; i++)
            {
                var row = bytes.AsSpan(4 + i * 24, 24);
                if (row[4] == 127 && row[5] == 0 && row[6] == 0 && row[7] == 1 &&
                    BinaryPrimitives.ReadUInt16BigEndian(row.Slice(8, 2)) == port && BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(20, 4)) == expectedPid) return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static JsonElement Object(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var field) ? field : default;
    private static string Text(JsonElement value, string key) => Object(value, key).ValueKind == JsonValueKind.String ? Object(value, key).GetString()! : "";
    public ValueTask DisposeAsync() { socket.Abort(); socket.Dispose(); return ValueTask.CompletedTask; }
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);
}

internal sealed record CodexPendingRequest(string Id, bool Approval);
internal sealed record CodexPendingSnapshot(string ThreadId, string TurnId, bool Executing, bool Overflow, CodexPendingRequest[] Requests);
