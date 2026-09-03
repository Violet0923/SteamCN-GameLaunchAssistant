namespace WetheringWavesSteamHelper_WinUI.Models;

// These models describe upstream metadata, independently of the current single-depot preset UI.
public sealed record SteamAppInfo(
    string AppId,
    string Name,
    IReadOnlyDictionary<string, string> LocalizedNames,
    string InstallDirectory,
    IReadOnlyDictionary<string, string> BranchBuildIds,
    IReadOnlyList<SteamDepotInfo> Depots,
    IReadOnlyList<SteamLaunchInfo> LaunchOptions);

public sealed record SteamPlatformFilter(string OperatingSystems, string Architecture, string Language);

public sealed record SteamDepotInfo(
    string Id, string Name, SteamPlatformFilter Filter, string DlcAppId,
    IReadOnlyDictionary<string, string> Manifests);

public sealed record SteamLaunchInfo(
    string Id, string Description, string Executable, string Arguments,
    string Type, SteamPlatformFilter Filter);

public sealed record SteamAppSelectionOptions(
    string OperatingSystem = "windows", string Architecture = "64",
    string Language = "schinese", string Branch = "public");

public sealed record SteamDepotCandidate(SteamDepotInfo Depot, string Manifest)
{
    public string DisplayText => $"{Depot.Id} · {(string.IsNullOrEmpty(Depot.Name) ? "未提供名称" : Depot.Name)}" +
        $" · {Depot.Filter.OperatingSystems} {Depot.Filter.Architecture} {Depot.Filter.Language}" +
        (string.IsNullOrEmpty(Manifest) ? " · 无可用 Manifest" : "");
}

public sealed record SteamExecutableCandidate(string Path, IReadOnlyList<SteamLaunchInfo> LaunchOptions)
{
    public string DisplayText => Path + " · " + string.Join(" / ", LaunchOptions.Select(l =>
        (string.IsNullOrEmpty(l.Description) ? $"启动项 {l.Id}" : l.Description) +
        (string.IsNullOrEmpty(l.Arguments) ? "" : $" ({l.Arguments})")));
}

public sealed record SteamAppSelection(
    string DisplayName, string InstallDirectory, string BuildId,
    IReadOnlyList<SteamDepotCandidate> Depots,
    IReadOnlyList<SteamExecutableCandidate> Executables,
    IReadOnlyList<string> Warnings);
