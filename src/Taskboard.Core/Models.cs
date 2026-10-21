using System.Text.Json;

namespace Taskboard.Core;

public enum Lane { Inspiration, Refining, Running, Action, Paused, Done }
public enum Repeat { Once, Daily, Weekly }

public static class Names
{
    public static readonly string[] Lanes = ["灵感库", "完善中", "进行中", "待操作", "暂搁置", "已完成"];
}

public sealed class Stage
{
    // Null only occurs in legacy data; WorkItem maps the former current stage once.
    public bool? IsRunning { get; set; }
    public CodexBinding? Codex { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    [System.Text.Json.Serialization.JsonRequired]
    public int Number { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public string Name { get; set; } = "阶段0";
    [System.Text.Json.Serialization.JsonRequired]
    public string Text { get; set; } = "";
}

public sealed class Reminder
{
    [System.Text.Json.Serialization.JsonRequired]
    public bool Enabled { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public Repeat Repeat { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public DateTime NextLocal { get; set; } = DateTime.Today.AddDays(1).AddHours(9);
    [System.Text.Json.Serialization.JsonRequired]
    public DayOfWeek Weekday { get; set; } = DayOfWeek.Monday;
    [System.Text.Json.Serialization.JsonRequired]
    public TimeSpan Time { get; set; } = TimeSpan.FromHours(9);
    [System.Text.Json.Serialization.JsonRequired]
    public DateTime? LastOccurrence { get; set; }
}

public sealed class WorkItem : System.Text.Json.Serialization.IJsonOnDeserialized
{
    public DateTime? CompletedSinceUtc { get; set; }
    public Guid? ColumnId { get; set; }
    // Read legacy task bindings once, then persist bindings on their owning stage.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CodexBinding? Codex { get; set; }
    public bool TimerUnread { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    [System.Text.Json.Serialization.JsonRequired]
    public string Title { get; set; } = "";
    [System.Text.Json.Serialization.JsonRequired]
    public Lane Lane { get; set; } = Lane.Refining;
    [System.Text.Json.Serialization.JsonRequired]
    public List<Stage> Stages { get; set; } = [new()];
    [System.Text.Json.Serialization.JsonRequired]
    // Legacy location retained for migration; display and automation use IsRunning.
    public Guid CurrentStageId { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public List<Guid> AiIds { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public Guid? PriorityId { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public Reminder Reminder { get; set; } = new();
    [System.Text.Json.Serialization.JsonRequired]
    public List<string> PendingReasons { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public WorkItem() { CurrentStageId = Stages[0].Id; Stages[0].IsRunning = true; }
    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
    {
        if (Stages is not null) foreach (var stage in Stages.Where(x => x is not null)) stage.IsRunning ??= stage.Id == CurrentStageId;
        if (Stages is { Count: > 0 } && !Stages.Any(x => x?.IsRunning == true) &&
            (Stages.FirstOrDefault(x => x?.Id == CurrentStageId) ?? Stages.FirstOrDefault(x => x is not null)) is { } fallback)
            fallback.IsRunning = true;
        if (Codex is null) return;
        var current = Stages?.FirstOrDefault(x => x?.Id == CurrentStageId);
        if (current is null) throw new JsonException("旧会话关联缺少对应的当前阶段");
        if (current.Codex is not null && !Copies.Same(current.Codex, Codex)) throw new JsonException("阶段与旧任务会话关联冲突");
        current.Codex = Codex; Codex = null;
    }
    [System.Text.Json.Serialization.JsonIgnore]
    // Compatibility accessor; new UI and observers must address stages individually.
    public CodexBinding? CurrentCodex => Stages.FirstOrDefault(x => x.Id == CurrentStageId)?.Codex;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasContent => !string.IsNullOrWhiteSpace(Title) || Stages.Any(x => !string.IsNullOrWhiteSpace(x.Text));
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title.Trim() :
        Stages.Where(x => Stages.Count == 1 || x.IsRunning == true).SelectMany(x => x.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "未命名任务";
}

public sealed class EditSession
{
    // Older sessions already have a list; the shared host's new entry starts unassigned.
    public bool StatusSelected { get; set; } = true;
    public DateTime? CreatedUtc { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime CreationTime => CreatedUtc ?? Original.UpdatedUtc;
    [System.Text.Json.Serialization.JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    [System.Text.Json.Serialization.JsonRequired]
    public Guid? TaskId { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public WorkItem Original { get; set; } = new();
    [System.Text.Json.Serialization.JsonRequired]
    public WorkItem Working { get; set; } = new();
    [System.Text.Json.Serialization.JsonRequired]
    public bool Visible { get; set; } = true;
    [System.Text.Json.Serialization.JsonRequired]
    public bool Inline { get; set; }
}

public sealed class FieldOption
{
    [System.Text.Json.Serialization.JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    [System.Text.Json.Serialization.JsonRequired]
    public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonRequired]
    public string Color { get; set; } = "#8B9BB4";
}

public sealed class WindowPosition
{
    [System.Text.Json.Serialization.JsonRequired]
    public double X { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public double Y { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public double Width { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public double Height { get; set; }
}

public sealed class Preferences : System.Text.Json.Serialization.IJsonOnDeserialized
{
    public bool AutoArchiveCompleted { get; set; } = true;
    public int AutoArchiveAfterDays { get; set; } = 5;
    public bool AutoDeleteArchives { get; set; }
    public bool InspirationQuickEntry { get; set; } = true;
    public bool BallEdgeAutoHide { get; set; } = true;
    public BoardHotkey Hotkey { get; set; } = new();
    public NotificationListeningOptions Listening { get; set; } = new();
    public List<BoardColumn> Columns { get; set; } = BoardColumn.Defaults();
    // Optional for backwards compatibility with 0.3/0.4 data files.
    public double GlassTransparency { get; set; } = 20;
    public double FloatingTransparency { get; set; } = 10;
    public double BallTransparency { get; set; } = 40;
    [System.Text.Json.Serialization.JsonRequired]
    public bool ResizeCorner { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public bool BallVisible { get; set; } = true;
    [System.Text.Json.Serialization.JsonRequired]
    public bool AutoStart { get; set; } = true;
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, WindowPosition> Positions { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, string> ConnectionConfigs { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, string> MachineMarkers { get; set; } = [];
    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
    {
        if (Columns is null) return;
        // These three built-ins could be deleted in previous versions. Preserve
        // the saved identity/name and the user's choice not to show the window.
        foreach (var lane in new[] { Lane.Refining, Lane.Paused, Lane.Done })
        {
            var column = Columns.FirstOrDefault(x => x.BuiltinLane == lane);
            if (column is null) { column = BoardColumn.Defaults().Single(x => x.BuiltinLane == lane); column.Visible = false; Columns.Add(column); }
            else if (column.Deleted) { column.Deleted = false; column.Visible = false; }
        }
    }
}

public sealed class ArchivedTask
{
    public WorkItem Item { get; set; } = new();
    public BoardColumn OriginalColumn { get; set; } = new();
    public DateTime ArchivedUtc { get; set; } = DateTime.UtcNow;
    public int OriginalIndex { get; set; }
}

public sealed class BoardState
{
    [System.Text.Json.Serialization.JsonRequired]
    public int Version { get; set; } = 2;
    public Guid PersistenceRevision { get; set; }
    public List<ArchivedTask> Archives { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public List<WorkItem> Tasks { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public List<EditSession> Sessions { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public List<FieldOption> AiOptions { get; set; } =
    [new() { Name = "codex", Color = "#77CBB0" }, new() { Name = "dsh", Color = "#82AFF7" }, new() { Name = "workbuddy", Color = "#C1A0EB" }];
    [System.Text.Json.Serialization.JsonRequired]
    public List<FieldOption> PriorityOptions { get; set; } =
    [new() { Name = "紧急", Color = "#F28F98" }, new() { Name = "重要不紧急", Color = "#E9C582" }, new() { Name = "不重要不紧急", Color = "#93A6BE" }];
    [System.Text.Json.Serialization.JsonRequired]
    public Preferences Preferences { get; set; } = new();
}

public static class Copies
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNameCaseInsensitive = false };
    public static T Of<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Json), Json)!;
    public static bool Same<T>(T a, T b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);
}
