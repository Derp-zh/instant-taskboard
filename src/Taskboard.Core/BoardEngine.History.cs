namespace Taskboard.Core;

public sealed partial class BoardEngine
{
    public void Redo()
    {
        if (redo.Count == 0) return;
        var recovery = redo[^1]; redo.RemoveAt(redo.Count - 1); var entry = recovery.Entry;
        var live = State.Tasks.FirstOrDefault(x => x.Id == entry.Before.Id);
        if (entry.After is null)
        { State.Tasks.RemoveAll(x => x.Id == entry.Before.Id); State.Sessions.RemoveAll(x => x.TaskId == entry.Before.Id); }
        else if (live is not null)
        {
            var restored = Merge(entry.Before, entry.After, live);
            if (live.Lane != entry.Before.Lane || live.ColumnId != entry.Before.ColumnId) { restored.Lane = live.Lane; restored.ColumnId = live.ColumnId; }
            CleanReferences(restored); restored.UpdatedUtc = DateTime.UtcNow;
            var index = State.Tasks.IndexOf(live); State.Tasks[index] = restored;
            if (entry.Reorder) { State.Tasks.Remove(restored); State.Tasks.Insert(Math.Clamp(recovery.AfterIndex, 0, State.Tasks.Count), restored); }
        }
        undo.Add(entry); if (undo.Count > 20) undo.RemoveAt(0);
        foreach (var column in State.Preferences.Columns.Where(x => x.Deleted && State.Tasks.Any(x.Contains))) { column.Deleted = false; column.Visible = true; }
        Notify([entry.Before.Id]);
    }
    public bool CanArchive(WorkItem item) => item.ColumnId is null && item.Lane is Lane.Paused or Lane.Done;
    public void Archive(Guid id)
    {
        var item = State.Tasks.First(x => x.Id == id);
        if (!CanArchive(item)) throw new InvalidOperationException("只有已完成或暂搁置列表的任务可以归档");
        State.Archives.Add(new ArchivedTask { Item = Copies.Of(item), OriginalColumn = Copies.Of(BoardColumns.For(State.Preferences, item)), OriginalIndex = State.Tasks.IndexOf(item) });
        TrimArchiveHistory();
        State.Tasks.Remove(item); State.Sessions.RemoveAll(x => x.TaskId == id);
        ForgetHistory(id); redo.Clear(); Notify([id]);
    }
    public WorkItem Unarchive(Guid id)
    {
        var archive = State.Archives.First(x => x.Item.Id == id); var item = Copies.Of(archive.Item);
        if (State.Tasks.Any(x => x.Id == id)) throw new InvalidOperationException("当前列表已存在同一任务，归档未修改");
        var column = State.Preferences.Columns.FirstOrDefault(x => x.BuiltinLane == archive.OriginalColumn.BuiltinLane);
        if (column is null) { column = Copies.Of(archive.OriginalColumn); State.Preferences.Columns.Add(column); }
        column.Deleted = false; column.Visible = true; column.Assign(item); CleanReferences(item);
        ResetBindings(item);
        State.Tasks.Insert(Math.Min(archive.OriginalIndex, State.Tasks.Count), item); State.Archives.Remove(archive);
        ForgetHistory(id); redo.Clear(); Notify([id]); return item;
    }
    private void ForgetHistory(Guid id)
    { undo.RemoveAll(x => x.Before.Id == id); redo.RemoveAll(x => x.Entry.Before.Id == id); }
    public int TrimArchiveHistory()
    {
        if (!State.Preferences.AutoDeleteArchives || State.Archives.Count <= 40) return 0;
        var keep = State.Archives.Select((x, i) => (Archive: x, Index: i)).OrderByDescending(x => x.Archive.ArchivedUtc).ThenByDescending(x => x.Index).Take(40).Select(x => x.Archive.Item.Id).ToHashSet();
        var removed = State.Archives.RemoveAll(x => !keep.Contains(x.Item.Id)); if (removed > 0) Touch(); return removed;
    }
    public void InitializeCompletionTimes(DateTime nowUtc)
    {
        var changed = false;
        foreach (var item in State.Tasks.Where(x => x.ColumnId is null && x.Lane == Lane.Done && x.CompletedSinceUtc is null))
        { item.CompletedSinceUtc = nowUtc; changed = true; }
        if (changed) Touch();
    }
    public Guid[] DueArchives(DateTime nowUtc)
    {
        InitializeCompletionTimes(nowUtc);
        if (!State.Preferences.AutoArchiveCompleted) return [];
        var days = State.Preferences.AutoArchiveAfterDays;
        if (days < 1 || days > 36500) throw new InvalidOperationException("自动归档天数无效");
        return State.Tasks.Where(x => x.ColumnId is null && x.Lane == Lane.Done && x.CompletedSinceUtc is DateTime since && nowUtc - since > TimeSpan.FromDays(days)
            && !State.Sessions.Any(s => s.TaskId == x.Id)).Select(x => x.Id).ToArray();
    }
}
