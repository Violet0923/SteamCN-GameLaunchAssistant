using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WetheringWavesSteamHelper_WinUI.Services;

namespace WetheringWavesSteamHelper_WinUI;

public sealed partial class MainWindow
{
    private readonly SettingsService _sidebarSettings = new();

    private void InitializeSidebarResizing()
    {
        // 保存的是 XAML 逻辑宽度；首次布局后再按实际内容空间限制上限。
        var savedWidth = _sidebarSettings.Load().SidebarWidth;
        NavView.OpenPaneLength = double.IsFinite(savedWidth) ? Math.Clamp(savedWidth, 160, 360) : 210;
        NavView.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => UpdateSidebarHandle());
        NavView.RegisterPropertyChangedCallback(Control.IsEnabledProperty, (_, _) => UpdateSidebarHandle());
        NavView.SizeChanged += (_, _) =>
        {
            SetSidebarWidth(NavView.OpenPaneLength);
            UpdateSidebarHandle();
        };
        UpdateSidebarHandle();
    }

    private void SetSidebarWidth(double width)
    {
        // Reserve enough space for the form when Windows uses a larger display scale.
        var maximum = NavView.ActualWidth > 0 ? Math.Clamp(NavView.ActualWidth - 480, 160, 360) : 360;
        NavView.OpenPaneLength = Math.Clamp(width, 160, maximum);
        UpdateSidebarHandle();
    }

    private void UpdateSidebarHandle()
    {
        SidebarResizeHandle.Margin = new Thickness(NavView.OpenPaneLength - SidebarResizeHandle.Width / 2, 0, 0, 0);
        SidebarResizeHandle.Visibility = NavView.IsPaneOpen && NavView.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SidebarResize_DragDelta(object? sender, double delta)
    {
        if (NavView.IsPaneOpen && NavView.IsEnabled)
            SetSidebarWidth(NavView.OpenPaneLength + delta);
    }

    private void SidebarResize_DragCompleted(object? sender, EventArgs e)
    {
        SidebarResizeHandle.Opacity = 0.35;
        SaveSidebarWidth();
    }

    private void SidebarResize_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Left && e.Key != VirtualKey.Right && e.Key != VirtualKey.Home) return;
        SetSidebarWidth(e.Key == VirtualKey.Home ? 210 : NavView.OpenPaneLength + (e.Key == VirtualKey.Left ? -10 : 10));
        SaveSidebarWidth();
        e.Handled = true;
    }

    private void SidebarResize_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        SetSidebarWidth(210);
        SaveSidebarWidth();
        e.Handled = true;
    }

    private void SaveSidebarWidth()
    {
        // 仅更新侧栏字段，避免旧配置快照覆盖游戏或外观设置。
        if (!_sidebarSettings.Update(settings => settings.SidebarWidth = NavView.OpenPaneLength))
            LogService.Instance.AddLog("侧边栏宽度保存失败，重启后可能恢复之前的宽度。");
    }
}
