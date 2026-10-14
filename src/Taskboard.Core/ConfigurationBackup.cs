using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskboard.Core;

// A deliberately separate schema: tasks, stages, sessions and reminders cannot
// appear here, nor can an old full-data backup be accidentally imported.
public sealed class ConfigurationBackup
{
    [JsonRequired] public string Format { get; set; } = "taskboard-configuration";
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public string DataDirectory { get; set; } = "";
    public Guid PersistenceRevision { get; set; }
    [JsonRequired] public Preferences Preferences { get; set; } = new();
    [JsonRequired] public List<FieldOption> AiOptions { get; set; } = [];
    [JsonRequired] public List<FieldOption> PriorityOptions { get; set; } = [];
    private static readonly JsonSerializerOptions Json = new()
    { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static ConfigurationBackup Capture(BoardState state, string directory)
    {
        Validation.Check(state);
        var result = new ConfigurationBackup { DataDirectory = Path.GetFullPath(directory), PersistenceRevision = state.PersistenceRevision, Preferences = Copies.Of(state.Preferences),
            AiOptions = Copies.Of(state.AiOptions), PriorityOptions = Copies.Of(state.PriorityOptions) };
        foreach (var key in result.Preferences.Positions.Keys.Where(x => x.StartsWith("edit-", StringComparison.Ordinal)).ToArray()) result.Preferences.Positions.Remove(key);
        return result;
    }
    public static ConfigurationBackup Read(string file)
    {
        using var stream = File.OpenRead(file);
        var result = JsonSerializer.Deserialize<ConfigurationBackup>(stream, Json) ?? throw new InvalidDataException("配置备份为空");
        result.Check(); return result;
    }
    public void Write(string file)
    {
        Check(); var full = Path.GetFullPath(file); var temporary = full + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, this, Json); stream.Flush(true); }
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Check()
    {
        if (Format != "taskboard-configuration" || Version != 1 || string.IsNullOrWhiteSpace(DataDirectory) || Preferences is null || AiOptions is null || PriorityOptions is null)
            throw new InvalidDataException("不是有效的任务面板配置备份（不能导入任务备份）");
        _ = Path.GetFullPath(DataDirectory);
        Validation.Check(new BoardState { Preferences = Preferences, AiOptions = AiOptions, PriorityOptions = PriorityOptions });
    }
    public BoardState Apply(BoardState state)
    {
        Check(); var candidate = Copies.Of(state); candidate.Preferences = Copies.Of(Preferences);
        // Preserve all task references exactly. Referenced old definitions survive
        // alongside restored definitions; importing configuration never edits tasks.
        var items = state.Tasks.Concat(state.Sessions.SelectMany(x => new[] { x.Working, x.Original })).ToArray();
        foreach (var column in state.Preferences.Columns.Where(x => items.Any(x.Contains)))
        {
            var restored = candidate.Preferences.Columns.FirstOrDefault(x => column.BuiltinLane is not null ? x.BuiltinLane == column.BuiltinLane : x.Id == column.Id);
            if (restored is null) candidate.Preferences.Columns.Add(Copies.Of(column)); else restored.Deleted = false;
        }
        var references = items.Concat(state.Archives.Select(x => x.Item)).ToArray();
        candidate.AiOptions = Merge(AiOptions, state.AiOptions, references.SelectMany(x => x.AiIds).ToHashSet());
        candidate.PriorityOptions = Merge(PriorityOptions, state.PriorityOptions, references.Where(x => x.PriorityId is not null).Select(x => x.PriorityId!.Value).ToHashSet());
        Validation.Check(candidate); return candidate;
    }
    private static List<FieldOption> Merge(List<FieldOption> restored, List<FieldOption> current, HashSet<Guid> referenced)
    {
        var result = Copies.Of(restored);
        result.AddRange(Copies.Of(current.Where(x => referenced.Contains(x.Id) && !result.Any(y => y.Id == x.Id)).ToList())); return result;
    }
}
