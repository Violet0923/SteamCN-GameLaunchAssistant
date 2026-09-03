using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Specialized;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WetheringWavesSteamHelper_WinUI.Models;
using WetheringWavesSteamHelper_WinUI.Services;

namespace WetheringWavesSteamHelper_WinUI.Views.Pages;

public sealed partial class CustomManifestPage : Page
{
    private readonly SteamService _steamService = new();
    private readonly SettingsService _settingsService = new();
    private readonly CustomManifestService _customManifestService = CustomManifestService.Instance;
    private readonly LogService _logService = LogService.Instance;
    private AppSettings _settings = new();
    private CustomManifestPreset? _preset;
    private List<CustomManifestPreset> _presets = new();
    private bool _suppressSelectionChanged;
    private bool _formLoaded;
    private bool _deleted;

    public string PresetId { get; private set; } = "";

    // 是否已在本次页面生命周期内提示过手动输入路径的风险
    private bool _clientExePathWarningShown = false;

    private NotifyCollectionChangedEventHandler? _logScrollHandler;

    public CustomManifestPage()
    {
        InitializeComponent();
        InitializeGameInfoLookup();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        logList.ItemsSource = _logService.Logs;
        _logScrollHandler = (_, _) =>
        {
            logScrollViewer.ChangeView(null, logScrollViewer.ScrollableHeight, null);
        };
        _logService.Logs.CollectionChanged += _logScrollHandler;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ResetGameInfoLookup();
        PersistCurrentPreset(showFailureLog: false);
        if (_logScrollHandler != null)
            _logService.Logs.CollectionChanged -= _logScrollHandler;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        PresetId = e.Parameter as string ?? _customManifestService.GetCurrentId();
        _presets = _customManifestService.GetAll().ToList();
        _preset = _presets.FirstOrDefault(p =>
            string.Equals(p.Id, PresetId, StringComparison.OrdinalIgnoreCase))
            ?? _presets.FirstOrDefault();
        if (_preset == null) return;

        PresetId = _preset.Id;
        _settings = _settingsService.Load();
        RefreshPresetComboBox(PresetId);
        LoadPreset(_preset);
        UpdateGlobalConfigInfoBar();
        _formLoaded = true;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ResetGameInfoLookup();
        PersistCurrentPreset(showFailureLog: false);
        base.OnNavigatedFrom(e);
    }

    private void LoadPreset(CustomManifestPreset preset)
    {
        ResetGameInfoLookup();
        _applyingGameInfo = true;
        try
        {
            btnRenamePreset.IsEnabled = !preset.IsBuiltIn;
            btnDeletePreset.IsEnabled = !preset.IsBuiltIn;
            txtAppId.Text = preset.AppId;
            txtDepotId.Text = preset.DepotId;
            txtDisplayName.Text = preset.GameDisplayName;
            txtInstallDir.Text = preset.InstallDir;
            txtClientExePath.Text = preset.ClientExePath;
            txtLauncherExePath.Text = preset.LauncherExePath;
            txtExecutableFileName.Text = preset.ExecutableFileName;
            txtBuildId.Text = preset.BuildId;
            txtManifest.Text = preset.Manifest;

            var langTag = string.IsNullOrEmpty(preset.Language) ? "schinese" : preset.Language;
            foreach (var item in cmbLanguageCode.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag && tag == langTag)
                {
                    cmbLanguageCode.SelectedItem = item;
                    return;
                }
            }
            cmbLanguageCode.SelectedIndex = 0;
        }
        finally
        {
            EndGameInfoUpdate();
            UpdateGameInfoLinks();
        }
    }

    private void UpdateGlobalConfigInfoBar()
    {
        var missing = string.IsNullOrWhiteSpace(_settings.SteamInstallPath)
                   || string.IsNullOrWhiteSpace(_settings.SteamLibraryPath)
                   || string.IsNullOrWhiteSpace(_settings.SteamId);
        globalConfigInfoBar.IsOpen = missing;
    }

    /// <summary>从表单构造当前自定义配置。</summary>
    private CustomManifestPreset BuildPresetFromUI()
    {
        var langTag = (cmbLanguageCode.SelectedItem as ComboBoxItem)?.Tag as string ?? "schinese";
        var current = _preset;
        return new CustomManifestPreset
        {
            Id = current?.Id ?? PresetId,
            Name = current?.Name is { Length: > 0 } name ? name : "默认",
            IsBuiltIn = current?.IsBuiltIn ?? false,
            AppId = txtAppId.Text.Trim(),
            DepotId = txtDepotId.Text.Trim(),
            BuildId = txtBuildId.Text.Trim(),
            Manifest = txtManifest.Text.Trim(),
            GameDisplayName = txtDisplayName.Text.Trim(),
            InstallDir = txtInstallDir.Text.Trim(),
            ClientExePath = txtClientExePath.Text.Trim(),
            LauncherExePath = txtLauncherExePath.Text.Trim(),
            ExecutableFileName = txtExecutableFileName.Text.Trim(),
            Language = langTag,
        };
    }

    private CustomManifestPreset? GetSelectedPreset()
    {
        return _preset;
    }

    // ── 自定义页面管理 ──────────────────────────────────────────────────────────

    private void RefreshPresetComboBox(string selectedId)
    {
        _suppressSelectionChanged = true;
        try
        {
            cmbPreset.ItemsSource = null;
            cmbPreset.ItemsSource = _presets;
            cmbPreset.SelectedItem = _presets.FirstOrDefault(p =>
                string.Equals(p.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged || cmbPreset.SelectedItem is not CustomManifestPreset selected)
            return;
        if (string.Equals(selected.Id, PresetId, StringComparison.OrdinalIgnoreCase))
            return;

        if (!PersistCurrentPreset())
        {
            RefreshPresetComboBox(PresetId);
            return;
        }

        var target = _customManifestService.GetById(selected.Id);
        if (target == null)
        {
            _presets = _customManifestService.GetAll().ToList();
            RefreshPresetComboBox(PresetId);
            return;
        }

        _preset = target;
        PresetId = target.Id;
        _deleted = false;
        LoadPreset(_preset);

        // 保持下拉框与左侧导航的选中状态一致。
        _customManifestService.Select(PresetId, notifyNavigation: true);
    }

    private async void NewPreset_Click(object sender, RoutedEventArgs e)
    {
        var name = await PromptForPresetNameAsync("新建自定义", "");
        if (name == null) return;

        var created = _customManifestService.Create(name);
        if (created == null)
        {
            await ShowInfoAsync("无法新建自定义配置，请稍后重试。");
            return;
        }
        _logService.AddLog($"[自定义页] 已新建自定义：{name}");
    }

    private async void SaveAsPreset_Click(object sender, RoutedEventArgs e)
    {
        var name = await PromptForPresetNameAsync("另存为新自定义", "");
        if (name == null) return;

        var created = _customManifestService.Create(name, BuildPresetFromUI());
        if (created == null)
        {
            await ShowInfoAsync("无法保存新的自定义配置，请稍后重试。");
            return;
        }
        _logService.AddLog($"[自定义页] 已另存为新自定义：{name}");
    }

    private async void RenamePreset_Click(object sender, RoutedEventArgs e)
    {
        var current = GetSelectedPreset();
        if (current == null) return;

        var name = await PromptForPresetNameAsync("重命名自定义", current.Name, current.Id);
        if (name == null) return;

        if (!_customManifestService.Rename(current.Id, name))
        {
            await ShowInfoAsync("重命名失败，请稍后重试。");
            return;
        }
        _presets = _customManifestService.GetAll().ToList();
        _preset = _presets.First(p =>
            string.Equals(p.Id, current.Id, StringComparison.OrdinalIgnoreCase));
        RefreshPresetComboBox(current.Id);
        LoadPreset(_preset);
        _logService.AddLog($"[自定义页] 已重命名为：{name}");
    }

    private async void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        var current = GetSelectedPreset();
        if (current == null) return;

        var dialog = new ContentDialog
        {
            Title = "删除自定义",
            Content = $"确定要删除自定义「{current.Name}」吗？此操作不可撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        _deleted = true;
        if (_customManifestService.Delete(current.Id) == null)
        {
            _deleted = false;
            await ShowInfoAsync("删除失败，请稍后重试。");
            return;
        }
        _logService.AddLog($"[自定义页] 已删除自定义：{current.Name}");
    }

    /// <summary>
    /// 弹出对话框输入侧边栏名称，校验非空且不与现有配置重名。
    /// 取消或校验失败返回 null；excludeId 用于重命名时排除当前配置。
    /// </summary>
    private async Task<string?> PromptForPresetNameAsync(string title, string defaultText, string? excludeId = null)
    {
        var textBox = new TextBox { Text = defaultText, PlaceholderText = "请输入预设名称" };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var name = textBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            await ShowInfoAsync("预设名称不能为空。");
            return null;
        }

        if (_customManifestService.NameExists(name, excludeId))
        {
            await ShowInfoAsync($"已存在同名预设「{name}」，请换一个名称。");
            return null;
        }

        return name;
    }

    // ── 浏览 EXE / 清除 ───────────────────────────────────────────────────────

    private async void BrowseClientExe_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".exe");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

        var file = await PickFileSafelyAsync(picker, "自定义页-浏览客户端EXE");
        if (file == null) return;

        txtClientExePath.Text = file.Path;
        _logService.AddLog($"[自定义页] 已选择可执行文件：{file.Path}");
    }

    private void ClearClientExe_Click(object sender, RoutedEventArgs e)
    {
        txtClientExePath.Text = string.Empty;
        _logService.AddLog("[自定义页] 已清除可执行文件路径");
    }

    private async void BrowseLauncherExe_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickLauncherExeAsync("自定义页-浏览游戏启动器");
        if (file == null) return;

        txtLauncherExePath.Text = file.Path;
        _logService.AddLog($"[自定义页] 已选择游戏启动器：{file.Path}");
    }

    private void ClearLauncherExe_Click(object sender, RoutedEventArgs e)
    {
        txtLauncherExePath.Text = string.Empty;
        _logService.AddLog("[自定义页] 已清除游戏启动器路径");
    }

    private async Task<Windows.Storage.StorageFile?> PickLauncherExeAsync(string context)
    {
        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".exe");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        return await PickFileSafelyAsync(picker, context);
    }

    // ── 生成 ACF ──────────────────────────────────────────────────────────────

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        // 重新加载全局字段（Steam 路径等），但保留内存中的预设列表
        var disk = _settingsService.Load();
        _settings.SteamInstallPath = disk.SteamInstallPath;
        _settings.SteamLibraryPath = disk.SteamLibraryPath;
        _settings.SteamId = disk.SteamId;
        UpdateGlobalConfigInfoBar();

        if (!ValidateForGenerate()) return;

        // 解析并校验占位 exe 文件名（在写文件前）
        var displayNameForExe = txtDisplayName.Text.Trim();
        var exeFileName = txtExecutableFileName.Text.Trim();
        if (string.IsNullOrEmpty(exeFileName))
            exeFileName = displayNameForExe + ".exe";
        else if (!exeFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exeFileName += ".exe";

        if (!TryValidatePlaceholderExeName(exeFileName, out var exeNameError))
        {
            _logService.AddLog($"[自定义页] 错误: 占位文件名不合法：{exeFileName}（{exeNameError}）");
            await ShowInfoAsync($"Steam 占位文件名不合法：\n{exeFileName}\n\n{exeNameError}\n\n请修正后重试。");
            return;
        }
        // 统一子目录分隔符（部分游戏的 Steam Executable 登记在子目录中，例如 "Sub/Launcher.exe"）
        exeFileName = Path.Combine(exeFileName.Split('/', '\\'));

        btnGenerate.IsEnabled = false;
        try
        {
            var appId = txtAppId.Text.Trim();
            var depotId = txtDepotId.Text.Trim();
            var displayName = txtDisplayName.Text.Trim();
            var installDir = txtInstallDir.Text.Trim();
            var buildId = txtBuildId.Text.Trim();
            var manifest = txtManifest.Text.Trim();
            var langTag = (cmbLanguageCode.SelectedItem as ComboBoxItem)?.Tag as string ?? "schinese";

            var libraryPath = _settings.SteamLibraryPath.Trim();
            var steamInstallPath = _settings.SteamInstallPath.Trim();
            var steamappsPath = Path.Combine(libraryPath, "steamapps");
            var launcherPath = Path.Combine(steamInstallPath, "steam.exe");
            var acfPath = Path.Combine(steamappsPath, $"appmanifest_{appId}.acf");

            if (File.Exists(acfPath))
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "文件已存在",
                    Content = $"检测到配置文件已存在：\nappmanifest_{appId}.acf\n\n是否要覆盖该文件？",
                    PrimaryButtonText = "覆盖",
                    CloseButtonText = "取消",
                    XamlRoot = XamlRoot
                };
                if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    _logService.AddLog("[自定义页] 用户取消覆盖 ACF 文件，操作已跳过。");
                    return;
                }
                _logService.AddLog("[自定义页] 用户选择覆盖 ACF 文件。");
            }

            var acfContent = _steamService.GenerateAcfContent(new AcfParameters(
                AppId: appId,
                DepotId: depotId,
                LauncherPath: launcherPath,
                DisplayName: displayName,
                InstallDir: installDir,
                BuildId: buildId,
                LastOwner: _settings.SteamId.Trim(),
                Manifest: manifest,
                Language: langTag));

            if (!Directory.Exists(steamappsPath))
            {
                Directory.CreateDirectory(steamappsPath);
                _logService.AddLog($"[自定义页] 已创建目录：{steamappsPath}");
            }

            await File.WriteAllTextAsync(acfPath, acfContent);
            _logService.AddLog($"[自定义页] 已生成：{acfPath}");

            // 创建安装目录 + 占位 EXE（修复 #23）
            var ph = _steamService.EnsureGameDirAndPlaceholder(libraryPath, installDir, exeFileName);
            if (ph.DirCreated)
                _logService.AddLog($"[自定义页] 已创建目录：{ph.GameDirPath}");
            if (ph.ExeCreated)
                _logService.AddLog($"[自定义页] 已创建占位 EXE：{ph.ExePath}");
            else
                _logService.AddLog($"[自定义页] 占位 EXE 已存在，跳过：{ph.ExePath}");

            // 同步保存当前配置（与鸣潮页一致的隐式保存语义）
            PersistCurrentPreset();
            _logService.AddLog("[自定义页][完成] ACF 与占位 EXE 已就绪，请重启 Steam。");

            await ShowInfoAsync($"配置生成成功！\n\n占位 EXE 已就绪：{exeFileName}\n\n请重启 Steam 客户端，然后在库中找到「{displayName}」。");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"[自定义页][错误] 操作失败：{ex.Message}");
            await ShowInfoAsync($"生成过程中出现错误：\n{ex.Message}");
        }
        finally
        {
            btnGenerate.IsEnabled = true;
        }
    }

    // ── 复制启动命令 ──────────────────────────────────────────────────────────

    private async void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        var exePath = txtClientExePath.Text.Trim();
        if (string.IsNullOrEmpty(exePath))
        {
            await ShowInfoAsync("请先指定游戏可执行文件（EXE）。");
            return;
        }
        if (!File.Exists(exePath))
        {
            var confirmDialog = new ContentDialog
            {
                Title = "路径不存在",
                Content = $"所指定的可执行文件不存在：\n{exePath}\n\n是否仍要生成启动命令？",
                PrimaryButtonText = "继续",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            };
            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        var command = _steamService.GenerateLaunchCommandFromExe(exePath);

        var dataPackage = new DataPackage();
        dataPackage.SetText(command);
        Clipboard.SetContent(dataPackage);

        _logService.AddLog("[自定义页] 启动命令已复制到剪贴板");
        _logService.AddLog($"[自定义页] 命令：{command}");
    }

    // ── 显式保存 ──────────────────────────────────────────────────────────────

    private async void OpenLauncher_Click(object sender, RoutedEventArgs e)
    {
        var launcherExe = txtLauncherExePath.Text.Trim();

        if (string.IsNullOrEmpty(launcherExe) || !File.Exists(launcherExe))
        {
            _logService.AddLog(string.IsNullOrEmpty(launcherExe)
                ? "[自定义页] 尚未指定游戏启动器，请手动选择"
                : $"[自定义页] 游戏启动器不存在，请重新选择：{launcherExe}");

            var file = await PickLauncherExeAsync("自定义页-打开游戏启动器");
            if (file == null) return;

            launcherExe = file.Path;
            txtLauncherExePath.Text = launcherExe;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = launcherExe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(launcherExe)
            };
            System.Diagnostics.Process.Start(psi);
            _logService.AddLog($"[自定义页] 已启动游戏启动器：{launcherExe}");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"[自定义页] 启动器打开失败：{ex.Message}");
            await ShowInfoAsync($"无法打开游戏启动器：\n{ex.Message}");
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (PersistCurrentPreset())
        {
            _logService.AddLog("[自定义页] 已保存当前配置");
            await ShowInfoAsync("当前配置已保存。下次打开该页将自动恢复。");
        }
        else
        {
            await ShowInfoAsync("当前配置保存失败，请稍后重试。");
        }
    }

    /// <summary>
    /// 把当前表单按稳定 Id 原子写回，页面离开时也会自动保存。
    /// </summary>
    private bool PersistCurrentPreset(bool showFailureLog = true)
    {
        if (!_formLoaded || _deleted || _preset == null || string.IsNullOrWhiteSpace(PresetId))
            return true;

        var built = BuildPresetFromUI();
        var saved = _customManifestService.Update(built);
        if (saved)
        {
            _preset = built;
            var index = _presets.FindIndex(p =>
                string.Equals(p.Id, built.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                _presets[index] = built.Clone();
        }
        else if (showFailureLog)
            _logService.AddLog("[自定义页][警告] 设置保存失败");

        return saved;
    }

    // ── 校验 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 校验 Steam 占位文件名。部分游戏的 Steam appinfo Executable 字段登记在安装目录的子目录中
    /// （例如 "Sub/Launcher.exe"），因此允许路径分隔符，但按分段校验以阻止绝对路径和目录穿越（issue #29）。
    /// </summary>
    private static bool TryValidatePlaceholderExeName(string exeFileName, out string error)
        => SteamPathValidator.TryValidate(exeFileName, out error, requireExe: true);

    private bool ValidateForGenerate()
    {
        if (string.IsNullOrWhiteSpace(_settings.SteamInstallPath)
            || string.IsNullOrWhiteSpace(_settings.SteamLibraryPath)
            || string.IsNullOrWhiteSpace(_settings.SteamId))
        {
            _logService.AddLog("[自定义页] 错误: Steam 全局配置不完整，请前往设置页配置");
            globalConfigInfoBar.IsOpen = true;
            return false;
        }

        if (string.IsNullOrWhiteSpace(txtAppId.Text))
        {
            _logService.AddLog("[自定义页] 错误: 请填写 AppID");
            return false;
        }
        if (!SteamAppInfoService.TryNormalizeAppId(txtAppId.Text, out _))
        {
            _logService.AddLog("[自定义页] 错误: AppID 必须为有效的正整数");
            return false;
        }
        if (string.IsNullOrWhiteSpace(txtDepotId.Text))
        {
            _logService.AddLog("[自定义页] 错误: 请填写 DepotID");
            return false;
        }
        if (!SteamAppInfoService.TryNormalizeAppId(txtDepotId.Text, out _))
        {
            _logService.AddLog("[自定义页] 错误: DepotID 必须为有效的正整数");
            return false;
        }
        if (string.IsNullOrWhiteSpace(txtDisplayName.Text))
        {
            _logService.AddLog("[自定义页] 错误: 请填写显示名称");
            return false;
        }
        if (string.IsNullOrWhiteSpace(txtInstallDir.Text))
        {
            _logService.AddLog("[自定义页] 错误: 请填写安装目录名");
            return false;
        }
        if (!SteamPathValidator.TryValidate(txtInstallDir.Text.Trim(), out var directoryError))
        {
            _logService.AddLog($"[自定义页] 错误: 安装目录名不合法：{directoryError}");
            return false;
        }
        if (txtDisplayName.Text.Any(c => c < 32 || c is '"' or '\\'))
        {
            _logService.AddLog("[自定义页] 错误: 显示名称不能包含引号、反斜杠或控制字符");
            return false;
        }
        if (string.IsNullOrWhiteSpace(txtBuildId.Text))
        {
            _logService.AddLog("[自定义页] 错误: 请先获取或填写 BuildID");
            return false;
        }
        if (string.IsNullOrWhiteSpace(txtManifest.Text))
        {
            _logService.AddLog("[自定义页] 错误: 请先获取或填写 Manifest");
            return false;
        }
        return true;
    }

    // ── 日志 ──────────────────────────────────────────────────────────────────

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _logService.Clear();

    private async void ClientExePath_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_clientExePathWarningShown) return;
        _clientExePathWarningShown = true;

        var dialog = new ContentDialog
        {
            Title = "手动输入路径",
            Content = "请确保填写的是正确的游戏可执行文件路径。路径错误会导致游戏无法启动；如果填的是其他程序的路径，Steam 启动时会执行那个程序而不是游戏。建议优先使用「浏览」按钮选择文件。",
            CloseButtonText = "知道了",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task<Windows.Storage.StorageFile?> PickFileSafelyAsync(FileOpenPicker picker, string context)
    {
        try
        {
            return await picker.PickSingleFileAsync();
        }
        catch (Exception ex)
        {
            _logService.AddLog($"[{context}] 文件选择框打开失败：{ex.GetType().Name} - {ex.Message}");
            try
            {
                await ShowInfoAsync($"无法打开文件选择框，这通常是系统 Shell 组件异常导致的。\n\n错误信息：{ex.Message}\n\n您可以尝试直接在路径输入框中粘贴文件路径。");
            }
            catch (Exception dialogEx)
            {
                _logService.AddLog($"[{context}] 显示错误提示弹窗也失败了：{dialogEx.GetType().Name} - {dialogEx.Message}");
            }
            return null;
        }
    }

    private async Task ShowInfoAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "提示",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
