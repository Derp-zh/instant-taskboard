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
        var before = (binding.Phase, binding.AwaitingUserAction, task.Lane, task.TimerUnread, binding.TurnId, binding.PendingRequests.Count, binding.SourceEpoch);
        var knownWait = binding.SourceEpoch.Length > 0 && binding.Phase is (CodexPhase.WaitingForInput or CodexPhase.WaitingForApproval or CodexPhase.ReadyForReview);
        if (binding.SourceEpoch != epoch)
        { binding.SourceEpoch = epoch; binding.LastSequence = -1; binding.TurnId = null; binding.PendingRequests.Clear(); binding.Phase = CodexPhase.Unknown; }
        if (sequence <= binding.LastSequence) return false;
        var method = Text(message, "method"); var turn = Object(parameters, "turn"); var turnId = Text(parameters, "turnId");
        if (turnId.Length == 0) turnId = Text(turn, "id");
        if (method != "turn/started" && binding.TurnId is not null && turnId.Length > 0 && turnId != binding.TurnId) return false;
        if (method == "turn/started" && turnId.Length > 0 && turnId == binding.TurnId) return false;
        binding.LastSequence = sequence;
        binding.AwaitingUserAction |= knownWait;
        // A resolved request or idle snapshot is not evidence that execution resumed.
        // Only a message which actually supplies a current actionable state may move the card.
        var currentState = false;
        switch (method)
        {
            case "probe/disconnected": binding.Phase = CodexPhase.Unknown; break;
            case "turn/started": binding.TurnId = turnId; binding.PendingRequests.Clear(); binding.Phase = CodexPhase.Running; currentState = true; break;
            case "probe/snapshot": case "thread/status/changed":
                var status = Object(parameters, "status"); var flagsElement = Object(status, "activeFlags");
                var flags = flagsElement.ValueKind == JsonValueKind.Array ? flagsElement.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [];
                if (Text(status, "type") == "active")
                {
                    currentState = true;
                    binding.Phase = flags.Contains("waitingOnUserInput") ? CodexPhase.WaitingForInput : flags.Contains("waitingOnApproval") ? CodexPhase.WaitingForApproval :
                        flags.Length > 0 ? CodexPhase.Unknown : CodexPhase.Running;
                    if (flags.Length == 0) binding.PendingRequests.Clear();
                }
                else if (Text(status, "type") is "notLoaded" or "systemError") binding.Phase = CodexPhase.Unknown;
                break;
            case "item/tool/requestUserInput": case "tool/requestUserInput": case "mcpServer/elicitation/request":
                AddRequest(binding, message); binding.Phase = CodexPhase.WaitingForInput; currentState = true; break;
            case "item/commandExecution/requestApproval": case "item/fileChange/requestApproval": case "item/permissions/requestApproval":
                AddRequest(binding, message); binding.Phase = CodexPhase.WaitingForApproval; currentState = true; break;
            case "serverRequest/resolved": binding.PendingRequests.Remove(Object(parameters, "requestId").ToString()); break;
            case "turn/completed":
                currentState = true;
                if (turnId.Length > 0) binding.TurnId = turnId; binding.PendingRequests.Clear();
                binding.Phase = Text(turn, "status") switch { "completed" => CodexPhase.ReadyForReview, "failed" => CodexPhase.Failed, "interrupted" => CodexPhase.Interrupted, _ => CodexPhase.Unknown }; break;
        }
        if (currentState && binding.Phase != CodexPhase.Unknown)
            binding.AwaitingUserAction = binding.Phase is CodexPhase.WaitingForInput or CodexPhase.WaitingForApproval or CodexPhase.ReadyForReview;
        if (currentState && binding.Phase is (CodexPhase.Running or CodexPhase.WaitingForInput or CodexPhase.WaitingForApproval or CodexPhase.ReadyForReview) &&
            task.Stages.Any(x => x.Id == stageId && x.IsRunning == true) && task.ColumnId is null && task.Lane is Lane.Running or Lane.Action)
        {
            var active = task.Stages.Where(x => x.IsRunning == true).Select(x => x.Codex).OfType<CodexBinding>().ToArray();
            if (active.Any(x => x.AwaitingUserAction || x.SourceEpoch == epoch && x.Phase is (CodexPhase.WaitingForInput or CodexPhase.WaitingForApproval or CodexPhase.ReadyForReview))) task.Lane = Lane.Action;
            else if (active.Any(x => x.SourceEpoch == epoch && x.Phase == CodexPhase.Running))
            {
                if (task.Lane == Lane.Action && binding.Phase == CodexPhase.Running) task.TimerUnread = false;
                task.Lane = Lane.Running;
            }
        }
        return before != (binding.Phase, binding.AwaitingUserAction, task.Lane, task.TimerUnread, binding.TurnId, binding.PendingRequests.Count, binding.SourceEpoch);
    }
    private static void AddRequest(CodexBinding binding, JsonElement message)
    { var id = Object(message, "id").ToString(); if (!binding.PendingRequests.Contains(id)) binding.PendingRequests.Add(id); }
}
