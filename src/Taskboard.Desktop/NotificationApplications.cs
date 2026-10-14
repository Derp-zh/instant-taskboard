using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed record NotificationApplication(string Name, string Id)
{
    public override string ToString() => Name + " · " + Id;
}
internal static class NotificationApplications
{
    public static Task<NotificationApplication[]> Read() => Task.Run(() =>
    {
        using var registry = Registry.CurrentUser.OpenSubKey(WindowsNotificationSoundStore.RegistryPath);
        var ids = registry?.GetSubKeyNames() ?? [];
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // AppsFolder exposes the shell display name and exact AUMID for desktop and packaged apps.
        object? shell = null, folder = null, items = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is not null)
            {
                shell = Activator.CreateInstance(type); folder = ((dynamic)shell!).NameSpace("shell:AppsFolder"); items = ((dynamic)folder!).Items();
                foreach (var item in (dynamic)items!)
                { try { names[(string)item.Path] = (string)item.Name; } finally { if (Marshal.IsComObject(item)) Marshal.ReleaseComObject(item); } }
            }
        }
        catch (Exception) { /* Registry identifiers remain selectable if shell display names are unavailable. */ }
        finally { foreach (var obj in new[] { items, folder, shell }) if (obj is not null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
        return ids.Where(x => !x.Contains('\\')).Select(id => new NotificationApplication(names.GetValueOrDefault(id, id), id))
            .Where(x => !NotificationSoundRules.IsSelf(x.Name)).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    });
}
