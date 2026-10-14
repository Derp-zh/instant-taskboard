using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Taskboard.Desktop;

internal static class DataPaths
{
    private sealed record Selection(int Version, string DataDirectory);
    public static string DefaultDirectory()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "launch-settings.json");
        if (!File.Exists(file)) return Path.Combine(AppContext.BaseDirectory, "data");
        var selected = ReadSelection(file);
        if (!Directory.Exists(selected)) throw new DirectoryNotFoundException("保存的数据目录不存在：" + selected);
        return selected;
    }
    internal static string ReadSelection(string file)
    {
        var saved = JsonSerializer.Deserialize<Selection>(File.ReadAllText(file));
        if (saved is null || saved.Version != 1 || string.IsNullOrWhiteSpace(saved.DataDirectory)) throw new InvalidDataException("保存的数据路径无效，请检查launch-settings.json");
        return Path.GetFullPath(saved.DataDirectory, Path.GetDirectoryName(Path.GetFullPath(file))!);
    }
    public static Mutex Lock(string directory, out bool acquired)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant())));
        var mutex = new Mutex(false, @"Local\Taskboard-" + hash);
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        return mutex;
    }
    public static void Save(string file, string directory)
    {
        var temporary = file + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var absolute = Path.GetFullPath(directory); var relative = Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(file))!, absolute);
                var portable = !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
                JsonSerializer.Serialize(stream, new Selection(1, portable ? relative : absolute)); stream.Flush(true);
            }
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
