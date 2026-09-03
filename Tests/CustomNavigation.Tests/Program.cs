using WetheringWavesSteamHelper_WinUI.Models;
using WetheringWavesSteamHelper_WinUI.Services;

// All mutations use a uniquely named test file; never load the user's actual settings.
var settingsPath = Path.Combine(AppContext.BaseDirectory, $"navigation-{Guid.NewGuid():N}.json");
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}
try
{
    var store = new SettingsService(settingsPath);
    var service = new CustomManifestService(store);
    Check(service.GetInitialSidebarId() == null && service.GetSidebarItems().Count == 0,
        "fresh install has no default game");
    var builtInId = service.GetBuiltInId();
    Check(service.GetById(builtInId) != null, "legacy built-in preset retained without counting as a game");
    var first = service.Create("First game")!;
    Check(service.GetInitialSidebarId() == first.Id, "adding first game replaces empty state target");
    var second = service.Create("Second game")!;
    var third = service.Create("Third game")!;
    service.Select(second.Id);
    Check(new CustomManifestService(store).GetInitialSidebarId() == second.Id,
        "startup restores previously selected user game");
    Check(service.ReorderSidebarItems(new[] { third.Id, first.Id, second.Id })
        && service.GetSidebarItems().Select(p => p.Id).SequenceEqual(new[] { third.Id, first.Id, second.Id })
        && service.GetInitialSidebarId() == second.Id, "drag order persists without changing current game");
    Check(!service.ReorderSidebarItems(new[] { first.Id, first.Id, second.Id })
        && service.GetSidebarItems().Select(p => p.Id).SequenceEqual(new[] { third.Id, first.Id, second.Id }),
        "stale or duplicate drag order is rejected without data loss");
    service.Select(builtInId);
    Check(service.GetInitialSidebarId() == third.Id, "legacy selected preset falls back to first user game");
    Check(service.Delete(second.Id) == first.Id, "deleting last list entry selects previous user game");
    Check(service.Delete(third.Id) == first.Id, "deleting first list entry selects next user game");
    Check(service.Delete(first.Id) == builtInId && service.GetInitialSidebarId() == null
        && service.GetSidebarItems().Count == 0, "deleting last game returns to empty state");
    var readded = service.Create("Added again")!;
    Check(service.GetInitialSidebarId() == readded.Id, "game can be added after deleting all games");
    store.Save(new AppSettings
    {
        CurrentCustomManifest = new CustomManifestPreset { AppId = "123", GameDisplayName = "Legacy data" }
    });
    Check(service.GetInitialSidebarId() == null && service.GetAll().Any(p => p.AppId == "123"),
        "old settings migration preserves data without introducing a default sidebar game");
    Console.WriteLine($"All {checks} checks passed.");
}
finally
{
    if (File.Exists(settingsPath)) File.Delete(settingsPath);
}
