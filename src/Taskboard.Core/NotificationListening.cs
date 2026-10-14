namespace Taskboard.Core;

public enum NotificationCaptureMode { Auto, Accessibility, WinRT }

public sealed class NotificationSoundRule
{
    public const string BuiltInAudioPath = "builtin:alarm04";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AppName { get; set; } = "";
    public string AppId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string AudioPath { get; set; } = BuiltInAudioPath;
}

public sealed class NotificationListeningOptions
{
    public bool Enabled { get; set; }
    public NotificationCaptureMode CaptureMode { get; set; }
    public double Volume { get; set; } = 0.8;
    public List<NotificationSoundRule> Rules { get; set; } = [];
}

public sealed record CapturedNotification(string AppName, string AppId, string Title, string Body, string Source);

public static class NotificationSoundRules
{
    public static string Normalize(string? name)
    {
        var value = (name ?? "").Trim().Trim('\u200B', '\uFEFF');
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4].TrimEnd() : value;
    }

    public static bool SameName(string? a, string? b) => Normalize(a).Length > 0 &&
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static NotificationSoundRule? Resolve(NotificationListeningOptions options, CapturedNotification notification)
    {
        if (!options.Enabled || IsSelf(notification.AppName)) return null;
        // Authoritative app identity takes priority; a different identity must not match by display name.
        if (!string.IsNullOrEmpty(notification.AppId))
        {
            var identified = options.Rules.FirstOrDefault(x => x.AppId.Length > 0 && string.Equals(x.AppId, notification.AppId, StringComparison.OrdinalIgnoreCase));
            if (identified is not null) return identified.Enabled ? identified : null;
        }
        var named = options.Rules.Where(x => SameName(x.AppName, notification.AppName) &&
            (x.AppId.Length == 0 || notification.AppId.Length == 0)).ToArray();
        return named.Length == 1 && named[0].Enabled ? named[0] : null;
    }

    public static bool IsSelf(string name) => new[] { "即时任务看板", "Taskboard", "PersonalTaskBoard", "任务提醒" }.Any(x => SameName(x, name));

    public static CapturedNotification? FromText(IEnumerable<string> texts, string source = "辅助功能", IEnumerable<string>? knownApps = null)
    {
        var parts = texts.Select(x => x.Trim().Trim('\u200B', '\uFEFF')).Where(x => x.Length > 0).ToArray();
        // Never guess an application from a short title/body-only notification.
        if (parts.Length < 2 || (parts.Length < 3 && !(knownApps?.Any(x => SameName(x, parts[0])) ?? false))) return null;
        return new(parts[0], "", parts[1], string.Join("\n", parts.Skip(2)), source);
    }

    public static CapturedNotification[] FromCombinedText(IEnumerable<string> texts, IEnumerable<string> knownApps)
    {
        var parts = texts.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        if (parts.Length == 0) return [];
        var markers = parts.Select((x, i) => (x, i)).Where(x => SameName(x.x, parts[0])).Select(x => x.i).ToArray();
        if (markers.Length > 1 && knownApps.Any(x => SameName(x, parts[0])) &&
            markers.Skip(1).Append(parts.Length).Zip(markers, (end, start) => end - start).All(x => x >= 2))
        {
            var ends = markers.Skip(1).Append(parts.Length).ToArray();
            return markers.Select((start, i) => FromText(parts.Skip(start).Take(ends[i] - start), knownApps: knownApps)).OfType<CapturedNotification>().ToArray();
        }
        var single = FromText(parts, knownApps: knownApps); return single is null ? [] : [single];
    }

    public static void Check(NotificationListeningOptions options)
    {
        if (options is null || options.Rules is null || !Enum.IsDefined(options.CaptureMode) ||
            !double.IsFinite(options.Volume) || options.Volume < 0 || options.Volume > 1)
            throw new InvalidDataException("监听配置或音量无效");
        if (options.Rules.Any(x => x is null || x.Id == Guid.Empty || x.AppName is null || x.AppId is null || x.AudioPath is null) ||
            options.Rules.Select(x => x.Id).Distinct().Count() != options.Rules.Count)
            throw new InvalidDataException("监听应用配置无效");
        // Empty rows are allowed as an unfinished configuration, but duplicate sources are ambiguous.
        var keys = options.Rules.Where(x => Normalize(x.AppName).Length > 0 || x.AppId.Length > 0)
            .Select(x => x.AppId.Length > 0 ? "id:" + x.AppId.ToUpperInvariant() : "name:" + Normalize(x.AppName).ToUpperInvariant()).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) throw new InvalidDataException("同一应用不能重复配置提醒音");
    }
}

// Used only in memory. The content fingerprint expires and is never written to task data.
public sealed class NotificationDeduplicator
{
    private readonly Dictionary<string, DateTimeOffset> recent = new(StringComparer.Ordinal);
    public bool Accept(CapturedNotification notification, DateTimeOffset now)
    {
        foreach (var key in recent.Where(x => now - x.Value >= TimeSpan.FromSeconds(5)).Select(x => x.Key).ToArray()) recent.Remove(key);
        // Use the normalized display name so WinRT and accessibility copies share a fingerprint.
        var keyValue = System.Text.Json.JsonSerializer.Serialize(new[] { NotificationSoundRules.Normalize(notification.AppName).ToUpperInvariant(), notification.Title.Trim(), notification.Body.Trim() });
        if (recent.ContainsKey(keyValue)) return false;
        if (recent.Count >= 512) recent.Remove(recent.MinBy(x => x.Value).Key);
        recent[keyValue] = now; return true;
    }
}
