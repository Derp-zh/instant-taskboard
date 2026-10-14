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

public sealed class WorkItem
{
    public Guid? ColumnId { get; set; }
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

    public WorkItem() => CurrentStageId = Stages[0].Id;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasContent => !string.IsNullOrWhiteSpace(Title) || Stages.Any(x => !string.IsNullOrWhiteSpace(x.Text));
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title.Trim() :
        Stages.SelectMany(x => x.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "未命名任务";
}

public sealed class EditSession
{
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

public sealed class Preferences
{
    public NotificationListeningOptions Listening { get; set; } = new();
    public List<BoardColumn> Columns { get; set; } = BoardColumn.Defaults();
    // Optional for backwards compatibility with 0.3/0.4 data files.
    public double GlassTransparency { get; set; } = 20;
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
}

public sealed class BoardState
{
    [System.Text.Json.Serialization.JsonRequired]
    public int Version { get; set; } = 1;
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
