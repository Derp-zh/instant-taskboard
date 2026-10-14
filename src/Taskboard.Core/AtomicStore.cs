using System.Text.Json;

namespace Taskboard.Core;

public sealed class AtomicStore
{
    private readonly SemaphoreSlim gate = new(1);
    public string DirectoryPath { get; }
    public string MainPath => Path.Combine(DirectoryPath, "board.json");
    public string BackupPath => MainPath + ".bak";
    public string? RecoveryMessage { get; private set; }

    public AtomicStore(string directory) => DirectoryPath = Path.GetFullPath(directory);

    public BoardState Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(MainPath))
        {
            if (File.Exists(BackupPath)) return Recover("主文件缺失");
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
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}
