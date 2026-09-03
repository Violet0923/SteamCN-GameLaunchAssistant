using WetheringWavesSteamHelper_WinUI.Models;

namespace WetheringWavesSteamHelper_WinUI.Services;

/// <summary>Pure selection policy, independent of HTTP and WinUI. No game-specific IDs or filenames.</summary>
public static class SteamAppSelectionService
{
    public static SteamAppSelection Select(SteamAppInfo app, SteamAppSelectionOptions options)
    {
        var warnings = new List<string>();
        var name = app.LocalizedNames.TryGetValue(options.Language, out var localized) && !string.IsNullOrWhiteSpace(localized)
            ? localized : app.Name;
        // ACF stores quoted strings; do not inject unescaped control characters from upstream metadata.
        if (name.Any(c => c < 32 || c is '"' or '\\')) name = "";
        var directory = SteamPathValidator.TryValidate(app.InstallDirectory, out _) ? app.InstallDirectory : "";
        var build = app.BranchBuildIds.TryGetValue(options.Branch, out var buildId) && IsId(buildId) ? buildId : "";
        var depots = app.Depots.Where(d => Matches(d.Filter, options) && (string.IsNullOrEmpty(d.DlcAppId) || d.DlcAppId == "0"))
            .Select(d => new SteamDepotCandidate(d,
                d.Manifests.TryGetValue(options.Branch, out var manifest) && IsId(manifest) ? manifest : ""))
            .ToList();
        if (depots.Count > 1)
            warnings.Add("检测到多个 Depot，它们可能需要共同使用。当前配置只支持一个 Depot，请核对后选择；选择一个不代表完整安装配置。");

        var matchingLaunches = app.LaunchOptions.Where(l => Matches(l.Filter, options)).ToList();
        var supported = matchingLaunches.Where(l => SteamPathValidator.TryValidate(l.Executable, out _, requireExe: true)).ToList();
        if (supported.Count != matchingLaunches.Count)
            warnings.Add("部分启动项不是受支持的 EXE 相对路径，已跳过，请按需手动核对。");
        var executables = supported.GroupBy(l => SteamPathValidator.Normalize(l.Executable), StringComparer.OrdinalIgnoreCase)
            .Select(g => new SteamExecutableCandidate(g.Key, g.ToList())).ToList();
        return new(name, directory, build, depots, executables, warnings);
    }

    private static bool IsId(string value) => ulong.TryParse(value,
        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0;

    private static bool Matches(SteamPlatformFilter filter, SteamAppSelectionOptions options) =>
        Allows(filter.OperatingSystems, options.OperatingSystem) && MatchesArchitecture(filter.Architecture, options)
        && Allows(filter.Language, options.Language);

    private static bool MatchesArchitecture(string restriction, SteamAppSelectionOptions options) =>
        Allows(restriction, options.Architecture)
        // 64-bit Windows can also run 32-bit launchers; keep both as candidates instead of guessing.
        || (options.OperatingSystem == "windows" && options.Architecture == "64" && Allows(restriction, "32"));

    private static bool Allows(string restriction, string target) => string.IsNullOrWhiteSpace(restriction)
        || restriction.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(target, StringComparer.OrdinalIgnoreCase);
}
