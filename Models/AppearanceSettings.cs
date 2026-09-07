namespace SteamCNGameLaunchAssistant.Models;

/// <summary>全局外观状态；图片效果和裁切参数按图库文件名分别保存。</summary>
public sealed class AppearanceSettings
{
    public bool Enabled { get; set; }
    // Null preserves the theme's original card opacity for existing settings.
    public double? CardOpacity { get; set; }
    public string SelectedImage { get; set; } = "";
    public Dictionary<string, BackgroundOptions> Images { get; set; } = new();

    // 便捷访问器不单独序列化，避免与 Images 中的参数重复。
    [System.Text.Json.Serialization.JsonIgnore]
    public BackgroundOptions Current
    {
        get
        {
            Images ??= new();
            if (!Images.TryGetValue(SelectedImage, out var options) || options == null)
                Images[SelectedImage] = options = new();
            return options;
        }
    }
}

public sealed class BackgroundOptions
{
    // 原图位于 Backgrounds/Originals；裁切坐标基于 1440×810 输出画布。
    public string SourceImage { get; set; } = "";
    public double CropScale { get; set; }
    public double CropX { get; set; }
    public double CropY { get; set; }
    public double Opacity { get; set; } = 0.6;
    public double OverlayOpacity { get; set; } = 0.25;
    public string Stretch { get; set; } = "UniformToFill";
}
