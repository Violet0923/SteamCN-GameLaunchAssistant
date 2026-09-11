using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WetheringWavesSteamHelper_WinUI.Models;
using WetheringWavesSteamHelper_WinUI.Services;

namespace WetheringWavesSteamHelper_WinUI.Views.Pages;

public sealed partial class CustomManifestPage
{
    private readonly ManifestFileService _manifestFileService = new();

    private async void OneClickUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!SteamAppInfoService.TryNormalizeAppId(txtAppId.Text, out var appId))
        {
            await ShowInfoAsync("请先填写有效的正整数 AppID。");
            return;
        }

        var disk = _settingsService.Load();
        _settings.SteamInstallPath = disk.SteamInstallPath;
        _settings.SteamLibraryPath = disk.SteamLibraryPath;
        _settings.SteamId = disk.SteamId;
        UpdateGlobalConfigInfoBar();
        if (string.IsNullOrWhiteSpace(_settings.SteamInstallPath)
            || string.IsNullOrWhiteSpace(_settings.SteamLibraryPath)
            || string.IsNullOrWhiteSpace(_settings.SteamId))
        {
            await ShowInfoAsync("请先在「设置」中填写 Steam 安装路径、SteamLibrary 路径和 SteamID64。");
            return;
        }

        ResetGameInfoLookup();
        UpdateGameInfoLinks();
        var request = new CancellationTokenSource();
        _gameInfoRequest = request;
        var presetId = PresetId;
        var originalDepotId = txtDepotId.Text.Trim();
        var originalExecutable = txtExecutableFileName.Text.Trim();
        var originalBuildId = txtBuildId.Text.Trim();
        // 网络请求期间用户仍可能切换预设或编辑字段；保存快照用于拒绝过期结果，
        // 避免一次较慢的响应覆盖用户刚刚输入的新配置。
        var inputSnapshot = GameInfoTextBoxes.ToDictionary(box => box, box => box.Text);
        var options = new SteamAppSelectionOptions(
            Architecture: Environment.Is64BitOperatingSystem ? "64" : "32", Language: SelectedGameLanguage);

        btnOneClickUpdate.IsEnabled = false;
        btnOneClickUpdate.Content = "正在更新…";
        btnFetchGameInfo.IsEnabled = false;
        ShowGameInfoStatus("正在一键更新配置", "正在获取并验证最新游戏信息，请稍候。",
            InfoBarSeverity.Informational);
        _logService.AddLog($"[一键更新] 正在获取 AppID={appId} 的最新游戏信息");

        try
        {
            var app = await _appInfoService.GetAppInfoAsync(appId, request.Token);
            if (_gameInfoRequest != request || request.IsCancellationRequested || PresetId != presetId
                || !SteamAppInfoService.TryNormalizeAppId(txtAppId.Text, out var currentId) || currentId != appId)
                return;
            if (SelectedGameLanguage != options.Language || inputSnapshot.Any(pair => pair.Key.Text != pair.Value))
            {
                ShowGameInfoStatus("更新已取消", "输入已修改，已丢弃过期结果；原配置文件没有变化。");
                return;
            }

            var selection = SteamAppSelectionService.Select(app, options);
            // 自动更新仅接受可明确确定的候选。存在多个有效 Depot/EXE 时停止写入，
            // 让用户先在页面中完成选择，避免依赖接口返回顺序猜测目标文件。
            var depotChoice = SteamAppSelectionService.ChooseDepotForAutomaticUpdate(selection, originalDepotId);
            var executableChoice = SteamAppSelectionService.ChooseExecutableForAutomaticUpdate(selection, originalExecutable);

            _applyingGameInfo = true;
            try
            {
                _gameInfoSelection = selection;
                cmbDepotCandidates.ItemsSource = selection.Depots;
                cmbDepotCandidates.Visibility = selection.Depots.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                cmbExecutableCandidates.ItemsSource = selection.Executables;
                cmbExecutableCandidates.Visibility = selection.Executables.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                txtAppId.Text = app.AppId;
                FillGameInfoField(txtDisplayName, selection.DisplayName, "显示名称");
                FillGameInfoField(txtInstallDir, selection.InstallDirectory, "安装目录名");
                FillGameInfoField(txtBuildId, selection.BuildId, "BuildID");
                if (depotChoice.Candidate != null)
                {
                    ApplyDepotCandidate(depotChoice.Candidate);
                    cmbDepotCandidates.SelectedItem = depotChoice.Candidate;
                }
                if (executableChoice.Candidate != null)
                {
                    FillGameInfoField(txtExecutableFileName, executableChoice.Candidate.Path, "Steam 占位文件名");
                    cmbExecutableCandidates.SelectedItem = executableChoice.Candidate;
                }
            }
            finally { EndGameInfoUpdate(); }

            var candidateErrors = new[]
                {
                    depotChoice.Error,
                    executableChoice.Error,
                    string.IsNullOrWhiteSpace(selection.DisplayName) ? "没有获取到合法的显示名称。" : "",
                    string.IsNullOrWhiteSpace(selection.InstallDirectory) ? "没有获取到合法的安装目录名。" : "",
                    string.IsNullOrWhiteSpace(selection.BuildId) ? "public 分支没有可用 BuildID。" : ""
                }
                .Where(message => !string.IsNullOrWhiteSpace(message)).ToList();
            if (candidateErrors.Count > 0)
            {
                var message = string.Join("\n", candidateErrors)
                    + "\n已获取其他字段，但没有覆盖配置文件。请完成选择后重新点击一键更新。";
                ShowGameInfoStatus("无法自动确定候选", message);
                _logService.AddLog("[一键更新] 已停止：" + string.Join("；", candidateErrors));
                return;
            }

            RefreshGameInfoStatus();
            if (!ValidateForGenerate())
            {
                await ShowInfoAsync("已获取游戏信息，但字段校验未通过。请查看运行日志，修正后重试；原配置文件没有变化。");
                return;
            }

            var executablePath = executableChoice.Candidate!.Path;
            if (!TryValidatePlaceholderExeName(executablePath, out var executableError))
            {
                await ShowInfoAsync($"Steam 占位文件名不合法：{executableError}\n原配置文件没有变化。");
                return;
            }
            executablePath = Path.Combine(executablePath.Split('/', '\\'));

            var libraryPath = _settings.SteamLibraryPath.Trim();
            var steamappsPath = Path.Combine(libraryPath, "steamapps");
            var acfPath = Path.Combine(steamappsPath, $"appmanifest_{appId}.acf");
            var acfContent = _steamService.GenerateAcfContent(new AcfParameters(
                AppId: appId,
                DepotId: depotChoice.Candidate!.Depot.Id,
                LauncherPath: Path.Combine(_settings.SteamInstallPath.Trim(), "steam.exe"),
                DisplayName: txtDisplayName.Text.Trim(),
                InstallDir: txtInstallDir.Text.Trim(),
                BuildId: txtBuildId.Text.Trim(),
                LastOwner: _settings.SteamId.Trim(),
                Manifest: depotChoice.Candidate.Manifest,
                Language: SelectedGameLanguage));

            // 先验证并创建占位 EXE，再替换 ACF。若占位文件处理失败，现有 Steam
            // 配置和备份历史都不会发生变化。
            var placeholder = _steamService.EnsureGameDirAndPlaceholder(
                libraryPath, txtInstallDir.Text.Trim(), executablePath);
            var write = await _manifestFileService.WriteWithBackupAsync(
                acfPath, acfContent, appId, originalBuildId, request.Token);
            if (!PersistCurrentPreset())
                throw new IOException("配置文件已更新，但助手预设保存失败。请检查设置目录权限。");

            if (write.BackupPath != null) _logService.AddLog($"[一键更新] 已备份原 ACF：{write.BackupPath}");
            if (write.RemovedBackupCount > 0)
                _logService.AddLog($"[一键更新] 已清理 {write.RemovedBackupCount} 份过期或超量备份");
            _logService.AddLog(write.Changed
                ? $"[一键更新][完成] 已原子覆盖：{acfPath}"
                : "[一键更新][完成] ACF 内容没有变化，未覆盖且未创建备份");

            var backupMessage = write.BackupPath == null
                ? (write.Changed ? "原文件不存在，无需备份。" : "内容没有变化，未生成备份。")
                : $"原配置已备份到：\n{write.BackupPath}";
            var placeholderMessage = placeholder.ExeCreated ? "已创建占位 EXE。" : "占位 EXE 已存在，未覆盖。";
            ShowGameInfoStatus("一键更新完成",
                $"DepotID：{depotChoice.Candidate.Depot.Id}\nBuildID：{selection.BuildId}\nManifest：{depotChoice.Candidate.Manifest}\n{backupMessage}\n{placeholderMessage}\n请完全重启 Steam。",
                InfoBarSeverity.Success);
            await ShowInfoAsync(string.Join("\n", new[]
            {
                "配置更新完成。",
                "",
                backupMessage,
                placeholderMessage,
                "每个 AppID 最多保留 2 份。",
                "备份最长保留 30 天。",
                "请完全重启 Steam。"
            }));
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_gameInfoRequest != request) return;
            var message = ex is SteamAppInfoException ? ex.Message : $"更新过程中出现错误：{ex.Message}";
            ShowGameInfoStatus("一键更新失败", message + " 请查看运行日志；若 ACF 已完成替换，原文件备份仍会保留。", InfoBarSeverity.Error);
            _logService.AddLog($"[一键更新][错误] {ex}");
            await ShowInfoAsync(message);
        }
        finally
        {
            if (_gameInfoRequest == request)
            {
                _gameInfoRequest = null;
                btnFetchGameInfo.IsEnabled = true;
            }
            btnOneClickUpdate.IsEnabled = true;
            btnOneClickUpdate.Content = "一键更新";
            request.Dispose();
        }
    }
}
