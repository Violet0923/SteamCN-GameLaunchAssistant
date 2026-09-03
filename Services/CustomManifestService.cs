using WetheringWavesSteamHelper_WinUI.Models;

namespace WetheringWavesSteamHelper_WinUI.Services;

/// <summary>
/// 集中管理自定义 Manifest 配置，确保侧边栏和编辑页使用同一份持久化数据。
/// </summary>
public sealed class CustomManifestService
{
    private readonly SettingsService _settingsService;

    public static CustomManifestService Instance { get; } = new(new SettingsService());

    /// <summary>导航结构变化时触发。参数是建议打开的配置 Id；null 表示只刷新列表并保留当前页面。</summary>
    public event Action<string?>? NavigationChanged;

    internal CustomManifestService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public IReadOnlyList<CustomManifestPreset> GetAll()
    {
        var settings = LoadMigratedSettings();
        return settings.CustomManifestPresets.Select(p => p.Clone()).ToList();
    }

    public IReadOnlyList<CustomManifestPreset> GetSidebarItems() =>
        GetAll().Where(p => !p.IsBuiltIn).ToList();

    /// <summary>旧版内置预设不计为已添加游戏；启动时优先恢复有效的自定义游戏。</summary>
    public string? GetInitialSidebarId()
    {
        var settings = LoadMigratedSettings();
        var games = settings.CustomManifestPresets.Where(p => !p.IsBuiltIn).ToList();
        return (games.FirstOrDefault(p => string.Equals(p.Id, settings.CurrentCustomManifestId,
            StringComparison.OrdinalIgnoreCase)) ?? games.FirstOrDefault())?.Id;
    }

    public string GetBuiltInId() =>
        GetAll().First(p => p.IsBuiltIn).Id;

    public CustomManifestPreset? GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var settings = LoadMigratedSettings();
        return settings.CustomManifestPresets.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))?.Clone();
    }

    public string GetCurrentId()
    {
        var settings = LoadMigratedSettings();
        return settings.CurrentCustomManifestId;
    }

    public bool NameExists(string name, string? excludeId = null)
    {
        var normalized = name.Trim();
        return GetAll().Any(p =>
            !string.Equals(p.Id, excludeId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Name, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public CustomManifestPreset? Create(string name, CustomManifestPreset? template = null)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || NameExists(normalized)) return null;

        var created = template?.Clone() ?? new CustomManifestPreset();
        created.Id = Guid.NewGuid().ToString("N");
        created.Name = normalized;
        created.IsBuiltIn = false;

        var saved = _settingsService.Update(settings =>
        {
            settings.EnsureCustomManifestPresets();
            settings.CustomManifestPresets.Add(created.Clone());
            SetCurrent(settings, created);
        });

        if (!saved) return null;
        NavigationChanged?.Invoke(created.Id);
        return created;
    }

    /// <summary>
    /// 按给定的完整 Id 列表调整侧边栏游戏顺序。列表必须与当前非内置游戏完全一致，
    /// 防止过期的拖放操作覆盖刚发生的新建或删除。
    /// </summary>
    public bool ReorderSidebarItems(IReadOnlyList<string> orderedIds)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        var requested = orderedIds.ToList();
        var accepted = false;
        var changed = false;
        var saved = _settingsService.Update(settings =>
        {
            settings.EnsureCustomManifestPresets();
            var current = settings.CustomManifestPresets.Where(p => !p.IsBuiltIn).ToList();
            if (requested.Count != current.Count
                || requested.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requested.Count
                || current.Any(p => !requested.Contains(p.Id, StringComparer.OrdinalIgnoreCase)))
                return;

            accepted = true;
            if (current.Select(p => p.Id).SequenceEqual(requested, StringComparer.OrdinalIgnoreCase))
                return;

            var byId = current.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<CustomManifestPreset>(requested.Select(id => byId[id]));
            for (var i = 0; i < settings.CustomManifestPresets.Count; i++)
            {
                if (!settings.CustomManifestPresets[i].IsBuiltIn)
                    settings.CustomManifestPresets[i] = queue.Dequeue();
            }
            changed = true;
        });

        if (saved && accepted && changed)
            NavigationChanged?.Invoke(null);
        return saved && accepted;
    }

    public bool Update(CustomManifestPreset preset)
    {
        var saved = false;
        var writeSucceeded = _settingsService.Update(settings =>
        {
            settings.EnsureCustomManifestPresets();
            var index = settings.CustomManifestPresets.FindIndex(p =>
                string.Equals(p.Id, preset.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;

            // 名称只通过 Rename 修改，防止表单保存意外覆盖侧边栏名称。
            var updated = preset.Clone();
            updated.Name = settings.CustomManifestPresets[index].Name;
            updated.IsBuiltIn = settings.CustomManifestPresets[index].IsBuiltIn;
            settings.CustomManifestPresets[index] = updated;
            // 只在该配置仍是当前导航项时更新兼容镜像。
            // 旧页的 Unloaded 可能晚于新页选中发生，不能把当前 Id 改回旧页。
            if (string.Equals(settings.CurrentCustomManifestId, updated.Id, StringComparison.OrdinalIgnoreCase))
                SetCurrent(settings, updated);
            saved = true;
        });
        return writeSucceeded && saved;
    }

    public bool Rename(string id, string name)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || NameExists(normalized, id)) return false;

        var renamed = false;
        var saved = _settingsService.Update(settings =>
        {
            settings.EnsureCustomManifestPresets();
            var preset = settings.CustomManifestPresets.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (preset == null || preset.IsBuiltIn) return;
            preset.Name = normalized;
            SetCurrent(settings, preset);
            renamed = true;
        });

        if (!saved || !renamed) return false;
        NavigationChanged?.Invoke(id);
        return true;
    }

    public string? Delete(string id)
    {
        string? nextId = null;
        var deleted = false;
        var saved = _settingsService.Update(settings =>
        {
            settings.EnsureCustomManifestPresets();
            var index = settings.CustomManifestPresets.FindIndex(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || settings.CustomManifestPresets[index].IsBuiltIn) return;

            settings.CustomManifestPresets.RemoveAt(index);
            // 优先选中相邻的用户游戏；删到零个游戏时只保留兼容预设，由主窗口展示空列表页。
            var next = settings.CustomManifestPresets.Skip(index).FirstOrDefault(p => !p.IsBuiltIn)
                ?? settings.CustomManifestPresets.LastOrDefault(p => !p.IsBuiltIn)
                ?? settings.CustomManifestPresets.First(p => p.IsBuiltIn);
            nextId = next.Id;
            SetCurrent(settings, next);
            deleted = true;
        });

        if (!saved || !deleted) return null;
        NavigationChanged?.Invoke(nextId);
        return nextId;
    }

    public void Select(string id, bool notifyNavigation = false)
    {
        var selected = false;
        _settingsService.Update(settings =>
        {
            settings.EnsureCustomManifestPresets();
            var preset = settings.CustomManifestPresets.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                SetCurrent(settings, preset);
                selected = true;
            }
        });

        if (selected && notifyNavigation)
            NavigationChanged?.Invoke(id);
    }

    private AppSettings LoadMigratedSettings()
    {
        var settings = _settingsService.Load();
        if (settings.EnsureCustomManifestPresets())
        {
            // 迁移落盘也使用原子更新，避免极端情况下覆盖其他页的刚写入数据。
            _settingsService.Update(latest => latest.EnsureCustomManifestPresets());
            settings = _settingsService.Load();
        }
        return settings;
    }

    private static void SetCurrent(AppSettings settings, CustomManifestPreset preset)
    {
        settings.CurrentCustomManifestId = preset.Id;
        settings.CurrentCustomManifestName = preset.Name;
        settings.CurrentCustomManifest = preset.Clone();
    }
}
