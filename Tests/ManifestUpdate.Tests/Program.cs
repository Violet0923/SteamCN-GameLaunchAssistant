using WetheringWavesSteamHelper_WinUI.Models;
using WetheringWavesSteamHelper_WinUI.Services;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}

SteamDepotCandidate Depot(string id, string manifest) => new(
    new SteamDepotInfo(id, id, new("windows", "64", "schinese"), "0",
        new Dictionary<string, string> { ["public"] = manifest }), manifest);
SteamExecutableCandidate Exe(string path) => new(path,
    new[] { new SteamLaunchInfo("0", "", path, "", "default", new("windows", "64", "")) });
SteamAppSelection Selection(IReadOnlyList<SteamDepotCandidate> depots,
    IReadOnlyList<SteamExecutableCandidate>? executables = null) =>
    new("Game", "Game", "10", depots, executables ?? new[] { Exe("Game.exe") }, Array.Empty<string>());

var currentDepot = SteamAppSelectionService.ChooseDepotForAutomaticUpdate(
    Selection(new[] { Depot("1", "11"), Depot("2", "22") }), "2");
Check(currentDepot.Candidate?.Depot.Id == "2", "current depot wins when its manifest remains usable");
var soleManifest = SteamAppSelectionService.ChooseDepotForAutomaticUpdate(
    Selection(new[] { Depot("1", ""), Depot("2", "22") }), "1");
Check(soleManifest.Candidate?.Depot.Id == "2", "sole depot with manifest is selected automatically");
var ambiguous = SteamAppSelectionService.ChooseDepotForAutomaticUpdate(
    Selection(new[] { Depot("1", "11"), Depot("2", "22") }), "3");
Check(!ambiguous.IsSuccess && ambiguous.Error.Contains("2 个"), "ambiguous usable depots are not guessed");
var noManifest = SteamAppSelectionService.ChooseDepotForAutomaticUpdate(
    Selection(new[] { Depot("1", ""), Depot("2", "") }), "1");
Check(!noManifest.IsSuccess && noManifest.Error.Contains("没有找到"), "depots without manifests cannot be selected");
Check(SteamAppSelectionService.ChooseExecutableForAutomaticUpdate(
    Selection(new[] { Depot("1", "11") }, new[] { Exe("A.exe"), Exe("B.exe") }), "b.EXE").Candidate?.Path == "B.exe",
    "current executable is matched case-insensitively");

var root = Path.Combine(Path.GetTempPath(), "manifest-update-" + Guid.NewGuid().ToString("N"));
var target = Path.Combine(root, "library", "steamapps", "appmanifest_123.acf");
var backups = Path.Combine(root, "backups");
var now = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
Check(ManifestFileService.DefaultBackupRoot == Path.Combine(AppContext.BaseDirectory, "backups"),
    "default backup root is the application installation directory");
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    await File.WriteAllTextAsync(target, "version-0");
    var service = new ManifestFileService(backups, ManifestBackupOptions.Default, () => now);
    for (var version = 1; version <= 3; version++)
    {
        now = now.AddMinutes(1);
        var result = await service.WriteWithBackupAsync(target, $"version-{version}", "123", (version - 1).ToString());
        Check(result.Changed && result.BackupPath != null, $"update {version} creates a backup before replacement");
    }
    var retained = Directory.GetFiles(Path.Combine(backups, "123"), "*.acf");
    Check(retained.Length == 2, "only the two newest backups are retained per AppID");
    Check(retained.Select(File.ReadAllText).Order().SequenceEqual(new[] { "version-1", "version-2" }),
        "the retained files are the two latest previous versions");

    var identical = await service.WriteWithBackupAsync(target, "version-3", "123", "3");
    Check(!identical.Changed && identical.BackupPath == null
        && Directory.GetFiles(Path.Combine(backups, "123"), "*.acf").Length == 2,
        "identical content creates no backup and no write");

    var stale = retained[0];
    File.SetLastWriteTimeUtc(stale, now.AddDays(-31).UtcDateTime);
    Check(service.CleanupBackups("123") == 1
        && Directory.GetFiles(Path.Combine(backups, "123"), "*.acf").Length == 1,
        "backups older than 30 days are removed");

    var otherTarget = Path.Combine(root, "library", "steamapps", "appmanifest_456.acf");
    await File.WriteAllTextAsync(otherTarget, "other-0");
    now = now.AddMinutes(1);
    await service.WriteWithBackupAsync(otherTarget, "other-1", "456", "0");
    Check(Directory.GetFiles(Path.Combine(backups, "456"), "*.acf").Length == 1,
        "backup retention is isolated per AppID");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

Console.WriteLine($"All {checks} checks passed.");
