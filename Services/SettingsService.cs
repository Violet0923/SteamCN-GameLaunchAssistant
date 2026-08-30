using System.Text.Json;
using WetheringWavesSteamHelper_WinUI.Models;

namespace WetheringWavesSteamHelper_WinUI.Services;

public class SettingsService
{
    private static readonly object SyncRoot = new();
    private static readonly string DefaultSettingsPath = Path.Combine(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "WutheringWavesSteamHelper"),
        "settings.json");
    private readonly string _settingsPath;

    public SettingsService() : this(DefaultSettingsPath) { }

    internal SettingsService(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = settingsPath;
    }

    public AppSettings Load()
    {
        lock (SyncRoot)
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }
    }

    public bool Save(AppSettings settings)
    {
        lock (SyncRoot)
        {
            return SaveCore(settings);
        }
    }

    /// <summary>在同一把锁内重新读取并更新设置，避免不同页面用旧快照相互覆盖。</summary>
    public bool Update(Action<AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (SyncRoot)
        {
            var settings = LoadCore();
            update(settings);
            return SaveCore(settings);
        }
    }

    private AppSettings LoadCore()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    private bool SaveCore(AppSettings settings)
    {
        try
        {
            var settingsDir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(settingsDir))
                Directory.CreateDirectory(settingsDir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
