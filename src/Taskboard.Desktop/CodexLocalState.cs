using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Taskboard.Desktop;

// Version 11 desktop state stream. Apply revisions to a structural projection,
// never to a retained transcript. Question replies use the desktop's exact
// structured envelope, not language/UI heuristics or tool return values.
internal sealed class CodexLocalState(string threadId)
{
    private JsonNode? state;
    private long revision = -1;
    private string owner = "", reason = "local-chat-not-cached";
    private bool valid, waiting, executingAfterWait, pendingCleared;
    private string waitingTurn = "";
    internal long Revision => revision;
    internal bool NeedsSnapshot => !valid && reason == "local-state-revision-gap";
    internal string RetainedMetadata => state?.ToJsonString() ?? "";
    internal bool Apply(JsonElement version, string source, JsonElement change)
    {
        try
        {
            if (!version.TryGetInt32(out var protocol) || protocol != 11 || !Guid.TryParse(source, out _))
                return Invalidate("unsupported-local-state-protocol");
            var next = Number(change, "revision"); var type = Text(change, "type");
            if (next < 0) return Invalidate("invalid-local-revision");
            if (type == "snapshot")
            {
                if (valid && source == owner && next <= revision) return false;
                executingAfterWait = false; pendingCleared = false;
                var raw = Object(change, "conversationState");
                if (Text(raw, "id") != threadId || Text(raw, "hostId") is not ("" or "local"))
                    return Invalidate("local-thread-identity-mismatch");
                state = Project(raw, Role.Root); owner = source; revision = next; valid = true; reason = "local-state-events";
                return true;
            }
            if (type != "patches" || !valid || source != owner || Number(change, "baseRevision") != revision || next != revision + 1)
                return Invalidate("local-state-revision-gap");
            var patches = Object(change, "patches");
            if (patches.ValueKind != JsonValueKind.Array || patches.GetArrayLength() > 16384) return Invalidate("invalid-local-patches");
            var changed = false;
            foreach (var patch in patches.EnumerateArray())
            {
                var path = Object(patch, "path");
                if (path.ValueKind != JsonValueKind.Array || path.GetArrayLength() > 24) return Invalidate("invalid-local-patch-path");
                var parts = path.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! :
                    x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var index) && index >= 0 ? index.ToString(System.Globalization.CultureInfo.InvariantCulture) : throw new InvalidOperationException()).ToArray();
                var role = Role.Root;
                foreach (var part in parts) role = Child(role, part);
                if (role == Role.Ignore)
                {
                    var itemIndex = Array.IndexOf(parts, "items");
                    if (waiting && pendingCleared && Text(patch, "op") is "add" or "replace" && itemIndex >= 0 && parts.Length == itemIndex + 3 && parts[^1] == "text" &&
                        String(At(parts.Take(itemIndex).ToArray())?["turnId"]) == waitingTurn && String(ItemAt(parts)?["type"]) == "agentMessage" &&
                        Object(patch, "value").ValueKind == JsonValueKind.String && Object(patch, "value").GetString()!.Length > 0)
                    { executingAfterWait = true; changed = true; }
                    continue;
                }
                // Content/input are atomically supplied by the desktop for user
                // messages. Nested transcript text changes are never retained.
                var op = Text(patch, "op");
                if (op is not ("add" or "replace" or "remove")) return Invalidate("unsupported-local-patch-operation");
                var value = Object(patch, "value");
                var oldItem = ItemAt(parts);
                var oldCount = ItemArrayAt(parts)?.Count ?? -1;
                var previousValue = At(parts)?.DeepClone();
                var newValue = op == "remove" ? null : Project(value, role);
                Set(parts, newValue, op);
                changed = true;
                var item = ItemAt(parts);
                if (waiting) _ = Snapshot();
                var itemPathIndex = Array.IndexOf(parts, "items");
                if (waiting && pendingCleared && itemPathIndex >= 0 && String(At(parts.Take(itemPathIndex).ToArray())?["turnId"]) == waitingTurn &&
                    IsExecutionChange(parts, oldItem, item, oldCount, op)) executingAfterWait = true;
                var startedIndex = Array.IndexOf(parts, "commandExecutionStartedAtMsById");
                if (waiting && pendingCleared && op is "add" or "replace" && startedIndex >= 0 && parts.Length == startedIndex + 2 &&
                    String(At(parts.Take(startedIndex).ToArray())?["turnId"]) == waitingTurn &&
                    value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var started) && started > 0 &&
                    (previousValue is null || previousValue.GetValue<double>() < started) &&
                    At(parts.Take(startedIndex).ToArray())?["completedItemIds"]?[parts[^1]]?.ToJsonString() != "true") executingAfterWait = true;
            }
            revision = next; return changed;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or JsonException or FormatException or OverflowException)
        { return Invalidate("invalid-local-state-schema"); }
    }
    private bool Invalidate(string why) { valid = false; state = null; executingAfterWait = false; pendingCleared = false; reason = why; return true; }
    internal CodexDesktopSnapshot Snapshot()
    {
        CodexDesktopSnapshot Unavailable(string why) => new(default, [], false, why, "local");
        if (!valid || state is null) return Unavailable(reason);
        try
        {
            var runtime = state["threadRuntimeStatus"]; var type = String(runtime?["type"]);
            if (type is not ("active" or "idle")) return Unavailable("unknown-local-runtime-state");
            if (runtime?["activeFlags"] is not JsonArray flagsNode && type == "active") return Unavailable("unknown-local-runtime-flags");
            var flags = (runtime?["activeFlags"] as JsonArray)?.Select(String).ToArray() ?? [];
            if (flags.Length > 16 || flags.Any(x => x.Length > 64)) return Unavailable("unknown-local-runtime-flags");
            var turns = OrderedTurns(); if (turns is null) return Unavailable("local-latest-history-not-cached");
            var latest = turns.LastOrDefault(x => String(x?["turnId"]).Length > 0 || String(x?["status"]) is not ("completed" or "failed"));
            var id = String(latest?["turnId"]); var status = String(latest?["status"]);
            if (type == "active" && (id.Length == 0 || status != "inProgress") || type == "idle" && status == "inProgress")
                return Unavailable("inconsistent-local-turn-state");
            var metadata = turns.Where(x => String(x?["turnId"]).Length > 0).Reverse().Take(4096).Select(x =>
            {
                var ms = x?["turnStartedAtMs"]?.GetValue<double>();
                long? start = ms is >= 0 and <= 253402300799999 ? (long)(ms.Value / 1000) : null;
                var stop = String(x?["error"]?["codexErrorInfo"]);
                return new CodexTurnMetadata(String(x?["turnId"]), String(x?["status"]), stop is "usageLimitExceeded" or "rateLimitExceeded" ? stop : null, start);
            }).DistinctBy(x => x.Id).ToArray();
            if (metadata.Any(x => x.Id.Length > 256 || x.Status is not ("inProgress" or "completed" or "failed" or "interrupted")))
                return Unavailable("unknown-local-turn-schema");
            var requests = new List<CodexPendingRequest>();
            if (state["requests"] is not JsonArray rpcRequests) return Unavailable("unknown-local-request-schema");
            foreach (var request in rpcRequests)
            {
                var method = String(request?["method"]);
                var approval = method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" or "item/permissions/requestApproval";
                var dynamicInput = method == "item/tool/call" && request?["params"]?["localInputPending"]?.ToJsonString() == "true";
                if (!approval && !dynamicInput && method is not ("item/tool/requestUserInput" or "tool/requestUserInput" or "mcpServer/elicitation/request" or "item/tool/requestOptionPicker")) continue;
                if (request?["completed"]?.ToJsonString() == "true") continue;
                if (String(request?["params"]?["threadId"]) != threadId || String(request?["params"]?["turnId"]) != id || request?["id"] is not JsonValue rid)
                    return Unavailable("local-request-identity-mismatch");
                requests.Add(new("rpc:" + rid.ToJsonString(), approval));
            }
            if (latest?["items"] is not JsonArray items && type == "active") return Unavailable("unknown-local-item-schema");
            if (latest?["items"] is JsonArray currentItems && status == "inProgress")
            {
                // Canonical and legacy can temporarily carry distinct live
                // items for the same turn. Include their structural pending
                // evidence; never replace a terminal canonical turn with an
                // older legacy in-progress record.
                var legacy = (state["turns"] as JsonArray)?.LastOrDefault(x => String(x?["turnId"]) == id);
                var supplemental = legacy?["items"] as JsonArray;
                var indexedItems = currentItems.Concat(supplemental ?? []).GroupBy(x => String(x?["id"]), StringComparer.Ordinal).Select(g =>
                    g.FirstOrDefault(x => x?["questions"] is JsonArray { Count: > 0 }) ?? g.First()).ToArray();
                foreach (var item in indexedItems)
                {
                    if (String(item?["type"]) == "userInputResponse" && item?["completed"]?.ToJsonString() == "false" && item["requestId"] is JsonValue requestId)
                        requests.Add(new("rpc:" + requestId.ToJsonString(), false));
                    if (String(item?["type"]) == "planImplementation" && item?["isCompleted"]?.ToJsonString() == "false")
                        requests.Add(new("plan:" + String(item["id"]), false));
                }
                var answered = currentItems.Concat(supplemental ?? []).Where(x => String(x?["type"]) == "userMessage" ||
                    String(x?["type"]) == "steeringUserMessage" && String(x?["status"]) == "accepted")
                    .SelectMany(x => (x?[String(x?["type"]) == "userMessage" ? "content" : "input"] as JsonArray ?? []).Select(String)).ToHashSet(StringComparer.Ordinal);
                foreach (var item in indexedItems.Where(x => String(x?["type"]) == "agentMessage"))
                    if (item?["questions"] is JsonArray questions)
                        for (var i = 0; i < questions.Count; i++)
                        {
                            var question = JsonSerializer.Serialize(new object[] { "request_user_input_async", String(item["id"]), i }, JsonOptions);
                            if (!answered.Contains(question)) requests.Add(new("question:" + question, false));
                        }
            }
            if (requests.Count > 0 || flags.Any(x => x is "waitingOnApproval" or "waitingOnUserInput"))
            {
                if (!waiting || waitingTurn != id) { waitingTurn = id; executingAfterWait = false; }
                waiting = true; pendingCleared = false; executingAfterWait = false;
            }
            else if (waiting && waitingTurn != id) { waiting = false; executingAfterWait = false; pendingCleared = false; }
            else if (waiting) pendingCleared = true;
            // A reply / resolved request alone is not evidence of resumed work.
            // Require a subsequent real assistant/tool execution change.
            var executing = type == "active" && status == "inProgress" && (!waiting || executingAfterWait);
            if (executing && requests.Count == 0 && flags.Length == 0) waiting = false;
            var pending = type == "active" ? new CodexPendingSnapshot(threadId, id, executing, requests.Count > 128, requests.DistinctBy(x => x.Id).Take(128).ToArray()) : null;
            var rawStatus = JsonSerializer.SerializeToElement(new { type, activeFlags = flags });
            return new(CodexPendingBridge.Merge(rawStatus, metadata, pending), metadata, true, "local-state-events", "local");
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException)
        { return Unavailable("unknown-local-state-schema"); }
    }
    private JsonNode?[]? OrderedTurns()
    {
        if (String(state?["turnHistory"]?["kind"]) == "canonical")
        {
            var history = state?["turnHistory"]?["history"];
            if (history?["islands"] is not JsonArray islands || islands.LastOrDefault() is not JsonNode last ||
                String(last["newerBoundary"]?["status"]) != "exhausted" || last["entries"] is not JsonArray entries ||
                history["entitiesByKey"] is not JsonObject entities) return null;
            return entries.Select(x => entities[String(x?["value"])]).ToArray();
        }
        return state?["turns"] is JsonArray turns ? turns.ToArray() : null;
    }
    private JsonNode? At(string[] path) { var node = state; foreach (var p in path) node = node is JsonArray a ? int.TryParse(p, out var i) && i >= 0 && i < a.Count ? a[i] : null : node?[p]; return node; }
    private JsonNode? ItemAt(string[] path) { var i = Array.IndexOf(path, "items"); return i >= 0 && path.Length > i + 1 ? At(path.Take(i + 2).ToArray())?.DeepClone() : null; }
    private JsonArray? ItemArrayAt(string[] path) { var i = Array.IndexOf(path, "items"); return i >= 0 ? At(path.Take(i + 1).ToArray()) as JsonArray : null; }
    private static bool IsExecutionChange(string[] path, JsonNode? before, JsonNode? after, int oldCount, string op)
    {
        if (op == "remove" || after is null) return false;
        var i = Array.IndexOf(path, "items"); if (i < 0 || path.Length <= i + 1) return false;
        var type = String(after["type"]);
        if (type is not ("agentMessage" or "reasoning" or "commandExecution" or "mcpToolCall" or "dynamicToolCall" or "collabAgentToolCall" or "subAgentActivity")) return false;
        if (before is null || int.Parse(path[i + 1]) >= oldCount) return true;
        return String(before["status"]) != "inProgress" && String(after["status"]) == "inProgress";
    }
    private void Set(string[] path, JsonNode? value, string op)
    {
        if (path.Length == 0) { state = value; return; }
        var parent = At(path.Take(path.Length - 1).ToArray()); var key = path[^1];
        if (parent is JsonArray a)
        {
            var i = int.Parse(key); if (i < 0 || i > a.Count || a.Count > 65536) throw new InvalidOperationException();
            if (op == "remove") a.RemoveAt(i); else if (op == "add") a.Insert(i, value); else a[i] = value;
        }
        else if (parent is JsonObject o) { if (op == "remove") o.Remove(key); else o[key] = value; }
        else throw new InvalidOperationException();
    }
    private enum Role { Ignore, Root, Scalar, Scalars, StopReason, Placeholder, Runtime, Flags, Requests, Request, Params, Turns, Turn, TurnHistory, History, Entities, Islands, Island, Boundary, Entries, Entry, Error, Items, Item, Questions, Replies }
    private static Role Child(Role role, string key) => role switch
    {
        Role.Root => key switch { "id" or "hostId" => Role.Scalar, "threadRuntimeStatus" => Role.Runtime, "requests" => Role.Requests, "turns" => Role.Turns, "turnHistory" => Role.TurnHistory, _ => Role.Ignore },
        Role.Runtime => key == "type" ? Role.Scalar : key == "activeFlags" ? Role.Flags : Role.Ignore,
        Role.Requests => Role.Request, Role.Request => key is "id" or "method" or "completed" ? Role.Scalar : key == "params" ? Role.Params : Role.Ignore,
        Role.Params => key is "threadId" or "turnId" or "tool" ? Role.Scalar : Role.Ignore,
        Role.Flags or Role.Scalars => Role.Scalar, Role.Turns or Role.Entities => Role.Turn,
        Role.Turn => key switch { "turnId" or "status" or "turnStartedAtMs" => Role.Scalar, "items" => Role.Items, "error" => Role.Error, "commandExecutionStartedAtMsById" or "completedItemIds" => Role.Scalars, _ => Role.Ignore },
        Role.TurnHistory => key == "kind" ? Role.Scalar : key == "history" ? Role.History : Role.Ignore,
        Role.History => key == "entitiesByKey" ? Role.Entities : key == "islands" ? Role.Islands : Role.Ignore,
        Role.Islands => Role.Island, Role.Island => key == "entries" ? Role.Entries : key == "newerBoundary" ? Role.Boundary : Role.Ignore,
        Role.Boundary => key == "status" ? Role.Scalar : Role.Ignore, Role.Entries => Role.Entry,
        Role.Entry => key == "value" ? Role.Scalar : Role.Ignore,
        Role.Error => key == "codexErrorInfo" ? Role.StopReason : Role.Ignore,
        Role.Questions => Role.Placeholder,
        Role.Items => Role.Item, Role.Item => key is "id" or "type" or "status" or "completed" or "requestId" or "isCompleted" ? Role.Scalar : key == "questions" ? Role.Questions : key is "content" or "input" ? Role.Replies : Role.Ignore,
        _ => Role.Ignore
    };
    private static JsonNode? Project(JsonElement value, Role role)
    {
        if (role == Role.Ignore || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (role == Role.Placeholder) return null;
        if (role == Role.StopReason) return JsonValue.Create(value.ValueKind == JsonValueKind.String ? value.GetString() :
            value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == 1 ? value.EnumerateObject().Single().Name : null);
        if (role == Role.Replies) return ReplyIds(value);
        if (role == Role.Questions) return value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= 1024 ? new JsonArray(value.EnumerateArray().Select(_ => (JsonNode?)null).ToArray()) : throw new InvalidOperationException();
        if (role == Role.Scalar)
        {
            if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) throw new InvalidOperationException();
            if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 512) throw new InvalidOperationException();
            return JsonNode.Parse(value.GetRawText());
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() > 65536) throw new InvalidOperationException();
            return new JsonArray(value.EnumerateArray().Select((v, i) => Project(v, Child(role, i.ToString()))).ToArray());
        }
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException();
        var result = new JsonObject();
        foreach (var p in value.EnumerateObject()) { var child = Child(role, p.Name); if (child != Role.Ignore) result[p.Name] = Project(p.Value, child); }
        if (role == Role.Params)
        {
            // These are the shipped desktop's fixed input tools. Inspect only
            // the schema discriminator, never keep arguments/question text.
            var tool = Text(value, "tool"); var arguments = Object(value, "arguments");
            var pending = tool switch
            {
                "request_environment_input" => Text(arguments, "mode") is "repositories" or "name" or "secrets" or "network" or "review",
                "request_option_picker" => Object(arguments, "question").ValueKind == JsonValueKind.String && Object(arguments, "options").ValueKind == JsonValueKind.Array,
                "request_onboarding_input" => Object(arguments, "questions") is { ValueKind: JsonValueKind.Array } q && q.GetArrayLength() is >= 1 and <= 3,
                "setup_codex_step" => Text(arguments, "step") is "role" or "task",
                _ => false
            };
            result["localInputPending"] = pending;
        }
        return result;
    }
    private static JsonArray ReplyIds(JsonElement input)
    {
        var empty = new JsonArray();
        if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() != 1 || Text(input[0], "type") != "text") return empty;
        var text = Text(input[0], "text").Trim(); const string open = "<send_user_message_question_reply>", close = "</send_user_message_question_reply>";
        if (!text.StartsWith(open, StringComparison.Ordinal) || !text.EndsWith(close, StringComparison.Ordinal)) return empty;
        try
        {
            using var document = JsonDocument.Parse(text[open.Length..^close.Length]); var root = document.RootElement;
            var values = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : new[] { root };
            if (values.Length is 0 or > 1024 || values.Any(x => Object(x, "questionItemId").ValueKind != JsonValueKind.String || Object(x, "question").ValueKind != JsonValueKind.String || Object(x, "answer").ValueKind != JsonValueKind.String)) return empty;
            return new JsonArray(values.Select(x => (JsonNode?)JsonValue.Create(Text(x, "questionItemId"))).ToArray());
        }
        catch (JsonException) { return empty; }
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static JsonElement Object(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) ? v : default;
    private static string Text(JsonElement e, string k) => Object(e, k) is { ValueKind: JsonValueKind.String } v ? v.GetString()! : "";
    private static long Number(JsonElement e, string k) => Object(e, k) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : -1;
    private static string String(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
}
