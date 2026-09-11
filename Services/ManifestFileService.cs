using System.Text;

namespace WetheringWavesSteamHelper_WinUI.Services;

public sealed record ManifestBackupOptions(int MaximumCount, TimeSpan MaximumAge)
{
    public static ManifestBackupOptions Default { get; } = new(2, TimeSpan.FromDays(30));
}

public sealed record ManifestWriteResult(bool Changed, string? BackupPath, int RemovedBackupCount);

/// <summary>负责 ACF 的比较、滚动备份和同目录原子替换，与页面及网络请求解耦。</summary>
public sealed class ManifestFileService
{
    // 备份放在安装目录的独立子目录中，便于安装包统一授予写权限和用户手动恢复。
    public static string DefaultBackupRoot => Path.Combine(AppContext.BaseDirectory, "backups");

    private readonly string _backupRoot;
    private readonly ManifestBackupOptions _options;
    private readonly Func<DateTimeOffset> _utcNow;

    public ManifestFileService(string? backupRoot = null, ManifestBackupOptions? options = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _backupRoot = backupRoot ?? DefaultBackupRoot;
        _options = options ?? ManifestBackupOptions.Default;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        if (_options.MaximumCount < 0) throw new ArgumentOutOfRangeException(nameof(options));
        if (_options.MaximumAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
    }

    public async Task<ManifestWriteResult> WriteWithBackupAsync(string targetPath, string content,
        string appId, string previousBuildId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (!SteamAppInfoService.TryNormalizeAppId(appId, out var normalizedAppId))
            throw new ArgumentException("AppID 必须为有效的正整数。", nameof(appId));

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException("配置文件路径缺少目录。", nameof(targetPath));
        Directory.CreateDirectory(targetDirectory);

        if (File.Exists(targetPath))
        {
            var current = await File.ReadAllTextAsync(targetPath, cancellationToken);
            if (string.Equals(current, content, StringComparison.Ordinal))
            {
                // 内容未变化时不制造重复备份，但仍执行保留策略，清理历史遗留文件。
                var removed = CleanupBackups(normalizedAppId);
                return new(false, null, removed);
            }
        }

        string? backupPath = null;
        var now = _utcNow();
        if (File.Exists(targetPath))
        {
            var backupDirectory = Path.Combine(_backupRoot, normalizedAppId);
            Directory.CreateDirectory(backupDirectory);
            var build = ulong.TryParse(previousBuildId, out _) ? previousBuildId : "unknown";
            var baseName = $"appmanifest_{normalizedAppId}_build{build}_{now:yyyyMMdd-HHmmssfff}";
            backupPath = Path.Combine(backupDirectory, baseName + ".acf");
            for (var suffix = 1; File.Exists(backupPath); suffix++)
                backupPath = Path.Combine(backupDirectory, $"{baseName}-{suffix}.acf");
            File.Copy(targetPath, backupPath, overwrite: false);
            File.SetCreationTimeUtc(backupPath, now.UtcDateTime);
            File.SetLastWriteTimeUtc(backupPath, now.UtcDateTime);
        }

        var temporaryPath = Path.Combine(targetDirectory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            // 临时文件与目标文件位于同一目录，写入完成后再替换，避免留下半写入的 ACF。
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }

        return new(true, backupPath, CleanupBackups(normalizedAppId));
    }

    internal int CleanupBackups(string appId)
    {
        var directory = Path.Combine(_backupRoot, appId);
        if (!Directory.Exists(directory)) return 0;
        var removed = 0;
        var cutoff = _utcNow().UtcDateTime - _options.MaximumAge;
        var files = Directory.EnumerateFiles(directory, $"appmanifest_{appId}_*.acf")
            .Select(path => new FileInfo(path)).ToList();
        // 先按最长保留天数清理，再按修改时间保留最新数量；两个条件同时生效。
        foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff))
            if (TryDelete(file)) removed++;
        files = Directory.EnumerateFiles(directory, $"appmanifest_{appId}_*.acf")
            .Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal).ToList();
        foreach (var file in files.Skip(_options.MaximumCount))
            if (TryDelete(file)) removed++;
        return removed;
    }

    private static bool TryDelete(FileInfo file)
    {
        try { file.Delete(); return true; }
        catch { return false; }
    }
}
