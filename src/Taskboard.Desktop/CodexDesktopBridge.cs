using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Taskboard.Desktop;

// Only tools/list and the existing desktop's read_thread are available here.
// A verified local peer is necessary but not sufficient: every Read also checks
// the returned thread, host, schema and current status before granting live trust.
internal sealed class CodexDesktopBridge : IAsyncDisposable
{
    private const int MaximumRequestBytes = 4 * 1024 * 1024;
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private const string DesktopPackage = "OpenAI.Codex_2p2nqsd0c76g0";
    // Observed installed OpenAI-signed 26.1013.4894.0 artifact. Direct launch
    // can lack an MSIX process identity; accept only this exact protected image
    // while the user explicitly enables the loopback observer.
    internal const string ObservedExecutable = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1013.4894.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe";
    internal const string ObservedExecutableHash = "2F61EAEB01DCEE7FD18D572FBB338C88B686C0D891427487FDD182AFDD64B4F6";
    private readonly NamedPipeClientStream pipe;
    private readonly SemaphoreSlim calls = new(1, 1);
    private int nextId;
    private int disposed;
    internal static string DiscoveryReason { get; private set; } = "not-attempted";
    internal int ServerProcessId => GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id) ? checked((int)id) : 0;
    internal string IdentityKind { get; }
    // Logical operation allowlist; tools/call is never an arbitrary public escape.
    internal static bool Allowed(string method) => method is "tools/list" or "read_thread";
    private CodexDesktopBridge(NamedPipeClientStream pipe, string identityKind) { this.pipe = pipe; IdentityKind = identityKind; }

    internal static async Task<CodexDesktopBridge?> TryConnect(CancellationToken cancellation = default)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(4000);
        var candidates = new List<CodexDesktopBridge>();
        CodexDesktopBridge? selected = null;
        DiscoveryReason = "pipe-not-found";
        try
        {
            var paths = Directory.EnumerateFiles(@"\\.\pipe\", "codex-app-tools-*")
                .Where(path => IsDesktopPipeName(Path.GetFileName(path))).Take(16).ToArray();
            foreach (var path in paths)
            {
                NamedPipeClientStream? stream = null;
                try
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    DiscoveryReason = "desktop-pipe-connect-unavailable";
                    stream = new NamedPipeClientStream(".", Path.GetFileName(path), PipeDirection.InOut,
                        PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
                    await stream.ConnectAsync(350, timeout.Token).ConfigureAwait(false);
                    if (!VerifyServer(stream.SafePipeHandle, out var identityReason)) { DiscoveryReason = identityReason; stream.Dispose(); continue; }
                    DiscoveryReason = "verified-peer-tools-unavailable";
                    var bridge = new CodexDesktopBridge(stream, identityReason); stream = null;
                    try
                    {
                        var result = await bridge.Exchange("tools/list", new { threadStartKind = "default" }, timeout.Token).ConfigureAwait(false);
                        var tools = Object(result, "tools");
                        if (tools.ValueKind == JsonValueKind.Array && tools.EnumerateArray().Any(x =>
                            Text(x, "namespace") == "codex_app" && Text(x, "name") == "read_thread")) candidates.Add(bridge);
                        else await bridge.DisposeAsync().ConfigureAwait(false);
                    }
                    catch { await bridge.DisposeAsync().ConfigureAwait(false); throw; }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException or JsonException)
                { stream?.Dispose(); DiscoveryReason = "desktop-pipe-unavailable-" + error.GetType().Name + "-" + error.HResult.ToString("X8"); }
            }
            // More than one verified desktop is ambiguous. Never select by pipe name.
            if (candidates.Count == 1) { DiscoveryReason = "verified-original-desktop"; selected = candidates[0]; return selected; }
            if (candidates.Count > 1) DiscoveryReason = "ambiguous-verified-desktops";
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally
        {
            foreach (var candidate in candidates)
                if (!ReferenceEquals(candidate, selected)) await candidate.DisposeAsync().ConfigureAwait(false);
        }
        cancellation.ThrowIfCancellationRequested();
        return null;
    }

    internal async Task<CodexDesktopSnapshot> Read(string threadId, string? targetTurnId = null,
        CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(threadId) || threadId.Length > 256) return Unavailable("thread-unavailable");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(4000);
        var entered = false;
        try
        {
            await calls.WaitAsync(timeout.Token).ConfigureAwait(false); entered = true;
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            var turns = new List<CodexTurnMetadata>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            CodexDesktopSnapshot? latest = null;
            for (var page = 0; page < 8; page++)
            {
                var arguments = new Dictionary<string, object>
                {
                    ["threadId"] = threadId, ["hostId"] = "local", ["turnLimit"] = page == 0 ? 1 : 10,
                    ["includeOutputs"] = false, ["maxOutputCharsPerItem"] = 0
                };
                if (cursor is not null) arguments["cursor"] = cursor;
                var result = await Exchange("tools/call", new
                {
                    @namespace = "codex_app", tool = "read_thread", arguments,
                    callerSource = "codex", threadId, turnId = "instant-taskboard-readonly", callId = Guid.NewGuid().ToString("N")
                }, timeout.Token).ConfigureAwait(false);
                var parsed = Parse(result, threadId, out var next);
                if (!parsed.LiveCapable) return parsed;
                latest ??= parsed;
                foreach (var turn in parsed.Turns) if (ids.Add(turn.Id)) turns.Add(turn);
                // Current status is sampled again on older pages. If execution has
                // resumed, do not finish an older turn based on the earlier idle read.
                if (Text(parsed.Status, "type") == "active")
                    return parsed with { Turns = turns.ToArray() };
                if (parsed.Turns.Any(x => x.Status == "inProgress"))
                    return Unavailable("inconsistent-turn-state");
                if (targetTurnId is null || ids.Contains(targetTurnId) || next.Length == 0)
                    return latest with { Turns = turns.ToArray() };
                if (!cursors.Add(next)) return latest with { Turns = turns.ToArray(), Reason = "live-current-state-turn-cursor-cycle" };
                cursor = next;
            }
            return latest! with { Turns = turns.ToArray(), Reason = "live-current-state-turn-page-limit" };
        }
        catch (DesktopToolUnavailableException) { return Unavailable("thread-unavailable"); }
        catch (OperationCanceledException error) when (!cancellation.IsCancellationRequested)
        {
            // Once a frame times out, request/response alignment is no longer known.
            await DisposeAsync().ConfigureAwait(false);
            throw new IOException("Codex桌面只读连接超时", error);
        }
        catch (OperationCanceledException)
        { await DisposeAsync().ConfigureAwait(false); throw; }
        catch (Exception error) when (error is JsonException or EndOfStreamException)
        { await DisposeAsync().ConfigureAwait(false); throw new IOException("Codex桌面只读协议不可用", error); }
        finally { if (entered) calls.Release(); }
    }

    // This parser intentionally retains no messages, previews, outputs or titles.
    internal static CodexDesktopSnapshot Parse(JsonElement result, string requestedThreadId, out string nextCursor)
    {
        nextCursor = "";
        if (Object(result, "success").ValueKind != JsonValueKind.True) return Unavailable("thread-unavailable");
        var content = Object(result, "contentItems");
        if (content.ValueKind != JsonValueKind.Array) return Unavailable("unsupported-response-format");
        var parts = content.EnumerateArray().Where(x => Text(x, "type") == "inputText").Select(x => Text(x, "text"));
        JsonElement data;
        try { using var document = JsonDocument.Parse(string.Join("\n", parts)); data = document.RootElement.Clone(); }
        catch (JsonException) { return Unavailable("unsupported-response-format"); }
        if (Object(data, "schemaVersion").ValueKind != JsonValueKind.Number || !Object(data, "schemaVersion").TryGetInt32(out var version) || version != 1)
            return Unavailable("unsupported-schema-version");
        var thread = Object(data, "thread");
        if (Text(thread, "id") != requestedThreadId) return Unavailable("thread-identity-mismatch");
        if (Text(thread, "kind") != "codex") return Unavailable("unsupported-thread-kind");
        var host = Text(thread, "hostId");
        if (host != "local") return Unavailable("remote-host-not-supported", host);
        var rawStatus = Object(thread, "status"); var type = Text(rawStatus, "type");
        if (type is not ("active" or "idle")) return Unavailable(type == "notLoaded" ? "thread-not-loaded" : "unknown-live-state", host);
        var flags = Object(rawStatus, "activeFlags");
        if (type == "active" && flags.ValueKind != JsonValueKind.Array) return Unavailable("unsupported-status-flags", host);
        if (flags.ValueKind == JsonValueKind.Array && (flags.GetArrayLength() > 16 || flags.EnumerateArray().Any(x =>
            x.ValueKind != JsonValueKind.String || x.GetString()!.Length > 64))) return Unavailable("unsupported-status-flags", host);
        var safeFlags = flags.ValueKind == JsonValueKind.Array
            ? flags.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length <= 64).Take(16).ToArray() : [];
        var safeStatus = JsonSerializer.SerializeToElement(new { type, activeFlags = safeFlags });
        var rawTurns = Object(data, "turns");
        if (rawTurns.ValueKind != JsonValueKind.Array) return Unavailable("unsupported-turn-metadata", host);
        var newestFirst = Text(Object(data, "page"), "order") == "newest_first";
        var turns = rawTurns.EnumerateArray().Select((x, index) => new CodexTurnMetadata(Text(x, "id"), Text(x, "status"),
            CodexTurnMetadata.ReadStopReason(Object(x, "error")), newestFirst && index == 0 ? StartedAt(x) : null))
            .Where(x => x.Id.Length is > 0 and <= 256 && x.Status is "inProgress" or "completed" or "failed" or "interrupted").Take(10).ToArray();
        nextCursor = Text(Object(data, "page"), "nextCursor");
        if (nextCursor.Length > 8192) nextCursor = "";
        return new(safeStatus, turns, true, "live-original-desktop", host);
    }
    private static long? StartedAt(JsonElement turn)
    {
        var value = Object(turn, "startedAt");
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var timestamp) && timestamp is >= 0 and <= 253402300799 ? timestamp : null;
    }


    private async Task<JsonElement> Exchange(string method, object parameters, CancellationToken cancellation)
    {
        if (method is not ("tools/list" or "tools/call")) throw new InvalidOperationException("只读桌面接口不允许此操作");
        // All tools/call parameters are constructed privately above, with the sole
        // fixed tool read_thread. No arbitrary caller-supplied method/tool exists.
        var id = Interlocked.Increment(ref nextId);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { id, jsonrpc = "2.0", method, @params = parameters });
        if (payload.Length > MaximumRequestBytes) throw new IOException("Codex桌面请求超过上限");
        var frame = new byte[payload.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length); payload.CopyTo(frame, 4);
        await pipe.WriteAsync(frame, cancellation).ConfigureAwait(false);
        await pipe.FlushAsync(cancellation).ConfigureAwait(false);
        var header = new byte[4]; await pipe.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length == 0 || length > MaximumResponseBytes) throw new IOException("Codex桌面响应超过上限");
        var response = new byte[(int)length]; await pipe.ReadExactlyAsync(response, cancellation).ConfigureAwait(false);
        using var document = JsonDocument.Parse(response); var root = document.RootElement;
        var responseId = Object(root, "id");
        if (Text(root, "jsonrpc") != "2.0" || responseId.ValueKind != JsonValueKind.Number || !responseId.TryGetInt32(out var receivedId) || receivedId != id)
            throw new IOException("Codex桌面响应编号不匹配");
        if (Object(root, "error").ValueKind != JsonValueKind.Undefined) throw new DesktopToolUnavailableException();
        var result = Object(root, "result");
        if (result.ValueKind != JsonValueKind.Object) throw new IOException("Codex桌面响应格式无效");
        return result.Clone();
    }

    private static CodexDesktopSnapshot Unavailable(string reason, string host = "") => new(default, [], false, reason, host);
    private static string Text(JsonElement element, string name) => Object(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString()! : "";
    private static JsonElement Object(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    internal static bool IsDesktopPipeName(string name) => name.StartsWith("codex-app-tools-", StringComparison.Ordinal) && Guid.TryParseExact(name[16..], "D", out _);

    internal static bool MatchesObservedExecutable(int packageError, string path, string hash, string? inspector) =>
        packageError == 15700 && CodexPendingBridge.ValidEndpoint(inspector ?? "", out _) &&
        string.Equals(path, ObservedExecutable, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(hash, ObservedExecutableHash, StringComparison.OrdinalIgnoreCase);

    private static bool VerifyServer(SafePipeHandle pipeHandle, out string reason)
    {
        reason = "server-identity-unavailable";
        try
        {
            if (!GetNamedPipeServerProcessId(pipeHandle, out var processId)) return false;
            using var process = Process.GetProcessById(checked((int)processId));
            using var self = Process.GetCurrentProcess();
            if (process.SessionId != self.SessionId) { reason = "different-desktop-session"; return false; }
            using var handle = OpenProcess(0x1000, false, processId);
            if (handle.IsInvalid) return false;
            var family = new StringBuilder(512); uint count = (uint)family.Capacity;
            var packageError = GetPackageFamilyName(handle, ref count, family);
            var packageIdentity = packageError == 0 && family.ToString() == DesktopPackage;
            if (!packageIdentity)
            {
                reason = "desktop-package-identity-mismatch";
                if (packageError != 15700 || !CodexPendingBridge.ValidEndpoint(Environment.GetEnvironmentVariable(CodexPendingBridge.Setting) ?? "", out _)) return false;
                var image = new StringBuilder(32768); uint imageLength = (uint)image.Capacity;
                if (!QueryFullProcessImageName(handle, 0, image, ref imageLength)) return false;
                using var executable = new FileStream(ObservedExecutable, FileMode.Open, FileAccess.Read, FileShare.Read);
                // WindowsApps may be a volume junction (C: alias, D: image).
                // Compare the process path to the final path of the pinned file
                // handle, rather than trusting another supplied installation path.
                var canonical = new StringBuilder(32768);
                var finalLength = GetFinalPathNameByHandle(executable.SafeFileHandle, canonical, (uint)canonical.Capacity, 0);
                if (finalLength == 0 || finalLength >= canonical.Capacity) return false;
                var expectedImage = canonical.ToString();
                if (expectedImage.StartsWith(@"\\?\", StringComparison.Ordinal)) expectedImage = expectedImage[4..];
                if (!string.Equals(image.ToString(), expectedImage, StringComparison.OrdinalIgnoreCase)) return false;
                var hash = Convert.ToHexString(SHA256.HashData(executable));
                if (!MatchesObservedExecutable(packageError, ObservedExecutable, hash, Environment.GetEnvironmentVariable(CodexPendingBridge.Setting))) return false;
            }
            if (!OpenProcessToken(handle, 0x0008, out var token)) { reason = "desktop-user-token-unavailable"; return false; }
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            using (var current = WindowsIdentity.GetCurrent())
            {
                var valid = identity.User is not null && current.User is not null && identity.User.Equals(current.User);
                reason = valid ? (packageIdentity ? "verified-package-peer" : "verified-pinned-installed-image-peer") : "different-desktop-user";
                return valid;
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        { return false; }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) pipe.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class DesktopToolUnavailableException : IOException { }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint length, StringBuilder family);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}

internal sealed record CodexDesktopSnapshot(JsonElement Status, CodexTurnMetadata[] Turns, bool LiveCapable, string Reason, string HostId);
