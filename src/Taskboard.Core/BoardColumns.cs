namespace Taskboard.Core;

public sealed class BoardColumn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新列表";
    public Lane? BuiltinLane { get; set; }
    public bool Visible { get; set; } = true;
    // Retain the identity so an undo in this run can safely restore its list.
    public bool Deleted { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool Protected => BuiltinLane is Lane.Inspiration or Lane.Running or Lane.Action;
    [System.Text.Json.Serialization.JsonIgnore] public string PositionKey => BuiltinLane is Lane lane ? "lane-"+(int)lane : "column-"+Id.ToString("N");
    public bool Contains(WorkItem item) => BuiltinLane is Lane lane ? item.ColumnId is null && item.Lane == lane : item.ColumnId == Id;
    public void Assign(WorkItem item) { item.ColumnId = BuiltinLane is null ? Id : null; item.Lane = BuiltinLane ?? Lane.Refining; }
    public override string ToString() => Name;
    public static List<BoardColumn> Defaults() => Enum.GetValues<Lane>().Select(lane => new BoardColumn {
        Id = Guid.Parse($"89073cbd-0185-4aaf-81b1-{(int)lane+1:000000000000}"), BuiltinLane = lane, Name = Names.Lanes[(int)lane] }).ToList();
}

public static class BoardColumns
{
    public static BoardColumn For(Preferences preferences, WorkItem item) => preferences.Columns.First(x => x.Contains(item));
    public static bool IsEmpty(BoardState state, BoardColumn column) => !state.Tasks.Any(column.Contains) &&
        !state.Sessions.Any(s => (s.Working.HasContent && column.Contains(s.Working)) || (s.TaskId is not null && column.Contains(s.Original)));
    public static void Delete(BoardState state, BoardColumn column)
    {
        if (column.Protected) throw new InvalidOperationException("灵感库、进行中和待操作可改名或隐藏，不能删除。");
        if (!IsEmpty(state,column)) throw new InvalidOperationException("列表中还有任务或非空草稿，请先清空或移走。");
        column.Deleted = true; column.Visible = false;
    }
    public static void SetVisibleCount(Preferences preferences, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var active = preferences.Columns.Where(x => !x.Deleted).ToList();
        while (active.Count < count) { var added = new BoardColumn { Name = "列表"+(active.Count+1) }; preferences.Columns.Add(added); active.Add(added); }
        // Keep existing visible lists first, then reveal hidden ones in configured order.
        var order = active.Where(x => x.Visible).Concat(active.Where(x => !x.Visible)).ToArray();
        for (var i=0;i<order.Length;i++) order[i].Visible = i<count;
    }
}
