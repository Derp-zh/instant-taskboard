using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class DataPaths
{
    private sealed record Selection(int Version, string DataDirectory, string? ConfigurationDirectory = null);
    internal static string SystemRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InstantTaskboard");
    internal static string SelectionFile => Path.Combine(SystemRoot, "locations.json");
    internal static string DefaultDataDirectory => Path.Combine(SystemRoot, "data");
    public static string DefaultDirectory() => Initialize(AppContext.BaseDirectory, SystemRoot, StartupLinks.ExistingInstallation());
    internal static bool NeedsLegacyChoice() => !File.Exists(SelectionFile) && !HasBoard(DefaultDataDirectory) && !HasLegacyData(AppContext.BaseDirectory) &&
        (StartupLinks.ExistingInstallation() is not string prior || !HasLegacyData(prior));
    internal static string MigrateSelectedDirectory(string source, string systemRoot)
    {
        var target = Path.Combine(systemRoot, "data");
        if (HasBoard(target)) throw new IOException("系统目录已有任务，不能覆盖，请在设置中切换数据目录。");
        using var original = Lock(source, out var acquired);
        if (!acquired) throw new IOException("原数据正被旧版本使用，请先从托盘退出旧版本。");
        try
        {
            if (!HasBoard(source)) throw new InvalidDataException("所选目录没有原任务或备份，不能作为迁移来源。");
            try { _ = AtomicStore.Read(Path.Combine(source, "board.json")); }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException) { _ = AtomicStore.Read(Path.Combine(source, "board.json.bak")); }
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(source, "board.json*")) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
            var configuration = Path.Combine(systemRoot, "config"); Directory.CreateDirectory(configuration);
            Save(Path.Combine(systemRoot, "locations.json"), target, configuration); return target;
        }
        finally { original.ReleaseMutex(); }
    }
    internal static string ConfigurationDirectory() => File.Exists(SelectionFile) ? ReadLocations(SelectionFile).ConfigurationDirectory! : Path.Combine(SystemRoot, "config");
    internal static string Initialize(string applicationDirectory, string systemRoot, string? priorInstallation = null)
    {
        using var initialization = Lock(Path.Combine(systemRoot, "initialization"), out var acquired);
        if (!acquired) throw new IOException("另一个程序正在初始化保存路径，请稍后重试。");
        try
        {
            var selectionFile = Path.Combine(systemRoot, "locations.json");
            if (File.Exists(selectionFile))
            {
                var saved = ReadLocations(selectionFile);
                if (!Directory.Exists(saved.DataDirectory) || !Directory.Exists(saved.ConfigurationDirectory))
                    throw new DirectoryNotFoundException("保存的数据或配置目录不存在，请恢复原目录或在启动提示中选择原数据，程序不会建立空数据替代。");
                return saved.DataDirectory;
            }
            var target = Path.Combine(systemRoot, "data"); var configuration = Path.Combine(systemRoot, "config");
            var sourceInstallation = applicationDirectory;
            if (!HasLegacyData(sourceInstallation) && priorInstallation is not null && HasLegacyData(priorInstallation)) sourceInstallation = priorInstallation;
            var legacySelection = Path.Combine(sourceInstallation, "launch-settings.json");
            if (File.Exists(legacySelection))
            {
                var selected = ReadSelection(legacySelection);
                if (!Directory.Exists(selected) || !HasBoard(selected)) throw new DirectoryNotFoundException("原来的自选数据目录或任务文件不存在：" + selected);
                Directory.CreateDirectory(configuration); Save(selectionFile, selected, configuration); return selected;
            }
            var source = Path.Combine(sourceInstallation, "data");
            if (!File.Exists(Path.Combine(target, "board.json")) && !File.Exists(Path.Combine(target, "board.json.bak")) && HasBoard(source))
            {
                using var original = Lock(source, out var sourceFree);
                if (!sourceFree) throw new IOException("原数据正被旧版本使用，请先从旧版托盘退出程序，再启动新版迁移。原件不会删除。");
                try
                {
                    try { _ = AtomicStore.Read(Path.Combine(source, "board.json")); }
                    catch (Exception e) when (e is IOException or InvalidDataException or JsonException) { _ = AtomicStore.Read(Path.Combine(source, "board.json.bak")); }
                    Directory.CreateDirectory(target);
                    foreach (var file in Directory.EnumerateFiles(source, "board.json*")) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
                }
                finally { original.ReleaseMutex(); }
            }
            Directory.CreateDirectory(target); Directory.CreateDirectory(configuration); Save(selectionFile, target, configuration); return target;
        }
        finally { initialization.ReleaseMutex(); }
    }
    private static bool HasBoard(string directory) => File.Exists(Path.Combine(directory, "board.json")) || File.Exists(Path.Combine(directory, "board.json.bak"));
    private static bool HasLegacyData(string installation) => File.Exists(Path.Combine(installation, "launch-settings.json")) || HasBoard(Path.Combine(installation, "data"));
    internal static string ReadSelection(string file) => ReadLocations(file).DataDirectory;
    private static Selection ReadLocations(string file)
    {
        var saved = JsonSerializer.Deserialize<Selection>(File.ReadAllText(file));
        if (saved is null || saved.Version is not (1 or 2) || string.IsNullOrWhiteSpace(saved.DataDirectory)) throw new InvalidDataException("保存路径文件无效，原文件保留：" + file);
        var parent = Path.GetDirectoryName(Path.GetFullPath(file))!;
        return new(2, Path.GetFullPath(saved.DataDirectory, parent), saved.ConfigurationDirectory is null ? Path.Combine(parent, "config") : Path.GetFullPath(saved.ConfigurationDirectory, parent));
    }
    public static Mutex Lock(string directory, out bool acquired)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant())));
        var mutex = new Mutex(false, @"Local\Taskboard-" + hash);
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        return mutex;
    }
    public static void Save(string file, string directory, string? configurationDirectory = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        configurationDirectory ??= File.Exists(file) ? ReadLocations(file).ConfigurationDirectory : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, "config");
        var temporary = file + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, new Selection(2, Path.GetFullPath(directory), Path.GetFullPath(configurationDirectory!))); stream.Flush(true); }
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
