using System.Text.Json;

namespace Taskboard.Core;

public enum CodexPhase { Unknown, Running, WaitingForInput, WaitingForApproval, ReadyForReview, Failed, Interrupted }
public sealed class CodexBinding
{
    public Guid BindingId { get; set; } = Guid.NewGuid();
    public string ThreadId { get; set; } = "";
    public string ThreadName { get; set; } = "";
    public int Generation { get; set; } = 1;
    public CodexPhase Phase { get; set; }
    // Confirmed waiting survives an unavailable connection. An untrusted history
    // phase alone cannot set this latch; a new confirmed execution clears it.
    public bool AwaitingUserAction { get; set; }
    public string? TurnId { get; set; }
    public string SourceEpoch { get; set; } = "";
    public long LastSequence { get; set; } = -1;
    public List<string> PendingRequests { get; set; } = [];
    public List<string> PendingApprovals { get; set; } = [];
    // A saturated request set stays protected until an accurate new turn or terminal.
    public bool PendingRequestOverflow { get; set; }
    public List<string> PreviousThreads { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public string Caption => Phase switch
    {
        CodexPhase.Running => "运行中", CodexPhase.WaitingForInput => "等待回答", CodexPhase.WaitingForApproval => "等待批准",
        CodexPhase.ReadyForReview => "结果待检查", CodexPhase.Failed => "执行失败", CodexPhase.Interrupted => "已中断", _ => "状态待确认"
    };
}

// Port of tools/codex_probe/state.py. Historical data never changes a lane.
public static class CodexState
{
    private const int MaxPendingRequests = 128, MaxRequestIdLength = 256;
    public static bool NeedsUserAction(CodexPhase phase) => phase is CodexPhase.WaitingForInput or CodexPhase.WaitingForApproval or CodexPhase.ReadyForReview or CodexPhase.Failed or CodexPhase.Interrupted;
    public static string Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    public static JsonElement Object(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    public static bool Apply(WorkItem task, JsonElement message, long sequence, int generation, string epoch, bool authoritative)
    {
        var parameters = Object(message, "params"); var threadId = Text(parameters, "threadId");
        if (threadId.Length == 0) threadId = Text(Object(parameters, "thread"), "id");
        var changed = false;
        foreach (var stage in task.Stages.Where(x => x.Codex?.ThreadId == threadId))
            changed |= Apply(task, stage.Id, message, sequence, generation, epoch, authoritative);
        return changed;
    }
    public static bool Apply(WorkItem task, Guid stageId, JsonElement message, long sequence, int generation, string epoch, bool authoritative)
    {
        var binding = task.Stages.FirstOrDefault(x => x.Id == stageId)?.Codex;
        if (binding is null || !authoritative || generation != binding.Generation) return false;
        var parameters = Object(message, "params"); var thread = Text(parameters, "threadId");
        if (thread.Length == 0) thread = Text(Object(parameters, "thread"), "id");
        if (thread != binding.ThreadId) return false;
        var method = Text(message, "method"); var turn = Object(parameters, "turn"); var turnId = Text(parameters, "turnId");
        if (turnId.Length == 0) turnId = Text(turn, "id");
        // Internal desktop reconciliation only: the verified source has already
        // established a newest-turn time watermark, then observed a newer stop.
        // This is deliberately not a fabricated execution-start/resume event.
        if (method == "probe/currentTurnStopped" &&
            (binding.SourceEpoch.Length == 0 || binding.SourceEpoch != epoch || string.IsNullOrEmpty(binding.TurnId) ||
             turnId.Length == 0 || turnId != Text(turn, "id") || turnId == binding.TurnId || Text(turn, "status") is not ("failed" or "interrupted") ||
             !UnixSecond(Object(turn, "startedAt"), out var startedAt) || !UnixSecond(Object(parameters, "baselineStartedAt"), out var baseline) || startedAt <= baseline)) return false;
        // A terminal needs an observed turn, not a persisted first-start history.
        // The observer can reconcile that same known turn immediately after reconnect.
        if (method is "turn/completed" or "error" && (turnId.Length == 0 || binding.SourceEpoch.Length == 0 || binding.TurnId != turnId)) return false;
        if (method == "turn/completed" && Text(turn, "status") is not ("completed" or "failed" or "interrupted")) return false;
        if (method == "turn/started" && turnId.Length == 0) return false;
        if (method == "turn/started" && turnId == binding.TurnId && binding.SourceEpoch.Length > 0) return false;
        var request = method is "item/tool/requestUserInput" or "tool/requestUserInput" or "mcpServer/elicitation/request" or
            "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" or "item/permissions/requestApproval";
        var idElement = Object(message, "id"); var requestId = idElement.ToString();
        if (request && (turnId.Length == 0 || idElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) || requestId.Length == 0 || requestId.Length > MaxRequestIdLength)) return false;
        var before = (binding.Phase, binding.AwaitingUserAction, task.Lane, task.TimerUnread, binding.TurnId, binding.PendingRequests.Count, binding.PendingApprovals.Count, binding.PendingRequestOverflow, binding.SourceEpoch);
        var previousRequests = binding.PendingRequests.ToArray(); var previousApprovals = binding.PendingApprovals.ToArray();
        var knownWait = binding.SourceEpoch.Length > 0 && NeedsUserAction(binding.Phase);
        var previousTurn = binding.TurnId;
        if (binding.SourceEpoch != epoch)
        { binding.SourceEpoch = epoch; binding.LastSequence = -1; binding.TurnId = null; binding.Phase = CodexPhase.Unknown; }
        if (sequence <= binding.LastSequence) return false;
        if (method is not ("turn/started" or "probe/currentTurnStopped") && binding.TurnId is not null && turnId.Length > 0 && turnId != binding.TurnId) return false;
        if (method == "turn/started" && turnId.Length > 0 && turnId == binding.TurnId) return false;
        binding.LastSequence = sequence;
        binding.AwaitingUserAction |= knownWait;
        // A resolved request or idle snapshot is not evidence that execution resumed.
        // Only a message which actually supplies a current actionable state may move the card.
        var currentState = false;
        switch (method)
        {
            case "probe/disconnected": binding.Phase = CodexPhase.Unknown; break;
            case "turn/started":
                binding.TurnId = turnId; ClearRequests(binding);
                var initialStatus = Object(parameters, "status");
                ApplyPendingSnapshot(binding, initialStatus, turnId);
                binding.Phase = Text(initialStatus, "type") == "active" ? ActivePhase(binding, initialStatus) : CodexPhase.Running;
                currentState = true; break;
            case "probe/snapshot": case "thread/status/changed":
                var status = Object(parameters, "status");
                if (Text(status, "type") == "active")
                {
                    currentState = true;
                    if (turnId.Length > 0 && previousTurn is not null && previousTurn != turnId) ClearRequests(binding);
                    if (turnId.Length > 0) binding.TurnId = turnId;
                    // A complete, versioned read of the original desktop's
                    // request index may replace the pending set. Ordinary active
                    // flags or public resolved events cannot clear missing IDs.
                    if (method == "probe/snapshot") ApplyPendingSnapshot(binding, status, turnId);
                    binding.Phase = ActivePhase(binding, status);
                }
                else if (Text(status, "type") is "notLoaded" or "systemError") binding.Phase = CodexPhase.Unknown;
                break;
            case "item/tool/requestUserInput": case "tool/requestUserInput": case "mcpServer/elicitation/request":
                binding.TurnId = turnId; AddRequest(binding, requestId, false); binding.Phase = PendingPhase(binding); currentState = true; break;
            case "item/commandExecution/requestApproval": case "item/fileChange/requestApproval": case "item/permissions/requestApproval":
                binding.TurnId = turnId; AddRequest(binding, requestId, true); binding.Phase = PendingPhase(binding); currentState = true; break;
            case "serverRequest/resolved":
                var resolved = Object(parameters, "requestId").ToString();
                binding.PendingRequests.Remove(resolved); binding.PendingApprovals.Remove(resolved); break;
            case "turn/completed": case "probe/currentTurnStopped":
                currentState = true;
                binding.TurnId = turnId;
                ClearRequests(binding);
                binding.Phase = Text(turn, "status") switch { "completed" => CodexPhase.ReadyForReview, "failed" => CodexPhase.Failed, "interrupted" => CodexPhase.Interrupted, _ => CodexPhase.Unknown }; break;
            case "error":
                var retry = Object(parameters, "willRetry"); var reason = Object(Object(parameters, "error"), "codexErrorInfo");
                var code = reason.ValueKind == JsonValueKind.String ? reason.GetString() :
                    reason.ValueKind == JsonValueKind.Object && reason.EnumerateObject().Count() == 1 ? reason.EnumerateObject().First().Name : "";
                if (retry.ValueKind == JsonValueKind.False && code is "usageLimitExceeded" or "rateLimitExceeded")
                { binding.TurnId = turnId; ClearRequests(binding); binding.Phase = CodexPhase.Failed; currentState = true; }
                break;
        }
        // Polling an already running turn is observation, not resumption. In
        // particular a timer can put that running card in Action between polls.
        // A disconnect alone must not manufacture a wait/recovery transition.
        var executionResumed = currentState && binding.Phase == CodexPhase.Running &&
            (method == "turn/started" || knownWait || before.AwaitingUserAction);
        if (currentState && binding.Phase != CodexPhase.Unknown)
            binding.AwaitingUserAction = NeedsUserAction(binding.Phase);
        if (currentState && (binding.Phase == CodexPhase.Running || NeedsUserAction(binding.Phase)) &&
            task.Stages.Any(x => x.Id == stageId && x.IsRunning == true) && task.ColumnId is null && task.Lane is Lane.Running or Lane.Action)
        {
            var active = task.Stages.Where(x => x.IsRunning == true).Select(x => x.Codex).OfType<CodexBinding>().ToArray();
            if (active.Any(x => x.AwaitingUserAction || x.SourceEpoch.Length > 0 && NeedsUserAction(x.Phase))) task.Lane = Lane.Action;
            else if (active.Any(x => x.SourceEpoch == epoch && x.Phase == CodexPhase.Running))
            {
                if (task.Lane == Lane.Action && executionResumed) task.TimerUnread = false;
                task.Lane = Lane.Running;
            }
        }
        return before != (binding.Phase, binding.AwaitingUserAction, task.Lane, task.TimerUnread, binding.TurnId, binding.PendingRequests.Count, binding.PendingApprovals.Count, binding.PendingRequestOverflow, binding.SourceEpoch) ||
            !previousRequests.SequenceEqual(binding.PendingRequests) || !previousApprovals.SequenceEqual(binding.PendingApprovals);
    }
    private static CodexPhase PendingPhase(CodexBinding binding) => binding.PendingApprovals.Count > 0 ? CodexPhase.WaitingForApproval : CodexPhase.WaitingForInput;
    private static CodexPhase ActivePhase(CodexBinding binding, JsonElement status)
    {
        if (binding.PendingRequests.Count > 0 || binding.PendingRequestOverflow) return PendingPhase(binding);
        var element = Object(status, "activeFlags");
        if (element.ValueKind == JsonValueKind.Array && element.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)) return CodexPhase.Unknown;
        var flags = element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToArray() : [];
        return flags.Contains("waitingOnUserInput") ? CodexPhase.WaitingForInput : flags.Contains("waitingOnApproval") ? CodexPhase.WaitingForApproval :
            flags.Length > 0 ? CodexPhase.Unknown : CodexPhase.Running;
    }
    private static void ApplyPendingSnapshot(CodexBinding binding, JsonElement status, string turnId)
    {
        if (turnId.Length == 0 || binding.TurnId != turnId || Text(status, "pendingTurnId") != turnId ||
            Object(status, "pendingSourceVersion").ValueKind != JsonValueKind.Number ||
            !Object(status, "pendingSourceVersion").TryGetInt32(out var version) || version != 1) return;
        var requests = Object(status, "pendingRequests"); var overflow = Object(status, "pendingOverflow");
        if (requests.ValueKind != JsonValueKind.Array || requests.GetArrayLength() > MaxPendingRequests ||
            overflow.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;
        var values = requests.EnumerateArray().ToArray();
        if (values.Any(x => Text(x, "id").Length is 0 or > MaxRequestIdLength ||
            Object(x, "approval").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) ||
            values.Select(x => Text(x, "id")).Distinct(StringComparer.Ordinal).Count() != values.Length) return;
        binding.PendingRequests = values.Select(x => Text(x, "id")).ToList();
        binding.PendingApprovals = values.Where(x => Object(x, "approval").ValueKind == JsonValueKind.True).Select(x => Text(x, "id")).ToList();
        binding.PendingRequestOverflow = overflow.ValueKind == JsonValueKind.True;
    }
    private static bool UnixSecond(JsonElement element, out long value)
    {
        value = -1;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value) && value is >= 0 and <= 253402300799;
    }
    private static void AddRequest(CodexBinding binding, string id, bool approval)
    {
        if (!binding.PendingRequests.Contains(id))
        {
            if (binding.PendingRequests.Count >= MaxPendingRequests) { binding.PendingRequestOverflow = true; return; }
            binding.PendingRequests.Add(id);
        }
        if (approval && !binding.PendingApprovals.Contains(id)) binding.PendingApprovals.Add(id);
    }
    private static void ClearRequests(CodexBinding binding)
    { binding.PendingRequests.Clear(); binding.PendingApprovals.Clear(); binding.PendingRequestOverflow = false; }
}
