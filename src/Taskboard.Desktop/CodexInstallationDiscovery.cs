using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;
using Windows.Management.Deployment;

namespace Taskboard.Desktop;

internal sealed record CodexInstallation(string Desktop, string Cli, string Version, string[] RegisteredDesktops, string Diagnostic);

// Installation metadata only. Never enumerates chat files or starts the desktop.
internal static class CodexInstallationDiscovery
{
    internal static bool Exists(string? path) => !string.IsNullOrWhiteSpace(path) &&
        string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    internal static async Task<CodexInstallation> Find(string desktop, string cli)
    {
        var registered = await Task.Run(RegisteredDesktops).ConfigureAwait(false);
        var selectedDesktop = Exists(desktop) ? desktop : registered.FirstOrDefault() ?? "";
        var cliCandidates = new List<string>();
        if (Exists(cli)) cliCandidates.Add(cli);
        // Prefer the CLI shipped with the selected/registered desktop, then PATH.
        foreach (var app in new[] { selectedDesktop }.Concat(registered).Where(Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.GetDirectoryName(app)!;
            foreach (var relative in new[] { "resources/codex.exe", "resources/bin/codex.exe", "bin/codex.exe" })
                cliCandidates.Add(Path.Combine(folder, relative));
        }
        cliCandidates.Add(FindCliExecutable());
        var selectedCli = "";
        foreach (var candidate in cliCandidates.Where(Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            if (await IsCli(candidate).ConfigureAwait(false)) { selectedCli = candidate; break; }
        var version = Exists(selectedDesktop) ? RegisteredVersion(selectedDesktop) : "";
        return new(selectedDesktop, selectedCli, version, registered,
            $"登记桌面数量：{registered.Length}；桌面与CLI分别核对。" +
            (desktop.Length > 0 && !Exists(desktop) ? " 原桌面路径已失效；原值保留供修复。" : "") +
            (cli.Length > 0 && !string.Equals(cli, selectedCli, StringComparison.OrdinalIgnoreCase) ? " 原CLI未通过检查；原值保留供修复。" : ""));
    }

    internal static string[] RegisteredDesktops()
    {
        var paths = new List<string>();
        try
        {
            var manager = new PackageManager();
            foreach (var package in manager.FindPackagesForUser("").Where(x => x.Id.FamilyName == "OpenAI.Codex_2p2nqsd0c76g0"))
            {
                var root = package.InstalledLocation.Path;
                var manifest = XDocument.Load(Path.Combine(root, "AppxManifest.xml"));
                foreach (var application in manifest.Descendants().Where(x => x.Name.LocalName == "Application"))
                {
                    var visual = application.Elements().FirstOrDefault(x => x.Name.LocalName == "VisualElements");
                    if (visual is null || visual.Attribute("AppListEntry")?.Value == "none") continue;
                    var executable = application.Attribute("Executable")?.Value;
                    if (string.IsNullOrWhiteSpace(executable)) continue;
                    var path = Path.GetFullPath(Path.Combine(root, executable.Replace('\\', Path.DirectorySeparatorChar)));
                    if (path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Exists(path)) paths.Add(path);
                }
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or System.Xml.XmlException) { }
        // Non-store installations: only explicit system registration, no disk-wide search.
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var name in new[] { "Codex.exe", "ChatGPT.exe" })
        {
            try
            {
                using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + name);
                if (key?.GetValue("") is string value && Exists(value.Trim('"')) &&
                    (FileVersionInfo.GetVersionInfo(value.Trim('"')).ProductName ?? "").Contains("Codex", StringComparison.OrdinalIgnoreCase)) paths.Add(value.Trim('"'));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string FindCliExecutable()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            try { var candidate = Path.Combine(directory.Trim('"'), "codex.exe"); if (IsConsoleExecutable(candidate)) return candidate; }
            catch (ArgumentException) { }
        }
        var known = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        try { return Directory.Exists(known) ? Directory.EnumerateFiles(known, "codex.exe", SearchOption.AllDirectories).FirstOrDefault(IsConsoleExecutable) ?? "" : ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static string RegisteredVersion(string executable)
    {
        try
        {
            foreach (var package in new PackageManager().FindPackagesForUser("").Where(x => x.Id.FamilyName == "OpenAI.Codex_2p2nqsd0c76g0"))
                if (Path.GetFullPath(executable).StartsWith(Path.GetFullPath(package.InstalledLocation.Path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var version = package.Id.Version;
                    return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException) { }
        return "文件版本 " + (FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "未知");
    }

    internal static async Task<bool> IsCli(string path)
    {
        if (!Exists(path)) return false;
        // Do not launch a desktop executable as a CLI, including accidental manual selections.
        if (!string.Equals(Path.GetFileName(path), "codex.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (!IsConsoleExecutable(path) || RegisteredDesktops().Contains(path, StringComparer.OrdinalIgnoreCase)) return false;
        try
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--version");
            using var process = Process.Start(start);
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false); }
            catch (TimeoutException) { if (!process.HasExited) process.Kill(); return false; }
            _ = await error.ConfigureAwait(false);
            return process.ExitCode == 0 && (await output.ConfigureAwait(false)).TrimStart().StartsWith("codex", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    internal static bool IsConsoleExecutable(string path)
    {
        if (!Exists(path)) return false;
        try
        {
            using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
            return pe.PEHeaders.PEHeader?.Subsystem == Subsystem.WindowsCui;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException) { return false; }
    }

    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
