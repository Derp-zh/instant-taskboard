using System.Text.Json;
using Taskboard.Desktop;

var checks = new List<string>();
void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
JsonElement Status(string type) => JsonSerializer.SerializeToElement(new { type });
JsonElement Event(string method, string id, string status = "inProgress", string thread = "A") => JsonSerializer.SerializeToElement(new { method, @params = new { threadId = thread, turn = new { id, status } } });
CodexTurnMetadata[] Turns(params (string Id, string Status)[] values) => values.Select(x => new CodexTurnMetadata(x.Id, x.Status)).ToArray();
string Method(JsonElement message) => message.GetProperty("method").GetString()!;
string TurnId(JsonElement message) => message.GetProperty("params").GetProperty("turn").GetProperty("id").GetString()!;
var tracker = new CodexTurnTracker("A");
Check(tracker.Reconcile(Status("idle"), Turns(("old", "completed")), true).Length == 0, "first idle completed history never becomes current completion");
Check(tracker.Reconcile(Status("idle"), Turns(("new-without-start", "completed"), ("old", "completed")), true).Length == 0, "newly discovered history without an observed active turn is inconclusive");
Check(tracker.Reconcile(Status("active"), Turns(("old", "inProgress")), true).Length == 0, "a known historical terminal cannot be revived by stale active metadata");
var start = tracker.Reconcile(Status("active"), Turns(("live", "inProgress"), ("old", "completed")), true);
Check(start.Length == 1 && Method(start[0]) == "turn/started" && TurnId(start[0]) == "live", "binding during execution establishes the exact active turn");
Check(tracker.Reconcile(Status("idle"), [], true).Length == 0, "idle without exact current terminal does not complete");
Check(tracker.Reconcile(Status("idle"), Turns(("unrelated", "completed")), true).Length == 0, "another completed turn does not finish the observed turn");
Check(tracker.Reconcile(Status("notLoaded"), Turns(("live", "completed")), true).Length == 0, "notLoaded does not masquerade as end of execution");
var terminal = tracker.Reconcile(Status("idle"), Turns(("live", "completed")), true);
Check(terminal.Length == 1 && Method(terminal[0]) == "turn/completed" && TurnId(terminal[0]) == "live", "missing completion notification is reconciled by exact observed turn metadata");
Check(tracker.Reconcile(Status("idle"), Turns(("live", "completed")), true).Length == 0, "polling terminal is idempotent");
Check(!tracker.ObserveNotification(Event("turn/completed", "live", "completed"), true), "real terminal arriving after reconciliation is not replayed");
Check(tracker.Reconcile(Status("active"), Turns(("live", "inProgress")), true).Length == 0, "late stale running turn cannot revive a completed turn");
Check(tracker.ObserveNotification(Event("turn/started", "second"), true), "new current notification starts a subsequent turn");
Check(!tracker.ObserveNotification(Event("turn/completed", "live", "completed"), true), "older terminal cannot override subsequent turn");
Check(!tracker.ObserveNotification(Event("turn/started", "live"), true), "retired start cannot override subsequent turn");
var rev = tracker.Revision;
Check(tracker.ObserveNotification(Event("thread/status/changed", "second"), true), "current thread state notification is accepted");
Check(tracker.Reconcile(Status("idle"), Turns(("second", "completed")), true, rev).Length == 0, "notification arriving during poll invalidates stale poll");
Check(tracker.Reconcile(Status("idle"), Turns(("second", "completed")), true, tracker.Revision).Length == 1, "a fresh retry can reconcile the current terminal");
Check(tracker.Reconcile(Status("idle"), Turns(("second", "completed")), true).Length == 0, "same binding state retained across reconnection does not replay terminal");
var rebound = new CodexTurnTracker("A");
Check(rebound.Reconcile(Status("idle"), Turns(("second", "completed")), true).Length == 0, "unbinding and rebinding establishes a fresh historical baseline");
Check(!rebound.ObserveNotification(Event("turn/completed", "unobserved", "completed"), true), "a terminal notification without an observed current turn cannot replay history on a new binding");
Check(rebound.Reconcile(Status("active"), Turns(("in-flight", "inProgress")), false).Length == 0 && rebound.CurrentTurnId is null, "non-authoritative historical service cannot establish current turn");
Check(!rebound.ObserveNotification(Event("turn/started", "in-flight"), false), "non-authoritative notification cannot establish current turn");
Check(!rebound.ObserveNotification(Event("turn/started", "foreign", thread: "B"), true), "thread B notification cannot contaminate A");
Check(rebound.Reconcile(Status("active"), Turns(("ambiguous-a", "inProgress"), ("ambiguous-b", "inProgress")), true).Length == 0, "multiple active turns are inconclusive");
Check(rebound.ObserveNotification(Event("turn/started", "observed"), true), "trustworthy current start establishes event baseline");
Check(!rebound.ObserveNotification(Event("turn/started", "observed"), true), "duplicate current start cannot clear pending requests");
Check(!rebound.ObserveNotification(Event("turn/completed", "observed", "unknown"), true), "unknown terminal status is not a completion");
Check(rebound.ObserveNotification(Event("turn/completed", "observed", "failed"), true), "exact observed failed terminal remains explicit");
var interrupted = new CodexTurnTracker("B");
interrupted.Reconcile(Status("active"), Turns(("b-live", "inProgress")), true);
Check(interrupted.Reconcile(Status("idle"), Turns(("b-live", "interrupted")), true).Single().GetProperty("params").GetProperty("turn").GetProperty("status").GetString() == "interrupted", "interrupted terminal preserves its status");
var changed = new CodexTurnTracker("A");
changed.Reconcile(Status("active"), Turns(("one", "inProgress")), true);
Check(TurnId(changed.Reconcile(Status("active"), Turns(("two", "inProgress"), ("one", "completed")), true).Single()) == "two", "later live turn supersedes older completed turn without intermediate false wait");

var requests = new List<JsonElement>();
Task<JsonElement> PageCall(string method, object parameters)
{
    Check(method == "thread/turns/list", "pagination uses only the allowed read method " + requests.Count);
    var p = JsonSerializer.SerializeToElement(parameters); requests.Add(p);
    return Task.FromResult(requests.Count == 1
        ? JsonSerializer.SerializeToElement(new { data = new[] { new { id = "newer", status = "completed", items = Array.Empty<object>() } }, nextCursor = "page-2" })
        : JsonSerializer.SerializeToElement(new { data = new[] { new { id = "target", status = "completed", items = Array.Empty<object>() } }, nextCursor = (string?)null }));
}
var page = await CodexTurnTracker.ReadTurnsAsync(PageCall, "A", "target");
Check(page.TargetFound && page.EndOfHistory && page.Pages == 2 && page.Turns[1].Id == "target", "exact current turn is found beyond the newest metadata page");
Check(requests.All(x => x.GetProperty("itemsView").GetString() == "notLoaded" && x.GetProperty("sortDirection").GetString() == "desc" && x.GetProperty("limit").GetInt32() == 32), "all requests exclude chat items and explicitly request newest-first metadata");
Check(requests[1].GetProperty("cursor").GetString() == "page-2", "opaque pagination cursor is forwarded unchanged");
var cycleCalls = 0;
var cycle = await CodexTurnTracker.ReadTurnsAsync((_, _) => { cycleCalls++; return Task.FromResult(JsonSerializer.SerializeToElement(new { data = Array.Empty<object>(), nextCursor = "same" })); }, "A", "absent");
Check(cycle.CursorCycle && !cycle.TargetFound && !cycle.EndOfHistory && cycleCalls == 2, "repeated cursor stops without manufacturing a terminal");
var cappedCalls = 0;
var capped = await CodexTurnTracker.ReadTurnsAsync((_, _) => Task.FromResult(JsonSerializer.SerializeToElement(new { data = Array.Empty<object>(), nextCursor = (++cappedCalls).ToString() })), "A", "absent", maximumPages: 3);
Check(capped.Pages == 3 && !capped.TargetFound && !capped.EndOfHistory && cappedCalls == 3, "page bound remains explicitly inconclusive");
var baselineCalls = 0;
var baseline = await CodexTurnTracker.ReadTurnsAsync((_, _) => { baselineCalls++; return Task.FromResult(JsonSerializer.SerializeToElement(new { data = Array.Empty<object>(), nextCursor = "more" })); }, "A");
Check(baselineCalls == 1 && !baseline.EndOfHistory, "initial baseline reads only the newest page rather than all history");
var malformed = await CodexTurnTracker.ReadTurnsAsync((_, _) => Task.FromResult(JsonSerializer.SerializeToElement(new { nextCursor = "more" })), "A", "absent");
Check(!malformed.TargetFound && !malformed.EndOfHistory, "malformed response is inconclusive");
var output = Path.GetFullPath(args.FirstOrDefault() ?? "test-output/codex-turn-tracker-0916/report.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = true, count = checks.Count, checks, scope = "Synthetic metadata and notification fixtures; not original desktop lifecycle acceptance", protocol = "codex-cli 0.163.0-alpha.10.2 local generated schema; desc/notLoaded/cursor" }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {checks.Count}: {output}");
