using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace WetheringWavesSteamHelper_WinUI.Services;

/// <summary>为预览与导出提供同一份已校正 EXIF 方向的预乘 BGRA 像素。</summary>
public sealed class BackgroundCropImage
{
    public int Width { get; private init; }
    public int Height { get; private init; }
    public byte[] Pixels { get; private init; } = Array.Empty<byte>();
    public SoftwareBitmapSource Preview { get; private init; } = null!;

    public static async Task<BackgroundCropImage> LoadAsync(StorageFile file)
    {
        using var stream = await file.OpenReadAsync();
        if (stream.Size > 40 * 1024 * 1024) throw new IOException("图片不能超过 40 MB。");
        var decoder = await BitmapDecoder.CreateAsync(stream);
        if (decoder.DecoderInformation.CodecId != BitmapDecoder.PngDecoderId &&
            decoder.DecoderInformation.CodecId != BitmapDecoder.JpegDecoderId)
            throw new IOException("仅支持 JPG 和 PNG 图片。");
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || decoder.PixelWidth > 16384 || decoder.PixelHeight > 16384
            || (ulong)decoder.PixelWidth * decoder.PixelHeight > 80_000_000)
            throw new IOException("图片最长边不能超过 16384 像素，总像素不能超过 8000 万。");
        // 限制解码最长边以控制内存；原始文件另存，重新裁切不使用上次导出的图片。
        var ratio = Math.Min(1d, 3840d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform { ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * ratio)),
                ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * ratio)), InterpolationMode = BitmapInterpolationMode.Fant },
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyToBuffer(pixels.AsBuffer());
        var preview = new SoftwareBitmapSource();
        await preview.SetBitmapAsync(bitmap);
        return new BackgroundCropImage { Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Pixels = pixels, Preview = preview };
    }
}
