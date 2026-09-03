using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamCNGameLaunchAssistant.Models;
using SteamCNGameLaunchAssistant.Services;

namespace SteamCNGameLaunchAssistant.Views.Pages;

public sealed partial class CustomManifestPage
{
    private readonly ISteamAppInfoService _appInfoService = new SteamAppInfoService();
    private CancellationTokenSource? _gameInfoRequest;
    private SteamAppSelection? _gameInfoSelection;
    private bool _applyingGameInfo;
    private readonly Dictionary<string, string> _gameInfoIssues = new();
    private readonly Dictionary<TextBox, string> _observedGameInfoText = new();

    private IEnumerable<TextBox> GameInfoTextBoxes => new[] { txtAppId, txtDepotId, txtDisplayName,
        txtInstallDir, txtExecutableFileName, txtBuildId, txtManifest };

    private string SelectedGameLanguage => (cmbLanguageCode.SelectedItem as ComboBoxItem)?.Tag as string ?? "schinese";

    private void InitializeGameInfoLookup()
    {
        foreach (var textBox in GameInfoTextBoxes)
            textBox.TextChanged += GameInfoField_Changed;
        CaptureGameInfoText();
        cmbLanguageCode.SelectionChanged += GameInfoLanguage_Changed;
    }

    private void CaptureGameInfoText()
    {
        // TextChanged may be delivered after programmatic assignments. Record those assignments
        // before leaving the update scope so delayed events cannot be mistaken for user edits.
        foreach (var textBox in GameInfoTextBoxes)
            _observedGameInfoText[textBox] = textBox.Text;
    }

    private void EndGameInfoUpdate()
    {
        CaptureGameInfoText();
        _applyingGameInfo = false;
    }

    private void CancelGameInfoRequest()
    {
        var request = _gameInfoRequest;
        _gameInfoRequest = null;
        request?.Cancel(); // The awaiting handler owns disposal, including superseded requests.
        btnFetchGameInfo.IsEnabled = true;
        btnFetchGameInfo.Content = "自动获取游戏信息";
    }

    private void ResetGameInfoLookup()
    {
        CancelGameInfoRequest();
        _gameInfoSelection = null;
        _gameInfoIssues.Clear();
        cmbDepotCandidates.ItemsSource = null;
        cmbExecutableCandidates.ItemsSource = null;
        cmbDepotCandidates.Visibility = Visibility.Collapsed;
        cmbExecutableCandidates.Visibility = Visibility.Collapsed;
        gameInfoStatus.IsOpen = false;
    }

    private void UpdateGameInfoLinks()
    {
        var valid = SteamAppInfoService.TryNormalizeAppId(txtAppId.Text, out var appId);
        lnkGameConfig.IsEnabled = lnkGameDepots.IsEnabled = valid;
        lnkGameConfig.NavigateUri = valid ? new Uri($"https://steamdb.info/app/{appId}/config/") : null;
        lnkGameDepots.NavigateUri = valid ? new Uri($"https://steamdb.info/app/{appId}/depots/") : null;
    }

    private void ShowGameInfoStatus(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Warning)
    {
        gameInfoStatus.Title = title;
        gameInfoStatus.Message = message;
        gameInfoStatus.Severity = severity;
        gameInfoStatus.IsOpen = true;
    }

    private void GameInfoLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingGameInfo || !_formLoaded) return;
        ResetGameInfoLookup();
        ShowGameInfoStatus("语言已变化", "原有字段已保留，请重新获取或手动核对名称、Depot 和 Manifest。");
    }

    private void GameInfoField_Changed(object source, TextChangedEventArgs args)
    {
        var sender = (TextBox)source;
        if (_observedGameInfoText.TryGetValue(sender, out var observed) && observed == sender.Text) return;
        _observedGameInfoText[sender] = sender.Text;
        if (_applyingGameInfo || !_formLoaded) return;
        if (sender == txtAppId)
        {
            ResetGameInfoLookup();
            UpdateGameInfoLinks();
            ShowGameInfoStatus("AppID 已变化", "原有字段已保留，尚未验证是否属于当前 AppID。请自动获取或手动核对。");
            return;
        }
        if (_gameInfoRequest != null)
        {
            CancelGameInfoRequest();
            ShowGameInfoStatus("获取已取消", "输入已修改，已丢弃本次请求，避免覆盖新输入。请重新获取或手动填写。");
        }
        if (_gameInfoSelection == null) return;

        _applyingGameInfo = true;
        try
        {
            if (sender == txtDepotId)
            {
                cmbDepotCandidates.SelectedItem = null;
                var candidate = _gameInfoSelection.Depots.FirstOrDefault(d => d.Depot.Id == txtDepotId.Text.Trim());
                // Editing the depot always invalidates the previous depot's manifest.
                txtManifest.Text = candidate?.Manifest ?? "";
                if (candidate != null) _gameInfoIssues.Remove("DepotID");
                else _gameInfoIssues["DepotID"] = "手动 DepotID 未在本次适用候选中找到，请核对";
                if (string.IsNullOrEmpty(candidate?.Manifest))
                    _gameInfoIssues["Manifest"] = "当前 Depot 无可用 Manifest，已清除旧值，请手动填写";
                else _gameInfoIssues.Remove("Manifest");
            }
            else
            {
                var field = sender == txtExecutableFileName ? "Steam 占位文件名"
                    : sender == txtDisplayName ? "显示名称" : sender == txtInstallDir ? "安装目录名"
                    : sender == txtBuildId ? "BuildID" : "Manifest";
                _gameInfoIssues[field] = "已手动修改，请核对";
                if (sender == txtExecutableFileName) cmbExecutableCandidates.SelectedItem = null;
            }
        }
        finally { EndGameInfoUpdate(); }
        RefreshGameInfoStatus();
    }

    private async void FetchGameInfo_Click(object sender, RoutedEventArgs e)
    {
        if (!SteamAppInfoService.TryNormalizeAppId(txtAppId.Text, out var appId))
        {
            ShowGameInfoStatus("无法获取", "请填写有效的正整数 AppID（1–4294967295）。");
            return;
        }

        ResetGameInfoLookup();
        UpdateGameInfoLinks();
        var request = new CancellationTokenSource();
        _gameInfoRequest = request;
        var presetId = PresetId;
        var inputSnapshot = GameInfoTextBoxes.ToDictionary(box => box, box => box.Text);
        var options = new SteamAppSelectionOptions(
            Architecture: Environment.Is64BitOperatingSystem ? "64" : "32", Language: SelectedGameLanguage);
        btnFetchGameInfo.IsEnabled = false;
        btnFetchGameInfo.Content = "正在获取…";
        ShowGameInfoStatus("正在获取游戏信息", "请稍候。", InfoBarSeverity.Informational);
        _logService.AddLog($"[自定义页] 正在获取 AppID={appId} 的游戏信息（steamcmd.net）");

        try
        {
            var app = await _appInfoService.GetAppInfoAsync(appId, request.Token);
            // Reference identity prevents late responses after edits, navigation or a newer request.
            if (_gameInfoRequest != request || request.IsCancellationRequested || PresetId != presetId
                || !SteamAppInfoService.TryNormalizeAppId(txtAppId.Text, out var currentId) || currentId != appId)
                return;
            if (SelectedGameLanguage != options.Language || inputSnapshot.Any(pair => pair.Key.Text != pair.Value))
            {
                ShowGameInfoStatus("获取已取消", "输入已修改，已丢弃过期结果，请重新获取或手动填写。");
                return;
            }
            var selection = SteamAppSelectionService.Select(app, options);
            _applyingGameInfo = true;
            try
            {
                _gameInfoSelection = selection;
                txtAppId.Text = app.AppId;
                FillGameInfoField(txtDisplayName, selection.DisplayName, "显示名称");
                FillGameInfoField(txtInstallDir, selection.InstallDirectory, "安装目录名");
                FillGameInfoField(txtBuildId, selection.BuildId, "BuildID");
                cmbDepotCandidates.ItemsSource = selection.Depots;
                cmbDepotCandidates.Visibility = selection.Depots.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                cmbExecutableCandidates.ItemsSource = selection.Executables;
                cmbExecutableCandidates.Visibility = selection.Executables.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

                if (selection.Depots.Count == 1)
                    ApplyDepotCandidate(selection.Depots[0], preserveMissingManifest: true);
                else
                {
                    _gameInfoIssues["DepotID"] = selection.Depots.Count == 0 ? "未找到适用候选，原值未验证" : "有多个候选，请选择，原值未验证";
                    _gameInfoIssues["Manifest"] = "尚未确定 Depot，原值未验证";
                }
                if (selection.Executables.Count == 1)
                    FillGameInfoField(txtExecutableFileName, selection.Executables[0].Path, "Steam 占位文件名");
                else
                    _gameInfoIssues["Steam 占位文件名"] = selection.Executables.Count == 0
                        ? "未找到适用的 EXE 相对路径，原值未验证" : "有多个候选，请选择，原值未验证";
            }
            finally { EndGameInfoUpdate(); }
            RefreshGameInfoStatus();
            _logService.AddLog($"[自定义页] AppID={appId} 查询完成：{selection.Depots.Count} 个适用 Depot，{selection.Executables.Count} 个 EXE 路径");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_gameInfoRequest != request) return;
            var message = ex is SteamAppInfoException ? ex.Message : "处理游戏信息时发生异常，请重试或手动填写。";
            ShowGameInfoStatus("获取失败", message + " 原有字段已保留，未被本次查询验证。", InfoBarSeverity.Error);
            _logService.AddLog($"[自定义页] 游戏信息获取失败：{ex.Message}");
        }
        finally
        {
            if (_gameInfoRequest == request)
            {
                _gameInfoRequest = null;
                btnFetchGameInfo.IsEnabled = true;
                btnFetchGameInfo.Content = "自动获取游戏信息";
            }
            request.Dispose();
        }
    }

    private void FillGameInfoField(TextBox target, string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _gameInfoIssues[field] = "缺失或不合法，原值未验证，请手动核对";
            return;
        }
        target.Text = value;
        _gameInfoIssues.Remove(field);
    }

    private void ApplyDepotCandidate(SteamDepotCandidate candidate, bool preserveMissingManifest = false)
    {
        txtDepotId.Text = candidate.Depot.Id;
        _gameInfoIssues.Remove("DepotID");
        if (string.IsNullOrEmpty(candidate.Manifest) && !preserveMissingManifest)
            txtManifest.Text = "";
        FillGameInfoField(txtManifest, candidate.Manifest, "Manifest");
    }

    private void DepotCandidate_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingGameInfo || _gameInfoSelection == null
            || cmbDepotCandidates.SelectedItem is not SteamDepotCandidate candidate) return;
        _applyingGameInfo = true;
        try { ApplyDepotCandidate(candidate); }
        finally { EndGameInfoUpdate(); }
        RefreshGameInfoStatus();
    }

    private void ExecutableCandidate_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingGameInfo || _gameInfoSelection == null
            || cmbExecutableCandidates.SelectedItem is not SteamExecutableCandidate candidate) return;
        _applyingGameInfo = true;
        try { FillGameInfoField(txtExecutableFileName, candidate.Path, "Steam 占位文件名"); }
        finally { EndGameInfoUpdate(); }
        RefreshGameInfoStatus();
    }

    private void RefreshGameInfoStatus()
    {
        if (_gameInfoSelection == null) return;
        var messages = _gameInfoIssues.Select(i => $"{i.Key}：{i.Value}。")
            .Concat(_gameInfoSelection.Warnings).ToList();
        ShowGameInfoStatus(messages.Count == 0 ? "已填入游戏信息" : "已获取，请检查以下字段",
            messages.Count == 0 ? "可继续编辑；保存预设和生成配置沿用原有操作。" : string.Join("\n", messages),
            messages.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }
}
