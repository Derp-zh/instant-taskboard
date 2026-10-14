using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Taskboard.Core;
using Microsoft.Win32;

namespace Taskboard.Desktop;

internal static class StartupLinks
{
    private const string Entry = "TaskboardPortable";
    private static string CatalogFile => Path.Combine(DataPaths.SystemRoot, "installations.json");
    internal static string? ExistingInstallation()
    {
        var current = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var known = ReadCatalog().Where(x => !string.Equals(Path.GetDirectoryName(x.Executable), current, StringComparison.OrdinalIgnoreCase));
        var previous = StartupCatalog.Latest(known, File.Exists);
        if (previous is not null) return Path.GetDirectoryName(previous.Executable);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var executable = ExecutableFromCommand(key?.GetValue(Entry) as string);
        return executable is not null && File.Exists(executable) ? Path.GetDirectoryName(executable) : null;
    }
    private static List<StartupInstallation> ReadCatalog() => File.Exists(CatalogFile)
        ? JsonSerializer.Deserialize<List<StartupInstallation>>(File.ReadAllBytes(CatalogFile)) ?? throw new InvalidDataException("程序版本登记损坏，原文件保留。") : [];
    internal static string? ExecutableFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim(); if (command.StartsWith('"')) { var end = command.IndexOf('"', 1); return end > 1 ? command[1..end] : null; }
        var suffix = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); return suffix < 0 ? null : command[..(suffix + 4)];
    }
    internal static string RegisterCurrent()
    {
        using var registration = DataPaths.Lock(CatalogFile, out var acquired);
        if (!acquired) throw new IOException("另一份程序正在登记启动版本，请稍后重试。");
        try
        {
        var entries = ReadCatalog();
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
        {
            var previous = ExecutableFromCommand(run?.GetValue(Entry) as string);
            if (previous is not null && File.Exists(previous))
            {
                var dll = Path.Combine(Path.GetDirectoryName(previous)!, "Taskboard.dll");
                if (File.Exists(dll) && Version.TryParse(FileVersionInfo.GetVersionInfo(dll).FileVersion, out var oldVersion))
                    StartupCatalog.Register(entries, previous, oldVersion, File.GetLastWriteTimeUtc(dll));
            }
        }
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("程序启动路径不可用");
        StartupCatalog.Register(entries, current, typeof(StartupLinks).Assembly.GetName().Version!, File.GetLastWriteTimeUtc(Path.Combine(AppContext.BaseDirectory, "Taskboard.dll")));
        Directory.CreateDirectory(DataPaths.SystemRoot); var temporary = CatalogFile + ".pending-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(entries)); File.Move(temporary, CatalogFile, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return StartupCatalog.Latest(entries, File.Exists)!.Executable;
        }
        finally { registration.ReleaseMutex(); }
    }
    private static string LatestExecutable() => StartupCatalog.Latest(ReadCatalog(), File.Exists)?.Executable ?? Environment.ProcessPath!;
    public static string MachineIdentity()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var identity = key?.GetValue("MachineGuid") as string ?? Environment.MachineName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
    public static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue(Entry, "\"" + LatestExecutable() + "\" --startup");
        else key.DeleteValue(Entry, false);
    }
    public static bool PathChanged()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var value = key?.GetValue(Entry) as string;
        return value is not null && !string.Equals(ExecutableFromCommand(value), LatestExecutable(), StringComparison.OrdinalIgnoreCase);
    }
    internal static string AutoStartStatus()
    {
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (run?.GetValue(Entry) is not string command) return "开机自启已关闭";
        if (!File.Exists(ExecutableFromCommand(command))) return "自启路径失效，请修复启动入口";
        // Read-only diagnostic for Task Manager's implementation-specific flag.
        using var approval = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        if (approval?.GetValue(Entry) is byte[] bytes && bytes.Length > 0 && bytes[0] is 3 or 7) return "Windows启动应用中已禁用此入口，请在系统设置启用";
        return "自启入口有效 · 已登记版本中的最新版";
    }
    internal static void VerifyAndUpdateExistingEntry(string directory)
    {
        Directory.CreateDirectory(directory);
        using var before = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var prior = before?.GetValue(Entry) as string;
        var selected = RegisterCurrent();
        if (prior is not null) SetAutoStart(true);
        using var after = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var command = after?.GetValue(Entry) as string;
        var valid = prior is null ? command is null : string.Equals(ExecutableFromCommand(command), selected, StringComparison.OrdinalIgnoreCase) && File.Exists(selected);
        if (!valid) throw new IOException("自启入口回读与选定最新版不一致");
        File.WriteAllText(Path.Combine(directory, "startup-entry-report.json"), JsonSerializer.Serialize(new { passed = 3, priorCommand = prior, command, latestExecutable = selected,
            status = AutoStartStatus(), preservedDisabledPreference = prior is null, realRegistryReadBack = true, rebootPerformed = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static void Shortcut()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("此系统不能创建快捷方式。");
        object? shell = null; object? link = null;
        try
        {
            shell = Activator.CreateInstance(shellType)!;
            dynamic dynamicShell = shell;
            link = dynamicShell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "即时任务看板.lnk"));
            dynamic shortcut = link;
            shortcut.TargetPath = LatestExecutable();
            shortcut.WorkingDirectory = Path.GetDirectoryName(LatestExecutable());
            shortcut.Description = "即时任务看板";
            shortcut.Save();
        }
        finally
        {
            if (link is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            if (shell is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
