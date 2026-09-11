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

    /// <summary>
    /// 一键更新时优先沿用当前仍有 Manifest 的 Depot；否则只在唯一有效候选时自动切换。
    /// 多个有效候选保持歧义，不依赖上游返回顺序猜测。
    /// </summary>
    public static AutomaticCandidateResult<SteamDepotCandidate> ChooseDepotForAutomaticUpdate(
        SteamAppSelection selection, string currentDepotId)
    {
        var usable = selection.Depots.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Manifest)).ToList();
        var current = usable.FirstOrDefault(candidate =>
            string.Equals(candidate.Depot.Id, currentDepotId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (current != null) return AutomaticCandidateResult<SteamDepotCandidate>.Success(current);
        return usable.Count switch
        {
            1 => AutomaticCandidateResult<SteamDepotCandidate>.Success(usable[0]),
            0 => AutomaticCandidateResult<SteamDepotCandidate>.Failure("没有找到带有可用 Manifest 的 Depot。"),
            _ => AutomaticCandidateResult<SteamDepotCandidate>.Failure(
                $"找到 {usable.Count} 个带有可用 Manifest 的 Depot，且当前 Depot 不在其中，请先手动选择。")
        };
    }

    /// <summary>占位 EXE 同样优先沿用当前有效值，否则只自动接受唯一候选。</summary>
    public static AutomaticCandidateResult<SteamExecutableCandidate> ChooseExecutableForAutomaticUpdate(
        SteamAppSelection selection, string currentExecutable)
    {
        var normalized = SteamPathValidator.TryValidate(currentExecutable.Trim(), out _, requireExe: true)
            ? SteamPathValidator.Normalize(currentExecutable.Trim()) : "";
        var current = selection.Executables.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, normalized, StringComparison.OrdinalIgnoreCase));
        if (current != null) return AutomaticCandidateResult<SteamExecutableCandidate>.Success(current);
        return selection.Executables.Count switch
        {
            1 => AutomaticCandidateResult<SteamExecutableCandidate>.Success(selection.Executables[0]),
            0 => AutomaticCandidateResult<SteamExecutableCandidate>.Failure("没有找到可用的 Steam 启动 EXE。"),
            _ => AutomaticCandidateResult<SteamExecutableCandidate>.Failure(
                $"找到 {selection.Executables.Count} 个 Steam 启动 EXE，且当前值不在其中，请先手动选择。")
        };
    }
}

public sealed record AutomaticCandidateResult<T>(T? Candidate, string Error) where T : class
{
    public bool IsSuccess => Candidate != null;
    public static AutomaticCandidateResult<T> Success(T candidate) => new(candidate, "");
    public static AutomaticCandidateResult<T> Failure(string error) => new(null, error);
}
