using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using WetheringWavesSteamHelper_WinUI.Models;

namespace WetheringWavesSteamHelper_WinUI.Services;

/// <summary>管理安装目录中的背景副本、原图和用户外观配置。</summary>
public sealed class AppearanceService
{
    public static AppearanceService Instance { get; } = new();
    public string ImageDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Backgrounds");
    public AppearanceSettings Settings { get; }
    public event Action? Changed;
    private readonly SettingsService _settingsService = new();

    private AppearanceService()
    {
        Settings = _settingsService.Load().Appearance ?? new();
        Settings.SelectedImage ??= "";
        Settings.Images ??= new();
    }

    public void Preview() => Changed?.Invoke();
    public bool Save() => _settingsService.Update(s => s.Appearance = Settings);

    public string GetImagePath(string name)
    {
        // 只允许直接文件名，防止配置中的相对路径越过背景目录。
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !IsImage(name))
            throw new IOException("背景图片文件名无效。");
        return Path.Combine(ImageDirectory, name);
    }

    private static bool IsImage(string name) =>
        new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(name).ToLowerInvariant());

    public IReadOnlyList<string> GetImages() => Directory.Exists(ImageDirectory)
        ? Directory.EnumerateFiles(ImageDirectory).Where(IsImage).Select(Path.GetFileName)
            .OfType<string>().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
        : Array.Empty<string>();

    // Validate actual contents and decode a bounded preview before committing the private copy.
    public async Task<string> ImportAsync(StorageFile source)
    {
        var properties = await source.GetBasicPropertiesAsync();
        if (properties.Size > 40 * 1024 * 1024)
            throw new IOException("图片不能超过 40 MB。");
        Directory.CreateDirectory(ImageDirectory);
        var extension = Path.GetExtension(source.Name).ToLowerInvariant();
        if (!IsImage(source.Name)) throw new IOException("仅支持 JPG、JPEG 和 PNG 图片。");
        var name = $"{Path.GetFileNameWithoutExtension(source.Name)[..Math.Min(Path.GetFileNameWithoutExtension(source.Name).Length, 60)]}_{Guid.NewGuid():N}{extension}";
        var destination = GetImagePath(name);
        var temporary = destination + ".importing";
        try
        {
            using (var input = await source.OpenStreamForReadAsync())
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer)) > 0)
                {
                    total += count;
                    if (total > 40 * 1024 * 1024) throw new IOException("图片不能超过 40 MB。");
                    await output.WriteAsync(buffer.AsMemory(0, count));
                }
            }
            var copy = await StorageFile.GetFileFromPathAsync(temporary);
            using (var stream = await copy.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                if (decoder.DecoderInformation.CodecId != BitmapDecoder.PngDecoderId &&
                    decoder.DecoderInformation.CodecId != BitmapDecoder.JpegDecoderId)
                    throw new IOException("图片内容必须是 JPG 或 PNG 格式。");
                if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 ||
                    decoder.PixelWidth > 16384 || decoder.PixelHeight > 16384 ||
                    (ulong)decoder.PixelWidth * decoder.PixelHeight > 80_000_000)
                    throw new IOException("图片尺寸过大：最长边不能超过 16384 像素，总像素不能超过 8000 万。");
                var scale = Math.Min(1d, 256d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
                await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    new BitmapTransform { ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale)),
                        ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale)) },
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
            }
            File.Move(temporary, destination);
            return name;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<BitmapImage> LoadImageAsync(string name, int decodeWidth)
    {
        var file = await StorageFile.GetFileFromPathAsync(GetImagePath(name));
        if ((await file.GetBasicPropertiesAsync()).Size > 40 * 1024 * 1024)
            throw new IOException("图片不能超过 40 MB。");
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 ||
            decoder.PixelWidth > 16384 || decoder.PixelHeight > 16384 ||
            (ulong)decoder.PixelWidth * decoder.PixelHeight > 80_000_000)
            throw new IOException("图片尺寸超出限制。");
        if (decoder.DecoderInformation.CodecId != BitmapDecoder.PngDecoderId &&
            decoder.DecoderInformation.CodecId != BitmapDecoder.JpegDecoderId)
            throw new IOException("仅支持 JPG 和 PNG 图片。");
        // Bound the longest side, including very tall portraits, rather than only the width.
        var bitmap = new BitmapImage();
        if (decoder.OrientedPixelWidth >= decoder.OrientedPixelHeight)
            bitmap.DecodePixelWidth = Math.Min(decodeWidth, (int)decoder.OrientedPixelWidth);
        else
            bitmap.DecodePixelHeight = Math.Min(decodeWidth, (int)decoder.OrientedPixelHeight);
        stream.Seek(0);
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    public void Delete(string name)
    {
        File.Delete(GetImagePath(name));
        Settings.Images.TryGetValue(name, out var deleted);
        Settings.Images.Remove(name);
        if (!string.IsNullOrEmpty(deleted?.SourceImage) && !Settings.Images.Values.Any(o => o?.SourceImage == deleted.SourceImage))
        {
            try { File.Delete(GetOriginalPath(deleted.SourceImage)); }
            catch (Exception ex) { LogService.Instance.AddLog($"背景已删除，但原图副本清理失败：{ex.Message}"); }
        }
        if (Settings.SelectedImage == name)
        {
            Settings.SelectedImage = "";
            Settings.Enabled = false;
        }
        Preview();
    }

    public string GetOriginalPath(string name)
    {
        GetImagePath(name); // Apply the same filename validation; never accept relative paths.
        return Path.Combine(ImageDirectory, "Originals", name);
    }

    public async Task<string> SaveCropAsync(StorageFile source, BackgroundCropImage image,
        double scale, double x, double y, BackgroundOptions? previous = null)
    {
        // 像素合成放到后台，避免确认裁切时阻塞窗口；写入完成前不发布图库条目。
        var pixels = await Task.Run(() => BackgroundCropRenderer.Render(image.Pixels, image.Width, image.Height, scale, x, y));
        string? originalName = null;
        string? outputPath = null;
        string? temporary = null;
        try
        {
            originalName = await ImportAsync(source);
            Directory.CreateDirectory(Path.Combine(ImageDirectory, "Originals"));
            File.Move(GetImagePath(originalName), GetOriginalPath(originalName));
            var name = Path.GetFileNameWithoutExtension(originalName) + ".png";
            outputPath = GetImagePath(name);
            temporary = outputPath + ".importing";
            var folder = await StorageFolder.GetFolderFromPathAsync(ImageDirectory);
            var output = await folder.CreateFileAsync(Path.GetFileName(temporary), CreationCollisionOption.FailIfExists);
            using (var stream = await output.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                    BackgroundCropRenderer.Width, BackgroundCropRenderer.Height, 96, 96, pixels);
                await encoder.FlushAsync();
            }
            File.Move(temporary, outputPath);
            Settings.Images[name] = new BackgroundOptions
            {
                SourceImage = originalName, CropScale = scale, CropX = x, CropY = y,
                Opacity = previous?.Opacity ?? .6, OverlayOpacity = previous?.OverlayOpacity ?? .25,
                Stretch = "Fill"
            };
            return name;
        }
        catch
        {
            // Only remove the uniquely named files belonging to this failed import.
            foreach (var path in new[] { temporary, outputPath,
                originalName == null ? null : GetOriginalPath(originalName),
                originalName == null ? null : GetImagePath(originalName) })
            {
                try { if (path != null && File.Exists(path)) File.Delete(path); }
                catch { /* Preserve the original import error. */ }
            }
            throw;
        }
    }
}
