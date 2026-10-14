using System.Text.Json;

namespace Taskboard.Core;

public sealed class AtomicStore
{
    private readonly SemaphoreSlim gate = new(1);
    public string DirectoryPath { get; }
    public string MainPath => Path.Combine(DirectoryPath, "board.json");
    public string BackupPath => MainPath + ".bak";
    public string? RecoveryMessage { get; private set; }
    public string? ConfigurationDirectory { get; }
    public string? ConfigurationPath => ConfigurationDirectory is null ? null : Path.Combine(ConfigurationDirectory, "settings.json");

    public AtomicStore(string directory, string? configurationDirectory = null)
    { DirectoryPath = Path.GetFullPath(directory); ConfigurationDirectory = configurationDirectory is null ? null : Path.GetFullPath(configurationDirectory); }

    public BoardState Load()
    {
        var state = LoadBoard();
        if (ConfigurationPath is not string path || (!File.Exists(path) && !File.Exists(path + ".bak"))) return state;
        ConfigurationBackup configuration;
        try { configuration = ConfigurationBackup.Read(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            try { configuration = ConfigurationBackup.Read(path + ".bak"); }
            catch (Exception backup)
            {
                if (!File.Exists(MainPath) && !File.Exists(BackupPath)) throw new InvalidDataException("配置与备份都无法读取且没有任务恢复副本，原文件保留：" + backup.Message, e);
                foreach (var original in new[] { path, path + ".bak" }) if (File.Exists(original)) File.Copy(original, original + ".damaged-" + Guid.NewGuid().ToString("N"));
                ConfigurationBackup.Capture(state, DirectoryPath).Write(path);
                RecoveryMessage = "配置与配置备份损坏，已从任务文件的完整恢复配置重建，损坏原件保留。";
                return state;
            }
            if (File.Exists(path)) File.Copy(path, path + ".damaged-" + Guid.NewGuid().ToString("N"));
            File.Copy(path + ".bak", path, true); RecoveryMessage = "配置主文件损坏，已从配置备份恢复，原文件保留。";
        }
        // A task commit is durable before its matching settings commit. On an
        // interrupted two-file save, the embedded recovery settings are newer.
        if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuration.DataDirectory)), Path.TrimEndingDirectorySeparator(DirectoryPath), StringComparison.OrdinalIgnoreCase) &&
            configuration.PersistenceRevision != Guid.Empty && state.PersistenceRevision != Guid.Empty && configuration.PersistenceRevision != state.PersistenceRevision)
        { RecoveryMessage = (RecoveryMessage is null ? "" : RecoveryMessage + " ") + "上次配置保存未完成，使用任务文件内的完整配置恢复；下一次保存修复配置文件。"; return state; }
        return configuration.Apply(state);
    }

    private BoardState LoadBoard()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(MainPath))
        {
            if (File.Exists(BackupPath)) return Recover("主文件缺失");
            if (ConfigurationPath is string configuration && File.Exists(configuration))
            {
                var existing = ConfigurationBackup.Read(configuration);
                if (string.Equals(Path.GetFullPath(existing.DataDirectory), DirectoryPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("任务文件和任务备份都缺失，原配置保留；不会建立空任务替代，请检查数据目录。");
            }
            return new BoardState();
        }
        try { return Read(MainPath); }
        catch (Exception e) when (e is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        { return Recover(e.Message); }
    }

    private BoardState Recover(string reason)
    {
        // Keep the primary untouched if no usable backup exists. Never initialize over bad data.
        BoardState state;
        try { state = Read(BackupPath); }
        catch (Exception e) { throw new InvalidDataException($"数据读取失败：{reason}；备份也不可用：{e.Message}。原文件保留，请先检查数据目录。", e); }
        if (File.Exists(MainPath)) File.Copy(MainPath, MainPath + ".damaged-" + Guid.NewGuid().ToString("N"));
        var recovered = MainPath + ".recovery";
        File.Copy(BackupPath, recovered, true);
        File.Move(recovered, MainPath, true);
        RecoveryMessage = $"主数据无法读取（{reason}），已恢复上一次备份，损坏原件保留在数据目录。";
        return state;
    }

    public static BoardState Read(string path)
    {
        var state = JsonSerializer.Deserialize<BoardState>(File.ReadAllBytes(path), Copies.Json) ?? throw new InvalidDataException("文件没有数据");
        Validation.Check(state);
        return state;
    }

    public async Task SaveAsync(BoardState snapshot)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                snapshot.Version = 2; snapshot.PersistenceRevision = Guid.NewGuid();
                Validation.Check(snapshot);
                Directory.CreateDirectory(DirectoryPath);
                var temp = MainPath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(stream, snapshot, Copies.Json);
                        stream.Flush(true);
                    }
                    if (File.Exists(MainPath))
                    {
                        // Two same-directory renames: first durable backup, then durable new primary.
                        // File.Replace requests metadata privileges unavailable in some portable folders.
                        _ = Read(MainPath);
                        var backupTemp = BackupPath + ".tmp-" + Guid.NewGuid().ToString("N");
                        try
                        {
                            using (var input = new FileStream(MainPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var output = new FileStream(backupTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            { input.CopyTo(output); output.Flush(true); }
                            File.Move(backupTemp, BackupPath, true);
                        }
                        finally { if (File.Exists(backupTemp)) File.Delete(backupTemp); }
                    }
                    File.Move(temp, MainPath, true);
                    if (ConfigurationPath is string configuration)
                    {
                        Directory.CreateDirectory(ConfigurationDirectory!);
                        if (File.Exists(configuration))
                        {
                            _ = ConfigurationBackup.Read(configuration);
                            File.Copy(configuration, configuration + ".bak", true);
                        }
                        var settings = ConfigurationBackup.Capture(snapshot, DirectoryPath);
                        settings.Preferences.Positions = Copies.Of(snapshot.Preferences.Positions);
                        settings.Write(configuration);
                    }
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}
