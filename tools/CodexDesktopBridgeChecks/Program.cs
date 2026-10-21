using System.Text.Json;
using Taskboard.Desktop;

try
{
const string registeredExecutable = @"D:\RegisteredCodex\app\ChatGPT.exe";
var checks = new List<string>();
void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
Check(CodexDesktopBridge.MatchesObservedExecutable(15700, registeredExecutable, CodexDesktopBridge.ObservedExecutableHash, "http://127.0.0.1:9339", registeredExecutable), "explicit observer accepts only the exact-hash registered installation image when MSIX process identity is absent");
foreach (var invalidIdentity in new[] {
    (0, registeredExecutable, CodexDesktopBridge.ObservedExecutableHash, "http://127.0.0.1:9339"),
    (15700, @"C:\Temp\ChatGPT.exe", CodexDesktopBridge.ObservedExecutableHash, "http://127.0.0.1:9339"),
    (15700, registeredExecutable, "wrong-hash", "http://127.0.0.1:9339"),
    (15700, registeredExecutable, CodexDesktopBridge.ObservedExecutableHash, ""),
    (15700, registeredExecutable, CodexDesktopBridge.ObservedExecutableHash, "http://remote.invalid:9339") })
    Check(!CodexDesktopBridge.MatchesObservedExecutable(invalidIdentity.Item1, invalidIdentity.Item2, invalidIdentity.Item3, invalidIdentity.Item4, registeredExecutable), "direct launch identity fails closed for wrong error, location, artifact or observer configuration");
JsonElement Reply(string id = "A", string type = "active", string host = "local", string kind = "codex", int schemaVersion = 1,
    bool success = true, string turnStatus = "inProgress", string[]? flags = null) => JsonSerializer.SerializeToElement(new
{
    success,
    contentItems = new[] { new { type = "inputText", text = JsonSerializer.Serialize(new
    {
        schemaVersion, thread = new { id, kind, hostId = host, status = new { type, activeFlags = flags ?? [] } },
        turns = new[] { new { id = "synthetic-turn", status = turnStatus, output = "MUST_NOT_RETAIN" } },
        page = new { nextCursor = "opaque-cursor" }
    }) } }
});
var active = CodexDesktopBridge.Parse(Reply(), "A", out var cursor);
Check(active.LiveCapable && active.HostId == "local" && active.Status.GetProperty("type").GetString() == "active", "validated local Codex active status is live capable");
Check(cursor == "opaque-cursor" && active.Turns.Single().Id == "synthetic-turn", "turn identity and opaque cursor are retained");
Check(!JsonSerializer.Serialize(active).Contains("MUST_NOT_RETAIN", StringComparison.Ordinal), "no output or message text survives metadata projection");
Check(CodexDesktopBridge.Parse(Reply(type: "idle", turnStatus: "completed"), "A", out _).LiveCapable, "validated local idle status is live capable without inferring a completion");
var question = CodexDesktopBridge.Parse(Reply(flags: ["waitingOnUserInput"]), "A", out _);
Check(question.Status.GetProperty("activeFlags")[0].GetString() == "waitingOnUserInput", "question wait flag is preserved");
var permission = CodexDesktopBridge.Parse(Reply(flags: ["waitingOnApproval"]), "A", out _);
JsonElement StopReply(string code, string order = "newest_first", long startedAt = 1000) => JsonSerializer.SerializeToElement(new {
    success = true, contentItems = new[] { new { type = "inputText", text = JsonSerializer.Serialize(new {
        schemaVersion = 1, thread = new { id = "A", kind = "codex", hostId = "local", status = new { type = "idle", activeFlags = Array.Empty<string>() } },
        turns = new[] { new { id = "stop-current", status = "failed", startedAt,
            error = new { codexErrorInfo = code, message = "PRIVATE_ERROR_MUST_NOT_RETAIN", additionalDetails = "PRIVATE_DETAIL" } } }, page = new { order }
    }) } } });
var quota = CodexDesktopBridge.Parse(StopReply("usageLimitExceeded"), "A", out _);
Check(quota.Turns.Single() is { Status: "failed", StopReason: "usageLimitExceeded", StartedAt: 1000 }, "bounded quota enum and newest-start metadata survive projection");
Check(!JsonSerializer.Serialize(quota).Contains("PRIVATE", StringComparison.Ordinal), "error body and additional details are discarded");
Check(CodexDesktopBridge.Parse(StopReply("arbitrary-prose"), "A", out _).Turns.Single().StopReason is null, "unknown error text is not treated as a quota enum");
Check(CodexDesktopBridge.Parse(StopReply("rateLimitExceeded", order: "oldest-first"), "A", out _).Turns.Single().StartedAt is null, "unverified metadata order cannot establish a newest-start watermark");
Check(CodexDesktopBridge.Parse(StopReply("rateLimitExceeded", order: "newest-first"), "A", out _).Turns.Single().StartedAt is null, "lookalike order spelling cannot establish a newest-start watermark");
Check(CodexDesktopBridge.Parse(StopReply("usageLimitExceeded", startedAt: 1790000000000), "A", out _).Turns.Single().StartedAt is null, "millisecond or invalid timestamp cannot masquerade as Unix seconds");
Check(permission.Status.GetProperty("activeFlags")[0].GetString() == "waitingOnApproval", "approval wait flag is preserved");
Check(!CodexDesktopBridge.Parse(Reply(id: "B"), "A", out _).LiveCapable, "another thread response cannot supply trust");
Check(!CodexDesktopBridge.Parse(Reply(host: "remote"), "A", out _).LiveCapable, "remote host is explicitly unsupported");
Check(!CodexDesktopBridge.Parse(Reply(host: ""), "A", out _).LiveCapable, "missing response host is inconclusive");
Check(!CodexDesktopBridge.Parse(Reply(kind: "chat"), "A", out _).LiveCapable, "non-Codex kind does not inherit local desktop trust");
Check(!CodexDesktopBridge.Parse(Reply(schemaVersion: 2), "A", out _).LiveCapable, "unrecognized schema cannot grant trust");
Check(!CodexDesktopBridge.Parse(Reply(type: "notLoaded"), "A", out _).LiveCapable, "unloaded thread does not inherit connection trust");
Check(!CodexDesktopBridge.Parse(Reply(type: "systemError"), "A", out _).LiveCapable, "system error does not infer completion");
Check(!CodexDesktopBridge.Parse(Reply(success: false), "A", out _).LiveCapable, "failed tool response is unavailable per-thread");
Check(!CodexDesktopBridge.Parse(JsonSerializer.SerializeToElement(new { success = true, contentItems = new[] { new { type = "inputText", text = "invalid" } } }), "A", out _).LiveCapable, "malformed content is inconclusive");
Check(CodexDesktopBridge.IsDesktopPipeName("codex-app-tools-" + Guid.NewGuid().ToString("D")), "only exact advertised GUID pipe naming is eligible");
Check(!CodexDesktopBridge.IsDesktopPipeName("codex-app-tools-fake") && !CodexDesktopBridge.IsDesktopPipeName("codex-app-tools-" + Guid.NewGuid().ToString("N")), "lookalike pipe names do not qualify");
Check(!CodexDesktopBridge.IsDesktopPipeName("other-" + Guid.NewGuid().ToString("D")), "other GUID pipes are ignored");
Check(CodexDesktopBridge.Allowed("tools/list") && CodexDesktopBridge.Allowed("read_thread") &&
    new[] { "tools/call", "create_thread", "send_message_to_thread", "approve", "answer", "thread/start", "turn/start" }.All(x => !CodexDesktopBridge.Allowed(x)), "logical operation allowlist exposes no arbitrary tool execution or writes");
var malformedFlagsData = JsonSerializer.Serialize(new { schemaVersion = 1, thread = new { id = "A", kind = "codex", hostId = "local", status = new { type = "active", activeFlags = "waitingOnApproval" } }, turns = Array.Empty<object>() });
Check(!CodexDesktopBridge.Parse(JsonSerializer.SerializeToElement(new { success = true, contentItems = new[] { new { type = "inputText", text = malformedFlagsData } } }), "A", out _).LiveCapable, "malformed waiting flags cannot be silently dropped and reclassified as running");

bool? verifiedDesktopConnected = null;
var desktopReads = new List<object>();
if (args.Contains("--discover"))
{
    await using var bridge = await CodexDesktopBridge.TryConnect();
    verifiedDesktopConnected = bridge is not null;
    Console.WriteLine("Discovery: " + CodexDesktopBridge.DiscoveryReason);
    Check(verifiedDesktopConnected == true, "installed desktop peer passed package/session/user identity and advertised read_thread checks");
    var targetOption = Array.IndexOf(args, "--targets");
    if (targetOption >= 0)
    {
        using var targetDocument = JsonDocument.Parse(await File.ReadAllTextAsync(args[targetOption + 1]));
        var targets = targetDocument.RootElement.EnumerateArray().Select(x => (Alias: x.GetProperty("alias").GetString()!, Id: x.GetProperty("threadId").GetString()!)).ToArray();
        await using var pendingSource = new CodexPendingBridge();
        foreach (var target in targets)
        {
            var snapshot = await bridge!.Read(target.Id);
            Console.WriteLine($"Original {target.Alias}: {snapshot.Reason}");
            Check(snapshot.LiveCapable, "original visible test chat " + target.Alias + " returned verified live metadata: " + snapshot.Reason);
            Check(snapshot.Turns.FirstOrDefault()?.StartedAt is >= 0 and <= 253402300799,
                "original test chat " + target.Alias + " exposes a verified newest_first Unix-second baseline; not a real quota-stop acceptance");
            desktopReads.Add(new { alias = target.Alias, bridge.IdentityKind, snapshot.LiveCapable, snapshot.Reason, snapshot.HostId, status = snapshot.Status,
                turns = snapshot.Turns.Select(x => new { idSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(x.Id))), x.Status, x.StartedAt }).ToArray() });
            if (args.Contains("--pending"))
            {
                var pending = await pendingSource.Read(target.Id, bridge.ServerProcessId, CancellationToken.None);
                Console.WriteLine($"Pending {target.Alias}: {pendingSource.Reason} at {pendingSource.ReadStage}");
                desktopReads.Add(new { alias = target.Alias, pendingReadAttempted = true, reason = pendingSource.Reason,
                    pendingSource.ReadStage,
                    available = pending is not null, sameNativeTurn = pending is not null && snapshot.Turns.Any(x => x.Id == pending.TurnId && x.Status == "inProgress"),
                    requestCount = pending?.Requests.Length, approvalCount = pending?.Requests.Count(x => x.Approval),
                    executing = pending?.Executing, overflow = pending?.Overflow });
            }
        }
        if (args.Contains("--unavailable-probe"))
        {
            var invalid = await bridge!.Read(Guid.NewGuid().ToString("D"));
            Check(!invalid.LiveCapable && invalid.Reason == "thread-unavailable", "unavailable synthetic thread fails per-thread without destroying original desktop connection");
            Check((await bridge.Read(targets[0].Id)).LiveCapable, "valid original visible test chat remains readable after unavailable target");
        }
    }
}
var output = Path.GetFullPath(args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal)) ?? "test-output/codex-desktop-bridge-checks/report.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = true, count = checks.Count, checks, verifiedDesktopConnected,
    desktopReads, scope = "Synthetic fixtures; optional verified original desktop tool discovery and explicitly selected test chat metadata reads. No question/permission/resume/end or board lane acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {checks.Count}: {output}");
return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
