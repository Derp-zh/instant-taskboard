using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace Taskboard.Desktop;

internal sealed record OriginalNotificationSound(string AppId, bool Existed, string Value);
internal interface INotificationSoundStore
{
    OriginalNotificationSound Read(string appId);
    void Silence(string appId);
    void Restore(OriginalNotificationSound original);
}

// Touches only the sender's SoundFile value; never Enabled, ShowBanner, volume, or audio sessions.
internal sealed class WindowsNotificationSoundStore : INotificationSoundStore
{
    internal const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private static RegistryKey Open(string id, bool writable)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Contains('\\') || id.IndexOf('\0') >= 0) throw new IOException("通知发送方标识无效");
        return Registry.CurrentUser.OpenSubKey(RegistryPath + "\\" + id, writable) ?? throw new IOException("该应用尚未在Windows注册通知，请先发送一条通知");
    }
    public OriginalNotificationSound Read(string appId)
    {
        using var key = Open(appId, false);
        var exists = key.GetValueNames().Contains("SoundFile", StringComparer.OrdinalIgnoreCase);
        if (exists && key.GetValueKind("SoundFile") != RegistryValueKind.String) throw new IOException("该应用通知声音设置类型不兼容，未修改");
        return new(appId, exists, key.GetValue("SoundFile", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "");
    }
    public void Silence(string appId)
    {
        using var key = Open(appId, true); key.SetValue("SoundFile", "", RegistryValueKind.String); key.Flush();
        if (Read(appId) is not { Existed: true, Value.Length: 0 }) throw new IOException("Windows通知声音设置未写入");
    }
    public void Restore(OriginalNotificationSound original)
    {
        using var key = Open(original.AppId, true);
        if (original.Existed) key.SetValue("SoundFile", original.Value, RegistryValueKind.String);
        else key.DeleteValue("SoundFile", false);
        key.Flush();
    }
}

// Persist originals before changing Windows. This file is deliberately outside portable configuration backups.
internal sealed class NotificationSoundLease
{
    private sealed record Journal(string Owner, List<OriginalNotificationSound> Originals);
    private readonly string file;
    private readonly INotificationSoundStore store;
    private readonly string owner;
    private readonly Dictionary<string, OriginalNotificationSound> originals = new(StringComparer.Ordinal);
    public NotificationSoundLease(string file, INotificationSoundStore store, string? owner = null)
    {
        this.file = file; this.store = store; this.owner = owner ?? Environment.MachineName + "\\" + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        if (!File.Exists(file)) return;
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(file)) ?? throw new IOException("通知声音恢复记录损坏，未修改系统");
        if (journal.Owner != this.owner) throw new IOException("通知声音恢复记录属于其他电脑或用户，未修改系统");
        foreach (var original in journal.Originals) originals.Add(original.AppId, original);
    }
    public void Acquire(string id)
    {
        if (originals.ContainsKey(id)) return;
        var original = store.Read(id); originals.Add(id, original);
        try { Save(); } catch { originals.Remove(id); throw; }
        try { store.Silence(id); }
        catch { Release(id); throw; }
    }
    public void Release(string id)
    {
        if (!originals.TryGetValue(id, out var original)) return;
        var current = store.Read(id);
        // Keep a user change made outside this application instead of overwriting it on exit.
        if (current.Existed && current.Value.Length == 0) store.Restore(original);
        originals.Remove(id);
        try { Save(); } catch { originals.Add(id, original); throw; }
    }
    public string[] RestoreAll()
    {
        var errors = new List<string>();
        foreach (var id in originals.Keys.ToArray()) { try { Release(id); } catch (Exception e) { errors.Add(id + "：" + e.Message); } }
        return errors.ToArray();
    }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temporary = file + ".pending";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, new Journal(owner, originals.Values.ToList())); stream.Flush(true); }
        File.Move(temporary, file, true);
    }
}
