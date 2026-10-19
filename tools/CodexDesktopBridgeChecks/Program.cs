using System.Text.Json;
using Taskboard.Desktop;

try
{
var checks = new List<string>();
void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
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
        foreach (var target in targets)
        {
            var snapshot = await bridge!.Read(target.Id);
            Check(snapshot.LiveCapable, "original visible test chat " + target.Alias + " returned verified live metadata: " + snapshot.Reason);
            desktopReads.Add(new { alias = target.Alias, snapshot.LiveCapable, snapshot.Reason, snapshot.HostId, status = snapshot.Status,
                turns = snapshot.Turns.Select(x => new { idSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(x.Id))), x.Status }).ToArray() });
        }
        var invalid = await bridge!.Read(Guid.NewGuid().ToString("D"));
        Check(!invalid.LiveCapable && invalid.Reason == "thread-unavailable", "unavailable synthetic thread fails per-thread without destroying original desktop connection");
        Check((await bridge.Read(targets[0].Id)).LiveCapable, "valid original visible test chat remains readable after unavailable target");
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
