namespace Taskboard.Core;

public sealed class StartupInstallation
{
    public string Executable { get; set; } = "";
    public string Version { get; set; } = "0.0.0";
    public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;
    public DateTime BuildUtc { get; set; }
}

public static class StartupCatalog
{
    public static StartupInstallation? Latest(IEnumerable<StartupInstallation> installations, Func<string, bool> exists) =>
        installations.Where(x => !string.IsNullOrWhiteSpace(x.Executable) && System.Version.TryParse(x.Version, out _) && exists(x.Executable))
            .OrderByDescending(x => System.Version.Parse(x.Version)).ThenByDescending(x => x.BuildUtc).ThenByDescending(x => x.RegisteredUtc).FirstOrDefault();
    public static void Register(List<StartupInstallation> installations, string executable, Version version, DateTime? buildUtc = null)
    {
        installations.RemoveAll(x => string.Equals(x.Executable, executable, StringComparison.OrdinalIgnoreCase));
        installations.Add(new() { Executable = executable, Version = version.ToString(), BuildUtc = buildUtc ?? DateTime.UtcNow });
    }
}
