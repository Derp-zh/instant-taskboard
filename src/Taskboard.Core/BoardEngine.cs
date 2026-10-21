namespace Taskboard.Core;

public sealed partial class BoardEngine(BoardState state)
{
    private sealed record UndoEntry(WorkItem Before, WorkItem? After, int Index, bool Reorder);
    private readonly List<UndoEntry> undo = [];
    private sealed record RedoEntry(UndoEntry Entry, int AfterIndex);
    private readonly List<RedoEntry> redo = [];
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public BoardState State { get; } = state;
    private readonly Dictionary<Guid, bool> completedLanes = state.Tasks.ToDictionary(x => x.Id, x => x.ColumnId is null && x.Lane == Lane.Done);
    public event Action<IReadOnlyCollection<Guid>>? Changed;
    public event Action? Dirty;

    public EditSession NewSession(Lane lane, bool inline = false)
    {
        var working = new WorkItem { Lane = lane };
        return new EditSession { Working = working, Original = Copies.Of(working), Inline = inline, CreatedUtc = DateTime.UtcNow };
    }

    public EditSession Edit(Guid id)
    {
        var existing = State.Sessions.FirstOrDefault(x => x.TaskId == id);
        if (existing is not null) { existing.Visible = true; return existing; }
        var item = State.Tasks.First(x => x.Id == id);
        if (item.TimerUnread) { item.TimerUnread = false; Notify([id]); }
        var s = new EditSession { TaskId = id, Original = Copies.Of(item), Working = Copies.Of(item), CreatedUtc = DateTime.UtcNow };
        State.Sessions.Add(s); Touch(); return s;
    }

    public void StageSession(EditSession session)
    {
        if (session.Working.HasContent)
        {
            if (!State.Sessions.Contains(session)) State.Sessions.Add(session);
        }
        else if (session.TaskId is null) State.Sessions.Remove(session);
        Touch();
    }

    public void Discard(EditSession session)
    {
        if (session.TaskId is not null) State.Sessions.Remove(session);
        else { session.Visible = false; StageSession(session); }
        Touch();
    }

    public WorkItem Submit(EditSession session, bool recordUndo = true)
    {
        var edited = session.Working;
        if (!edited.HasContent) throw new InvalidOperationException("请先填写名称或任务详情。");
        if (!session.StatusSelected) throw new InvalidOperationException("请先选择当前状态，再保存任务。");
        if (!edited.Stages.Any(x => x.IsRunning == true)) throw new InvalidOperationException(LastRunningStageMessage);
        WorkItem result;
        if (session.TaskId is Guid id)
        {
            var live = State.Tasks.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("任务已被删除，不能继续提交。");
            result = Merge(session.Original, edited, live);
            var index = State.Tasks.IndexOf(live);
            if (recordUndo && !Copies.Same(live, result)) Remember(new(Copies.Of(live), Copies.Of(result), index, live.Lane != result.Lane || live.ColumnId != result.ColumnId));
            State.Tasks[index] = result;
            if (live.Lane != result.Lane || live.ColumnId != result.ColumnId) { State.Tasks.Remove(result); State.Tasks.Add(result); }
        }
        else
        {
            result = Copies.Of(edited);
            var existing = State.Tasks.FindIndex(x => x.Id == result.Id);
            if (existing >= 0) State.Tasks[existing] = result; // retry after a failed disk write remains idempotent
            else State.Tasks.Add(result);
            redo.Clear();
        }
        CleanReferences(result);
        result.TimerUnread = false;
        result.UpdatedUtc = DateTime.UtcNow;
        State.Sessions.Remove(session); Notify([result.Id]); return result;
    }

    public static WorkItem Merge(WorkItem original, WorkItem edited, WorkItem live)
    {
        var result = Copies.Of(live);
        if (original.Title != edited.Title) result.Title = edited.Title;
        if (original.Lane != edited.Lane || original.ColumnId != edited.ColumnId) { result.Lane = edited.Lane; result.ColumnId = edited.ColumnId; }
        result.Stages = edited.Stages.Select(stage => MergeStage(original.Stages.FirstOrDefault(x => x.Id == stage.Id), stage,
            live.Stages.FirstOrDefault(x => x.Id == stage.Id))).ToList();
        if (original.CurrentStageId != edited.CurrentStageId) result.CurrentStageId = edited.CurrentStageId;
        if (!Copies.Same(original.AiIds, edited.AiIds)) result.AiIds = [.. edited.AiIds];
        if (original.PriorityId != edited.PriorityId) result.PriorityId = edited.PriorityId;
        // Occurrence bookkeeping and pending reasons belong to the background, not editor buffers.
        if (original.Reminder.Enabled != edited.Reminder.Enabled || original.Reminder.Repeat != edited.Reminder.Repeat ||
            original.Reminder.Time != edited.Reminder.Time || original.Reminder.Weekday != edited.Reminder.Weekday || original.Reminder.NextLocal != edited.Reminder.NextLocal)
        {
            result.Reminder = Copies.Of(edited.Reminder);
            result.Reminder.LastOccurrence = live.Reminder.LastOccurrence;
        }
        // Independent editor/list changes can each keep one stage yet merge to none.
        if (!result.Stages.Any(x => x.IsRunning == true) && result.Stages.Count > 0)
            (result.Stages.FirstOrDefault(x => edited.Stages.Any(s => s.Id == x.Id && s.IsRunning == true)) ?? result.Stages[0]).IsRunning = true;
        return result;
    }
    private static Stage MergeStage(Stage? original, Stage edited, Stage? live)
    {
        if (original is null || live is null)
        {
            var added = Copies.Of(edited);
            if (added.Codex is { } binding) ResetBinding(binding);
            return added;
        }
        var result = Copies.Of(live);
        if (original.Name != edited.Name) result.Name = edited.Name;
        if (original.Text != edited.Text) result.Text = edited.Text;
        if (original.Number != edited.Number) result.Number = edited.Number;
        if (original.IsRunning != edited.IsRunning) result.IsRunning = edited.IsRunning;
        if (original.Codex?.ThreadId != edited.Codex?.ThreadId)
            result.Codex = edited.Codex is null ? null : new CodexBinding { ThreadId = edited.Codex.ThreadId, ThreadName = edited.Codex.ThreadName,
                Generation = Math.Max(live.Codex?.Generation ?? 0, original.Codex?.Generation ?? 0) + 1,
                PreviousThreads = live.Codex is null ? [] : live.Codex.PreviousThreads.Append(live.Codex.ThreadId).TakeLast(20).ToList() };
        if (result.Codex is not null && edited.Codex is not null && original.Codex?.ThreadId == edited.Codex.ThreadId && original.Codex?.ThreadName != edited.Codex.ThreadName)
            result.Codex.ThreadName = edited.Codex.ThreadName;
        return result;
    }

    public Stage AddStage(EditSession session)
    {
        var number = session.Working.Stages.Max(x => x.Number) + 1;
        var stage = new Stage { Number = number, Name = "阶段" + number, IsRunning = false };
        session.Working.Stages.Add(stage); StageSession(session); return stage;
    }

    public const string LastStageMessage = "至少保留一个阶段";
    public bool RemoveStage(EditSession session, Guid stageId, out string? message)
    {
        var item = session.Working;
        var stage = item.Stages.FirstOrDefault(x => x.Id == stageId);
        message = null;
        if (stage is null) return false;
        if (item.Stages.Count <= 1) { message = LastStageMessage; return false; }
        if (stage.IsRunning == true && !CanSetStageRunning(item, stageId, false))
        { message = LastRunningStageMessage; return false; }
        item.Stages.Remove(stage);
        if (item.CurrentStageId == stageId) item.CurrentStageId = item.Stages.First(x => x.IsRunning == true).Id;
        StageSession(session); return true;
    }

    public static IReadOnlyList<Stage> CardStages(WorkItem item)
    {
        if (item.Stages.Count == 1) return item.Stages;
        return item.Stages.Where(x => x.IsRunning == true).ToArray();
    }
    public const string LastRunningStageMessage = "至少保留一个运行中的阶段";
    public static bool CanSetStageRunning(WorkItem item, Guid stageId, bool running) =>
        running || item.Stages.Any(x => x.Id != stageId && x.IsRunning == true);
    public bool SetStageRunning(Guid taskId, Guid stageId, bool running)
    {
        var item = State.Tasks.First(x => x.Id == taskId); var stage = item.Stages.First(x => x.Id == stageId);
        if (stage.IsRunning == running) return true;
        if (!CanSetStageRunning(item, stageId, running)) return false;
        var before = Copies.Of(item); stage.IsRunning = running; item.TimerUnread = false; item.UpdatedUtc = DateTime.UtcNow;
        Remember(new(before, Copies.Of(item), State.Tasks.IndexOf(item), false)); Notify([item.Id]);
        return true;
    }

    public IEnumerable<WorkItem> Search(string text) => string.IsNullOrWhiteSpace(text) ? [] :
        State.Tasks.Where(x => x.Title.Contains(text, StringComparison.OrdinalIgnoreCase) || x.Stages.Any(s => s.Text.Contains(text, StringComparison.OrdinalIgnoreCase)));

    public void Move(Guid id, Lane lane, Guid? beforeId, Guid? columnId = null)
    {
        var item = State.Tasks.First(x => x.Id == id);
        if (beforeId == id) return;
        var index = State.Tasks.IndexOf(item);
        var original = Copies.Of(item);
        State.Tasks.Remove(item);
        item.Lane = lane;
        item.ColumnId = columnId; item.TimerUnread = false;
        var target = beforeId is Guid before ? State.Tasks.FindIndex(x => x.Id == before && x.Lane == lane && x.ColumnId == columnId) : -1;
        if (target < 0)
        {
            var last = State.Tasks.FindLastIndex(x => x.Lane == lane && x.ColumnId == columnId);
            target = last < 0 ? State.Tasks.Count : last + 1;
        }
        State.Tasks.Insert(target, item);
        if (index == target && original.Lane == lane && original.ColumnId == columnId && !original.TimerUnread) return;
        Remember(new(original, Copies.Of(item), index, true));
        item.UpdatedUtc = DateTime.UtcNow; Notify([id]);
    }

    public void Delete(Guid id)
    {
        var item = State.Tasks.First(x => x.Id == id);
        Remember(new(Copies.Of(item), null, State.Tasks.IndexOf(item), true));
        State.Tasks.Remove(item); State.Sessions.RemoveAll(x => x.TaskId == id); Notify([id]);
    }

    public void Undo()
    {
        if (undo.Count == 0) return;
        var entry = undo[^1]; undo.RemoveAt(undo.Count - 1);
        var live = State.Tasks.FirstOrDefault(x => x.Id == entry.Before.Id);
        redo.Add(new(entry, live is null ? -1 : State.Tasks.IndexOf(live))); if (redo.Count > 20) redo.RemoveAt(0);
        if (entry.After is null)
        {
            if (live is null)
            {
                var restored = Copies.Of(entry.Before); ResetBindings(restored);
                State.Tasks.Insert(Math.Min(entry.Index, State.Tasks.Count), restored);
            }
        }
        else if (live is not null)
        {
            var restored = Merge(entry.After, entry.Before, live);
            if (live.Lane != entry.After.Lane || live.ColumnId != entry.After.ColumnId) { restored.Lane = live.Lane; restored.ColumnId = live.ColumnId; }
            CleanReferences(restored);
            restored.UpdatedUtc = DateTime.UtcNow;
            var index = State.Tasks.IndexOf(live);
            State.Tasks[index] = restored;
            if (entry.Reorder)
            {
                State.Tasks.Remove(restored);
                State.Tasks.Insert(Math.Min(entry.Index, State.Tasks.Count), restored);
            }
        }
        foreach (var column in State.Preferences.Columns.Where(x => x.Deleted && State.Tasks.Any(x.Contains))) { column.Deleted = false; column.Visible = true; }
        Notify([entry.Before.Id]);
    }

    private void Remember(UndoEntry entry)
    {
        redo.Clear(); undo.Add(entry); if (undo.Count > 20) undo.RemoveAt(0);
    }

    public void DeleteOption(Guid id, bool ai)
    {
        var options = ai ? State.AiOptions : State.PriorityOptions;
        options.RemoveAll(x => x.Id == id);
        foreach (var task in State.Tasks) CleanReferences(task);
        foreach (var archive in State.Archives) CleanReferences(archive.Item);
        foreach (var session in State.Sessions) { CleanReferences(session.Original); CleanReferences(session.Working); }
        foreach (var entry in undo) { CleanReferences(entry.Before); if (entry.After is not null) CleanReferences(entry.After); }
        foreach (var recovery in redo) { CleanReferences(recovery.Entry.Before); if (recovery.Entry.After is not null) CleanReferences(recovery.Entry.After); }
        Notify(State.Tasks.Select(x => x.Id).ToArray());
    }

    private void CleanReferences(WorkItem item)
    {
        item.AiIds.RemoveAll(id => !State.AiOptions.Any(x => x.Id == id));
        if (item.PriorityId is Guid priority && !State.PriorityOptions.Any(x => x.Id == priority)) item.PriorityId = null;
    }
    private static void ResetBindings(WorkItem item)
    { foreach (var binding in item.Stages.Select(x => x.Codex).OfType<CodexBinding>()) ResetBinding(binding); }
    private static void ResetBinding(CodexBinding binding)
    {
        binding.BindingId = Guid.NewGuid(); binding.Generation++; binding.Phase = CodexPhase.Unknown; binding.AwaitingUserAction = false;
        binding.SourceEpoch = ""; binding.TurnId = null; binding.PendingRequests.Clear(); binding.LastSequence = -1;
        binding.PendingApprovals.Clear(); binding.PendingRequestOverflow = false;
    }

    public void Touch() => Dirty?.Invoke();
    public void Notify(IReadOnlyCollection<Guid> ids)
    {
        foreach (var id in ids)
        {
            var item = State.Tasks.FirstOrDefault(x => x.Id == id);
            if (item is null) { completedLanes.Remove(id); continue; }
            var completed = item.ColumnId is null && item.Lane == Lane.Done;
            if (!completed) item.CompletedSinceUtc = null;
            else if (!completedLanes.GetValueOrDefault(id) || item.CompletedSinceUtc is null) item.CompletedSinceUtc = DateTime.UtcNow;
            completedLanes[id] = completed;
        }
        Dirty?.Invoke(); Changed?.Invoke(ids);
    }
}
