using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Taskboard.Desktop;

// One instance per binding identity/generation. Keep it across transport reconnects,
// discard it when the binding is removed. History can corroborate an observed turn;
// it cannot establish that an arbitrary old completed turn belongs to this binding.
internal sealed class CodexTurnTracker(string threadId)
{
    private readonly object gate = new();
    private readonly HashSet<string> historicalOrFinished = new(StringComparer.Ordinal);
    private string? current;
    private long revision;
    private string? metadataEpoch;
    private long? latestStartedAt;
    internal long Revision { get { lock (gate) return revision; } }
    internal string? CurrentTurnId { get { lock (gate) return current; } }
    internal bool CurrentTurnRunning { get { lock (gate) return current is not null && !historicalOrFinished.Contains(current); } }

    // Emit these messages BEFORE the corresponding active snapshot so a waiting flag
    // is retained. Only a trustworthy per-thread live source may set authoritative.
    internal JsonElement[] Reconcile(JsonElement status, IReadOnlyList<CodexTurnMetadata> turns,
        bool authoritative, long? expectedRevision = null, string? sourceEpoch = null)
    {
        lock (gate)
        {
            if (!authoritative || expectedRevision is { } expected && expected != revision) return [];
            var messages = new List<JsonElement>();
            var type = Text(status, "type");
            if (metadataEpoch != sourceEpoch) { metadataEpoch = sourceEpoch; latestStartedAt = null; }
            var newest = turns.FirstOrDefault();
            var previousWatermark = latestStartedAt;
            if (type == "active")
            {
                // Latest first is mandatory. Multiple active IDs are ambiguous, never
                // guess which turn should own the binding or revive an old finished ID.
                var running = turns.Where(x => x.Status == "inProgress" && x.Id.Length > 0)
                    .Select(x => x.Id).Distinct(StringComparer.Ordinal).Take(2).ToArray();
                if (running.Length == 1 && running[0] != current && !historicalOrFinished.Contains(running[0]))
                {
                    if (current is not null) historicalOrFinished.Add(current);
                    current = running[0]; revision++;
                    messages.Add(Message("turn/started", current, "inProgress"));
                }
            }
            else if (type == "idle" && current is not null && sourceEpoch is not null && previousWatermark is long baseline &&
                newest is { Status: "failed" or "interrupted", StartedAt: long started } && started is >= 0 and <= 253402300799 && started > baseline &&
                newest.Id != current && !historicalOrFinished.Contains(newest.Id))
            {
                // A new, strictly later latest turn can stop between polls. Do
                // not invent a Running event or revive an initial history row.
                historicalOrFinished.Add(current); current = newest.Id;
                historicalOrFinished.Add(current); revision++;
                messages.Add(JsonSerializer.SerializeToElement(new { method = "probe/currentTurnStopped", @params = new {
                    threadId, baselineStartedAt = baseline, turn = new { id = newest.Id, status = newest.Status, startedAt = started } } }));
            }
            else if (type == "idle" && current is not null && !historicalOrFinished.Contains(current))
            {
                var matching = turns.FirstOrDefault(x => x.Id == current);
                if (matching is not null && IsTerminal(matching.Status))
                {
                    historicalOrFinished.Add(current); revision++;
                    messages.Add(Message("turn/completed", current, matching.Status, matching.StopReason));
                }
            }
            foreach (var turn in turns)
                if (turn.Id != current && IsTerminal(turn.Status)) historicalOrFinished.Add(turn.Id);
            if (newest?.StartedAt is long timestamp && timestamp is >= 0 and <= 253402300799)
                latestStartedAt = latestStartedAt is long old ? Math.Max(old, timestamp) : timestamp;
            return messages.ToArray();
        }
    }

    // Called before forwarding a real notification. Returns false for another
    // thread, a historical source, duplicate/retired starts or unobserved terminals.
    internal bool ObserveNotification(JsonElement message, bool authoritative)
    {
        lock (gate)
        {
            var parameters = Object(message, "params");
            var id = Text(parameters, "threadId");
            if (id.Length == 0) id = Text(Object(parameters, "thread"), "id");
            if (!authoritative || id != threadId) return false;
            var method = Text(message, "method");
            var turn = Object(parameters, "turn"); var turnId = Text(parameters, "turnId");
            if (turnId.Length == 0) turnId = Text(turn, "id");
            if (method == "turn/started")
            {
                if (turnId.Length == 0 || turnId == current || historicalOrFinished.Contains(turnId)) return false;
                if (current is not null) historicalOrFinished.Add(current);
                current = turnId;
            }
            else if (method == "turn/completed")
            {
                if (turnId != current || turnId.Length == 0 || historicalOrFinished.Contains(turnId) || !IsTerminal(Text(turn, "status"))) return false;
                historicalOrFinished.Add(turnId);
            }
            // An event arriving during a poll invalidates that poll, including an
            // approval/request resolution/status update which can change its meaning.
            revision++;
            return true;
        }
    }

    private JsonElement Message(string method, string id, string status, string? stopReason = null) => JsonSerializer.SerializeToElement(
        new { method, @params = new { threadId, turn = new { id, status,
            error = stopReason is "usageLimitExceeded" or "rateLimitExceeded" ? new { codexErrorInfo = stopReason } : null,
            items = Array.Empty<object>() } } });
    private static bool IsTerminal(string status) => status is "completed" or "failed" or "interrupted";
    private static string Text(JsonElement element, string name) => Object(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString()! : "";
    private static JsonElement Object(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    // Local codex 0.163.0-alpha.10.2 schema: desc + notLoaded returns metadata
    // only. Never request items or reconstruct a completion from assistant prose.
    // A missing target/capped/cyclic page is inconclusive, not a completed turn.
    internal static async Task<CodexTurnPage> ReadTurnsAsync(Func<string, object, Task<JsonElement>> call,
        string threadId, string? targetTurnId = null, CancellationToken cancellation = default,
        int maximumPages = 8)
    {
        var turns = new List<CodexTurnMetadata>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var page = 0; page < Math.Clamp(maximumPages, 1, 32); page++)
        {
            cancellation.ThrowIfCancellationRequested();
            var response = await call("thread/turns/list", new { threadId, cursor, limit = 32, sortDirection = "desc", itemsView = "notLoaded" }).ConfigureAwait(false);
            var data = Object(response, "data");
            if (data.ValueKind != JsonValueKind.Array) return new(turns.ToArray(), false, false, page + 1, false);
            foreach (var item in data.EnumerateArray())
            {
                var id = Text(item, "id");
                if (id.Length > 0 && ids.Add(id)) turns.Add(new(id, Text(item, "status"), CodexTurnMetadata.ReadStopReason(Object(item, "error"))));
            }
            var next = Text(response, "nextCursor");
            var found = targetTurnId is not null && ids.Contains(targetTurnId);
            if (found || next.Length == 0 || targetTurnId is null)
                return new(turns.ToArray(), next.Length == 0, found, page + 1, false);
            if (!cursors.Add(next)) return new(turns.ToArray(), false, false, page + 1, true);
            cursor = next;
        }
        return new(turns.ToArray(), false, targetTurnId is not null && ids.Contains(targetTurnId), Math.Clamp(maximumPages, 1, 32), false);
    }
}

internal sealed record CodexTurnMetadata(string Id, string Status, string? StopReason = null, long? StartedAt = null)
{
    // Retain only the protocol enum. Error prose, URLs, outputs and account
    // quota snapshots never enter the metadata projection.
    internal static string? ReadStopReason(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object || !error.TryGetProperty("codexErrorInfo", out var info)) return null;
        if (info.ValueKind == JsonValueKind.String)
            return info.GetString() is "usageLimitExceeded" or "rateLimitExceeded" ? info.GetString() : null;
        if (info.ValueKind == JsonValueKind.Object)
        {
            var properties = info.EnumerateObject().Take(2).ToArray();
            if (properties.Length == 1 && properties[0].Name is "usageLimitExceeded" or "rateLimitExceeded") return properties[0].Name;
        }
        return null;
    }
}
internal sealed record CodexTurnPage(CodexTurnMetadata[] Turns, bool EndOfHistory, bool TargetFound, int Pages, bool CursorCycle);
