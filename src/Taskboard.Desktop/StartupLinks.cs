using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Taskboard.Desktop;

internal static class StartupLinks
{
    private const string Entry = "TaskboardPortable";
    public static string MachineIdentity()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var identity = key?.GetValue("MachineGuid") as string ?? Environment.MachineName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
    public static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue(Entry, "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue(Entry, false);
    }
    public static bool PathChanged()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var value = key?.GetValue(Entry) as string;
        return value is not null && !string.Equals(value.Trim('"'), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
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
            shortcut.TargetPath = Environment.ProcessPath;
            shortcut.WorkingDirectory = AppContext.BaseDirectory;
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
