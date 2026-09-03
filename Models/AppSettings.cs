using System.Collections.Generic;

namespace WetheringWavesSteamHelper_WinUI.Models;

public class AppSettings
{
    // ===== 全局通用 Steam 配置（v2.2.0 起由设置页统一管理） =====
    public string SteamInstallPath { get; set; } = "";
    public string SteamLibraryPath { get; set; } = "";
    public string SteamId { get; set; } = "";

    // ===== 鸣潮专用 =====
    public string BuildId { get; set; } = "";
    public string Manifest { get; set; } = "";
    public string CnGameSource { get; set; } = "official";

    // ===== 自定义 Manifest（v2.2.0 新增；v2.3.0 起 CustomManifestPresets 升为唯一数据源） =====
    public CustomManifestPreset CurrentCustomManifest { get; set; } = new(); // 镜像选中预设，保向后兼容
    public List<CustomManifestPreset> CustomManifestPresets { get; set; } = new();
    public string CurrentCustomManifestName { get; set; } = ""; // v2.3.0 新增：记住上次选中的预设名
    public string CurrentCustomManifestId { get; set; } = "";   // 稳定的导航标识，名称可以独立修改

    // ===== 应用设置 =====
    public bool DeveloperMode { get; set; } = false;
    public bool DebugMode { get; set; } = false;
    public bool BetaChannel { get; set; } = false;
    public string Language { get; set; } = "zh-CN";

    /// <summary>
    /// 幂等地确保 <see cref="CustomManifestPresets"/> 至少有一个预设（v2.3.0 迁移逻辑）。
    /// 旧 v2.2.0 settings.json 无预设列表时：若 CurrentCustomManifest 含任一非默认字段，
    /// 则把它纳入列表；否则补一个空白「默认」预设。
    /// </summary>
    public bool EnsureCustomManifestPresets()
    {
        var changed = false;
        CustomManifestPresets ??= new List<CustomManifestPreset>();

        if (CustomManifestPresets.Count == 0)
        {
            var current = CurrentCustomManifest ?? new CustomManifestPreset();
            var hasData = !string.IsNullOrWhiteSpace(current.AppId)
                       || !string.IsNullOrWhiteSpace(current.DepotId)
                       || !string.IsNullOrWhiteSpace(current.BuildId)
                       || !string.IsNullOrWhiteSpace(current.Manifest)
                       || !string.IsNullOrWhiteSpace(current.GameDisplayName)
                       || !string.IsNullOrWhiteSpace(current.InstallDir)
                       || !string.IsNullOrWhiteSpace(current.ClientExePath)
                       || !string.IsNullOrWhiteSpace(current.LauncherExePath);

            if (hasData)
            {
                var migrated = current.Clone();
                migrated.Name = string.IsNullOrWhiteSpace(migrated.Name) ? "默认" : migrated.Name;
                CustomManifestPresets.Add(migrated);
            }
            else
            {
                CustomManifestPresets.Add(new CustomManifestPreset { Name = "默认" });
            }
            changed = true;
        }

        // 旧版配置没有 Id；同时修复罕见的重复 Id，确保导航键唯一。
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in CustomManifestPresets)
        {
            if (string.IsNullOrWhiteSpace(preset.Id) || !ids.Add(preset.Id))
            {
                do preset.Id = Guid.NewGuid().ToString("N");
                while (!ids.Add(preset.Id));
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(preset.Name))
            {
                preset.Name = "未命名自定义";
                changed = true;
            }
        }

        // 保留旧版内置「自定义」预设，不显示在动态侧边栏，仍可从页面预设下拉框访问。
        // 优先接管旧版的「默认」预设，以保留其中的全部数据。
        var builtIns = CustomManifestPresets.Where(p => p.IsBuiltIn).ToList();
        CustomManifestPreset builtIn;
        if (builtIns.Count == 0)
        {
            builtIn = CustomManifestPresets.FirstOrDefault(p =>
                string.Equals(p.Name, "默认", StringComparison.OrdinalIgnoreCase))
                ?? new CustomManifestPreset();
            if (!CustomManifestPresets.Contains(builtIn))
                CustomManifestPresets.Insert(0, builtIn);
            builtIn.IsBuiltIn = true;
            changed = true;
        }
        else
        {
            builtIn = builtIns[0];
            foreach (var duplicate in builtIns.Skip(1))
            {
                duplicate.IsBuiltIn = false;
                changed = true;
            }
        }

        if (!string.Equals(builtIn.Name, "自定义", StringComparison.Ordinal))
        {
            builtIn.Name = "自定义";
            changed = true;
        }

        var selected = CustomManifestPresets.FirstOrDefault(p =>
            string.Equals(p.Id, CurrentCustomManifestId, StringComparison.OrdinalIgnoreCase));
        selected ??= CustomManifestPresets.FirstOrDefault(p =>
            string.Equals(p.Name, CurrentCustomManifestName, StringComparison.OrdinalIgnoreCase));
        if (selected == null && string.Equals(CurrentCustomManifestName, "默认", StringComparison.OrdinalIgnoreCase))
            selected = builtIn;
        selected ??= CustomManifestPresets[0];

        if (!string.Equals(CurrentCustomManifestId, selected.Id, StringComparison.OrdinalIgnoreCase))
        {
            CurrentCustomManifestId = selected.Id;
            changed = true;
        }
        if (!string.Equals(CurrentCustomManifestName, selected.Name, StringComparison.Ordinal))
        {
            CurrentCustomManifestName = selected.Name;
            changed = true;
        }

        if (changed)
            CurrentCustomManifest = selected.Clone();

        return changed;
    }
}

public class CustomManifestPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "默认";
    public bool IsBuiltIn { get; set; } = false;
    public string AppId { get; set; } = "";
    public string DepotId { get; set; } = "";
    public string BuildId { get; set; } = "";
    public string Manifest { get; set; } = "";
    public string GameDisplayName { get; set; } = "";
    public string InstallDir { get; set; } = "";
    public string ClientExePath { get; set; } = "";        // 真实游戏 exe 完整路径（用于复制启动命令）
    public string LauncherExePath { get; set; } = "";      // 游戏启动器 exe 完整路径（用于直接打开启动器）
    public string ExecutableFileName { get; set; } = "";   // v2.3.0 新增：Steam 占位 exe 文件名
    public string Language { get; set; } = "schinese";

    public CustomManifestPreset Clone() => new()
    {
        Id = Id,
        Name = Name,
        IsBuiltIn = IsBuiltIn,
        AppId = AppId,
        DepotId = DepotId,
        BuildId = BuildId,
        Manifest = Manifest,
        GameDisplayName = GameDisplayName,
        InstallDir = InstallDir,
        ClientExePath = ClientExePath,
        LauncherExePath = LauncherExePath,
        ExecutableFileName = ExecutableFileName,
        Language = Language,
    };
}
