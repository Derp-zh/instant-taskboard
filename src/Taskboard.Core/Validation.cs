using System.Text.RegularExpressions;

namespace Taskboard.Core;

public static partial class Validation
{
    public static void Check(BoardState state)
    {
        Require(state is not null && state.Version is 1 or 2, "不支持的数据版本");
        Require(state!.Tasks is not null && state.Sessions is not null && state.AiOptions is not null && state.PriorityOptions is not null && state.Preferences is not null, "数据缺少必要字段");
        CheckOptions(state.AiOptions!); CheckOptions(state.PriorityOptions!);
        var ai = state.AiOptions!.Select(x => x.Id).ToHashSet();
        var priority = state.PriorityOptions!.Select(x => x.Id).ToHashSet();
        Unique(state.Tasks!, x => x.Id); Unique(state.Sessions!, x => x.Id);
        foreach (var item in state.Tasks!) { CheckItem(item, ai, priority); Require(item.HasContent, "正式任务没有有效内容"); }
        var ids = state.Tasks!.Select(x => x.Id).ToHashSet();
        Require(state.Archives is not null, "归档集合无效");
        Require(state.Archives!.All(x => x is not null && x.Item is not null && x.OriginalColumn is not null), "归档记录无效");
        Require(state.Archives.Select(x => x.Item.Id).Distinct().Count() == state.Archives.Count, "归档任务重复");
        foreach (var archive in state.Archives)
        {
            CheckItem(archive.Item, ai, priority);
            Require(archive.Item.HasContent && !ids.Contains(archive.Item.Id) && archive.Item.ColumnId is null && archive.Item.Lane is Lane.Paused or Lane.Done && archive.OriginalColumn.BuiltinLane == archive.Item.Lane && archive.OriginalColumn.Id != Guid.Empty && !string.IsNullOrWhiteSpace(archive.OriginalColumn.Name) && archive.OriginalIndex >= 0 && archive.ArchivedUtc != default, "归档任务身份或来源无效");
        }
        var editing = new HashSet<Guid>();
        var workingIds = new HashSet<Guid>();
        foreach (var s in state.Sessions!)
        {
            Require(s is not null && s.Original is not null && s.Working is not null, "编辑会话为空");
            CheckItem(s!.Original, ai, priority); CheckItem(s.Working, ai, priority);
            Require(s.TaskId is not null || s.Working.HasContent, "不允许持久保存空草稿");
            Require(s.Working.Id == s.Original.Id, "草稿编号不一致");
            Require(workingIds.Add(s.Working.Id), "草稿任务编号重复");
            if (s.TaskId is Guid id) Require(ids.Contains(id) && editing.Add(id) && id == s.Working.Id && !s.Inline, "编辑任务引用无效或重复");
            else Require(!ids.Contains(s.Working.Id), "草稿与正式任务重复");
        }
        var p = state.Preferences!;
        Require(p.AutoArchiveAfterDays is >= 1 and <= 36500, "自动归档天数应为1至36500的整数");
        Require(p.Hotkey is not null && p.Hotkey.IsValid, "面板快捷键无效");
        NotificationSoundRules.Check(p.Listening);
        Require(p.Columns is not null, "列表配置为空"); Unique(p.Columns!, x => x.Id);
        Require(p.Columns!.All(x => !string.IsNullOrWhiteSpace(x.Name) && (x.BuiltinLane is null || Enum.IsDefined(x.BuiltinLane.Value)) && (!x.Protected || !x.Deleted)), "列表名称或角色无效");
        Require(p.Columns.Where(x => x.BuiltinLane is not null).Select(x => x.BuiltinLane).Distinct().Count() == p.Columns.Count(x => x.BuiltinLane is not null), "列表角色重复");
        Require(new[] { Lane.Inspiration,Lane.Running,Lane.Action }.All(lane => p.Columns.Any(x => x.BuiltinLane == lane && !x.Deleted)), "缺少受保护列表");
        foreach (var item in state.Tasks.Concat(state.Sessions.SelectMany(x => x.TaskId is null ? (x.StatusSelected ? new[] { x.Working } : Array.Empty<WorkItem>()) : new[] { x.Original,x.Working })))
            Require(p.Columns.Any(x => !x.Deleted && x.Contains(item)), "任务列表引用无效");
        Require(double.IsFinite(p.GlassTransparency) && p.GlassTransparency >= 0 && p.GlassTransparency <= 100, "毛玻璃透明度应为0至100%");
        Require(double.IsFinite(p.BallTransparency) && p.BallTransparency >= 0 && p.BallTransparency <= 100, "悬浮球透明度应为0至100%");
        Require(double.IsFinite(p.FloatingTransparency) && p.FloatingTransparency >= 0 && p.FloatingTransparency <= 100, "悬浮窗口透明度应为0至100%");
        Require(p.Positions is not null && p.ConnectionConfigs is not null && p.MachineMarkers is not null, "设置缺少字段");
        foreach (var (key, pos) in p.Positions!)
            Require(!string.IsNullOrWhiteSpace(key) && pos is not null && double.IsFinite(pos.X) && double.IsFinite(pos.Y) && double.IsFinite(pos.Width) && double.IsFinite(pos.Height) && pos.Width >= 1 && pos.Height >= 1, "窗口位置尺寸无效");
        Require(p.ConnectionConfigs!.All(x => x.Value is not null) && p.MachineMarkers!.All(x => x.Value is not null), "设置中存在空值");
    }

    private static void CheckItem(WorkItem item, HashSet<Guid> ai, HashSet<Guid> priority)
    {
        Require(item is not null && item.Id != Guid.Empty && item.Title is not null && Enum.IsDefined(item.Lane), "任务基础字段无效");
        Require(item.CompletedSinceUtc is null || item.CompletedSinceUtc != default(DateTime), "任务完成时间无效");
        Require(item!.Stages is not null && item.Stages.Count > 0 && item.AiIds is not null && item.Reminder is not null && item.PendingReasons is not null, "任务缺少必要字段");
        Unique(item.Stages!, x => x.Id);
        Require(item.Stages!.All(x => x.Number >= 0 && x.Name is not null && x.Text is not null) && item.Stages.Select(x => x.Number).Distinct().Count() == item.Stages.Count, "阶段字段无效");
        Require(item.Stages.Any(x => x.IsRunning == true), BoardEngine.LastRunningStageMessage);
        Require(item.Stages.Any(x => x.Number == 0) && item.Stages.Any(x => x.Id == item.CurrentStageId), "当前阶段指针无效");
        Require(item.Stages.Select(x => x.Number).SequenceEqual(item.Stages.Select(x => x.Number).Order()), "阶段顺序无效");
        Require(item.AiIds!.All(ai.Contains) && item.AiIds.Distinct().Count() == item.AiIds.Count && (item.PriorityId is null || priority.Contains(item.PriorityId.Value)), "字段选项引用无效");
        Require(item.PendingReasons!.All(x => x is not null), "待处理原因为空");
        var r = item.Reminder!;
        foreach (var c in item.Stages.Select(x => x.Codex).Append(item.Codex).OfType<CodexBinding>())
            Require(c.BindingId != Guid.Empty && !string.IsNullOrWhiteSpace(c.ThreadId) && c.Generation > 0 && Enum.IsDefined(c.Phase) &&
                c.PendingRequests is { Count: <= 128 } && c.PendingRequests.All(x => !string.IsNullOrEmpty(x) && x.Length <= 256) &&
                c.PendingApprovals is { Count: <= 128 } && c.PendingApprovals.All(x => c.PendingRequests!.Contains(x)) && c.PreviousThreads is not null, "阶段Codex关联字段无效");
        Require(Enum.IsDefined(r.Repeat) && Enum.IsDefined(r.Weekday) && r.Time >= TimeSpan.Zero && r.Time < TimeSpan.FromDays(1) && r.NextLocal != default, "提醒字段无效");
    }

    private static void CheckOptions(List<FieldOption> options)
    {
        Unique(options, x => x.Id);
        Require(options.All(x => !string.IsNullOrWhiteSpace(x.Name) && x.Color is not null && ColorPattern().IsMatch(x.Color)), "选项名称或颜色无效");
    }
    private static void Unique<T>(List<T> list, Func<T, Guid> id) where T : class
    {
        Require(list.All(x => x is not null), "集合包含空对象");
        Require(list.All(x => id(x) != Guid.Empty) && list.Select(id).Distinct().Count() == list.Count, "编号为空或重复");
    }
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}
