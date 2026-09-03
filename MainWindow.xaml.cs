using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.UI;
using WetheringWavesSteamHelper_WinUI.Models;
using WetheringWavesSteamHelper_WinUI.Services;

namespace WetheringWavesSteamHelper_WinUI;

public sealed partial class MainWindow : Window
{
    private string _forceDownloadUrl = "";
    private AppWindow? _appWindow;
    private readonly CustomManifestService _customManifestService = CustomManifestService.Instance;
    private bool _refreshingCustomNavigation;
    private bool _addingCustomManifest;

    private sealed record CustomNavigationTag(string Id);

    public MainWindow()
    {
        InitializeComponent();
        InitializeGameReordering();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TopBarGrid);

        ConfigureFixedWindow();

        ((FrameworkElement)Content).ActualThemeChanged += (_, _) => ApplyTitleBarColors();

        txtTitleBar.Text = AppInfo.AppName;

        // 订阅更新通知（主窗口负责全局展示）
        UpdateService.Instance.UpdateAvailable += OnUpdateAvailable;
        _customManifestService.NavigationChanged += OnCustomNavigationChanged;
        Closed += (_, _) => _customManifestService.NavigationChanged -= OnCustomNavigationChanged;

        // 恢复上次使用的自定义游戏；没有游戏时展示添加入口。
        RefreshCustomNavigation(_customManifestService.GetInitialSidebarId());
    }

    // ── 更新通知处理 ──────────────────────────────────────────────────────────

    private void OnUpdateAvailable(string message, string downloadUrl, bool forceUpdate)
    {
        _forceDownloadUrl = downloadUrl;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (forceUpdate)
            {
                // 强制更新：显示全屏遮罩 + 顶部横幅，锁定所有导航
                txtForceUpdateMsg.Text = string.IsNullOrWhiteSpace(message)
                    ? "当前版本存在严重问题，必须更新后才能继续使用。"
                    : message;
                forceUpdateBanner.IsOpen = true;
                forceUpdateOverlay.Visibility = Visibility.Visible;
                NavView.IsEnabled = false;
            }
            else
            {
                // 普通更新：右上角显示提示按钮
                btnUpdateBadge.Visibility = Visibility.Visible;
            }
        });
    }

    // ── 按钮事件 ──────────────────────────────────────────────────────────────

    private void BtnUpdateBadge_Click(object sender, RoutedEventArgs e)
    {
        // 跳转到设置页
        NavView.SelectedItem = NavView.FooterMenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => i.Tag?.ToString() == "Settings");
        ContentFrame.Navigate(typeof(Views.Pages.SettingsPage));
    }

    private async void BtnForceUpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_forceDownloadUrl)) return;
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_forceDownloadUrl));
        }
        catch { }
    }

    // ── 窗口配置 ──────────────────────────────────────────────────────────────

    private void ConfigureFixedWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Title = AppInfo.WindowTitle;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "WutheringWavesSteamHelper.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        _appWindow.Resize(new SizeInt32(1100, 780));

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        ApplyTitleBarColors();
    }

    private void ApplyTitleBarColors()
    {
        if (_appWindow == null || !AppWindowTitleBar.IsCustomizationSupported()) return;

        var titleBar = _appWindow.TitleBar;
        var isDark = ((FrameworkElement)Content).ActualTheme == ElementTheme.Dark;

        // 透明背景，让 XAML 内容的颜色透出来
        titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);

        if (isDark)
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 200, 200, 200);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 127, 127, 127);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(80, 255, 255, 255);
        }
        else
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 0, 0, 0);
            titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 0, 0, 0);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 50, 50, 50);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 127, 127, 127);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 0, 0, 0);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(80, 0, 0, 0);
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_refreshingCustomNavigation || _reorderSource != null) return;
        if (args.SelectedItem is not NavigationViewItem item) return;

        if (item.Tag is CustomNavigationTag customTag)
        {
            NavigateToCustom(customTag.Id);
            return;
        }

        var tag = item.Tag?.ToString();
        switch (tag)
        {
            case "Settings":
                ContentFrame.Navigate(typeof(Views.Pages.SettingsPage));
                break;
            case "AppearanceSettings":
                ContentFrame.Navigate(typeof(Views.Pages.AppearanceSettingsPage));
                break;
            case "Placeholder":
                break;
        }
    }

    private void RefreshCustomNavigation(string? preferredId = null)
    {
        CancelGameReordering();
        var selectionId = preferredId ?? ((NavView.SelectedItem as NavigationViewItem)?.Tag as CustomNavigationTag)?.Id;
        var games = _customManifestService.GetSidebarItems();
        if (games.Count > 0 && !string.IsNullOrWhiteSpace(preferredId)
            && _customManifestService.GetById(preferredId) == null)
            preferredId = games[0].Id;
        _refreshingCustomNavigation = true;
        try
        {
            var dynamicItems = NavView.MenuItems
                .OfType<NavigationViewItem>()
                .Where(item => item.Tag is CustomNavigationTag)
                .ToList();
            foreach (var item in dynamicItems)
                NavView.MenuItems.Remove(item);

            var insertIndex = NavView.MenuItems.IndexOf(AddCustomNavItem);
            CustomGamesHeader.Visibility = games.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var preset in games)
            {
                var item = new NavigationViewItem
                {
                    Content = preset.Name,
                    Tag = new CustomNavigationTag(preset.Id),
                    Icon = new FontIcon { Glyph = "\uE7FC" }
                };
                ToolTipService.SetToolTip(item, $"{preset.Name}（可拖动调整顺序）");
                NavView.MenuItems.Insert(insertIndex++, item);
            }

            if (games.Count == 0)
                NavView.SelectedItem = null;
            else if (!string.IsNullOrWhiteSpace(selectionId))
            {
                // 旧版内置预设仍可通过页面下拉框访问，但不再有固定的侧边栏入口。
                // 找不到对应导航项时清除选中态，避免错误高亮“外观设置”等其他页面。
                NavView.SelectedItem = NavView.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(item => item.Tag is CustomNavigationTag custom
                        && string.Equals(custom.Id, selectionId, StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            _refreshingCustomNavigation = false;
        }

        if (games.Count == 0)
            ShowEmptyGameLibrary();
        else if (!string.IsNullOrWhiteSpace(preferredId))
            NavigateToCustom(preferredId);
    }

    private void ShowEmptyGameLibrary()
    {
        if (ContentFrame.Content is Views.Pages.EmptyGameLibraryPage) return;
        ContentFrame.Navigate(typeof(Views.Pages.EmptyGameLibraryPage));
        if (ContentFrame.Content is Views.Pages.EmptyGameLibraryPage page)
            page.AddGameRequested += async (_, _) => await AddGameAsync();
    }

    private void NavigateToCustom(string id)
    {
        if (ContentFrame.Content is Views.Pages.CustomManifestPage current
            && string.Equals(current.PresetId, id, StringComparison.OrdinalIgnoreCase))
        {
            _customManifestService.Select(id);
            return;
        }

        // 先导航，让旧页面在 OnNavigatedFrom 中保存；再记录新选中项，
        // 否则旧页的自动保存会把 CurrentCustomManifestId 改回旧 Id。
        ContentFrame.Navigate(typeof(Views.Pages.CustomManifestPage), id);
        _customManifestService.Select(id);
    }

    private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer == AddCustomNavItem)
            await AddGameAsync();
    }

    private async Task AddGameAsync()
    {
        if (_addingCustomManifest) return;
        _addingCustomManifest = true;
        try
        {
            var textBox = new TextBox { PlaceholderText = "请输入游戏名称", MinWidth = 320 };
            var dialog = new ContentDialog
            {
                Title = "添加游戏",
                Content = textBox,
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = NavView.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var name = textBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await ShowInfoAsync("游戏名称不能为空。");
                return;
            }
            if (_customManifestService.NameExists(name))
            {
                await ShowInfoAsync($"已存在同名游戏「{name}」，请换一个名称。");
                return;
            }

            if (_customManifestService.Create(name) == null)
                await ShowInfoAsync("无法保存新的自定义配置，请稍后重试。");
        }
        finally
        {
            _addingCustomManifest = false;
        }
    }

    private void OnCustomNavigationChanged(string? preferredId)
    {
        DispatcherQueue.TryEnqueue(() => RefreshCustomNavigation(preferredId));
    }

    private async Task ShowInfoAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "提示",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = NavView.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
