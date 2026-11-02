namespace Taskboard.Core;

public sealed partial class BoardEngine
{
    // A list action changes exactly one binding, without submitting an open editor.
    public bool BindStageConversation(Guid taskId, Guid stageId, Guid? expectedBinding, string threadId, string name)
    {
        if (!Guid.TryParseExact(threadId, "D", out var parsed) || parsed == Guid.Empty) return false;
        threadId = parsed.ToString("D");
        var item = State.Tasks.FirstOrDefault(x => x.Id == taskId);
        var stage = item?.Stages.FirstOrDefault(x => x.Id == stageId);
        if (item is null || stage is null || stage.Codex?.BindingId != expectedBinding) return false;
        var sameThread = string.Equals(stage.Codex?.ThreadId, threadId, StringComparison.OrdinalIgnoreCase);
        if (sameThread && stage.Codex!.ThreadName == name) return true;
        var before = Copies.Of(item);
        if (sameThread) stage.Codex!.ThreadName = name;
        else stage.Codex = new CodexBinding
        {
            ThreadId = threadId, ThreadName = name, Generation = (stage.Codex?.Generation ?? 0) + 1,
            PreviousThreads = stage.Codex is null ? [] : stage.Codex.PreviousThreads.Append(stage.Codex.ThreadId).TakeLast(20).ToList()
        };
        item.UpdatedUtc = DateTime.UtcNow;
        Remember(new(before, Copies.Of(item), State.Tasks.IndexOf(item), false));
        Notify([item.Id]); return true;
    }
}
