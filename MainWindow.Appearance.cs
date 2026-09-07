using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamCNGameLaunchAssistant.Services;

namespace SteamCNGameLaunchAssistant;

public sealed partial class MainWindow
{
    private readonly AppearanceService _appearance = AppearanceService.Instance;
    private string _loadedBackground = "";
    private int _backgroundRequest;
    private readonly AcrylicBrush _paneBrush = new();
    private readonly SolidColorBrush _expandedPaneBrush = new();
    private readonly SolidColorBrush _contentBrush = new();

    private void InitializeAppearance()
    {
        NavView.Resources["NavigationViewDefaultPaneBackground"] = _paneBrush;
        NavView.Resources["NavigationViewExpandedPaneBackground"] = _expandedPaneBrush;
        NavView.Resources["NavigationViewContentBackground"] = _contentBrush;
        _appearance.Changed += ApplyAppearance;
        WindowRoot.ActualThemeChanged += (_, _) => ApplyAppearance();
        Closed += (_, _) => { _appearance.Changed -= ApplyAppearance; ++_backgroundRequest; };
        ApplyAppearance();
    }

    private async void ApplyAppearance()
    {
        // 解码可能晚于下一次切图完成，仅允许最后一次请求更新背景。
        var request = ++_backgroundRequest;
        var settings = _appearance.Settings;
        var cardBrush = (SolidColorBrush)Application.Current.Resources["AppearanceCardBackground"];
        var cardColor = ((SolidColorBrush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]).Color;
        if (settings.CardOpacity is double opacity && double.IsFinite(opacity))
            cardColor.A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        // 只改变底色画刷，卡片内的文字与输入控件不会一起变透明。
        cardBrush.Color = cardColor;
        var enabled = settings.Enabled && !string.IsNullOrEmpty(settings.SelectedImage);
        if (enabled && _loadedBackground != settings.SelectedImage)
        {
            var name = settings.SelectedImage;
            try
            {
                var bitmap = await _appearance.LoadImageAsync(name, 1920);
                if (request != _backgroundRequest) return;
                BackgroundImage.Source = bitmap;
                _loadedBackground = name;
            }
            catch (Exception ex)
            {
                if (request != _backgroundRequest) return;
                enabled = false;
                LogService.Instance.AddLog($"背景图片无法加载，已恢复默认外观：{ex.Message}");
            }
        }
        if (!enabled)
        {
            BackgroundImage.Source = null;
            _loadedBackground = "";
        }
        BackgroundImage.Visibility = BackgroundOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        var options = settings.Current;
        BackgroundImage.Opacity = double.IsFinite(options.Opacity) ? Math.Clamp(options.Opacity, 0, 1) : 0.6;
        BackgroundOverlay.Opacity = double.IsFinite(options.OverlayOpacity) ? Math.Clamp(options.OverlayOpacity, 0, 1) : 0.25;
        BackgroundImage.Stretch = options.Stretch switch
        {
            "Uniform" => Stretch.Uniform,
            "Fill" => Stretch.Fill,
            _ => Stretch.UniformToFill
        };
        var pageBrush = (SolidColorBrush)Application.Current.Resources["AppearancePageBackground"];
        var defaultPage = (SolidColorBrush)Application.Current.Resources["SolidBackgroundFillColorSecondaryBrush"];
        var defaultPane = (Brush)Application.Current.Resources["NavigationViewDefaultPaneBackground"];
        pageBrush.Color = enabled ? Colors.Transparent : defaultPage.Color;
        _contentBrush.Color = enabled ? Colors.Transparent
            : ((SolidColorBrush)Application.Current.Resources["NavigationViewContentBackground"]).Color;
        _expandedPaneBrush.Color = enabled ? Colors.Transparent
            : Application.Current.Resources["NavigationViewExpandedPaneBackground"] switch
            {
                SolidColorBrush brush => brush.Color,
                Windows.UI.Color color => color,
                _ => Colors.Transparent
            };
        // Use private brushes so disabling the background restores the original theme materials.
        if (defaultPane is AcrylicBrush acrylic)
        {
            _paneBrush.TintColor = acrylic.TintColor;
            _paneBrush.TintOpacity = acrylic.TintOpacity;
            _paneBrush.TintLuminosityOpacity = acrylic.TintLuminosityOpacity;
            _paneBrush.FallbackColor = acrylic.FallbackColor;
            _paneBrush.AlwaysUseFallback = acrylic.AlwaysUseFallback;
        }
        else if (defaultPane is SolidColorBrush solid)
        {
            _paneBrush.AlwaysUseFallback = true;
            _paneBrush.FallbackColor = solid.Color;
        }
        _paneBrush.Opacity = enabled ? 0 : defaultPane.Opacity;
        TopBarGrid.Background = enabled ? new SolidColorBrush(Colors.Transparent)
            : (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    }
}
