namespace SteamCNGameLaunchAssistant.Services;

// Coordinates are always expressed in output pixels, independently of UI scaling.
public static class BackgroundCropRenderer
{
    public const int Width = 1440;
    public const int Height = 810;

    public static byte[] Render(byte[] source, int width, int height, double scale, double x, double y)
    {
        if (width <= 0 || height <= 0 || source.Length != checked(width * height * 4)
            || !double.IsFinite(scale) || scale <= 0 || !double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentException("裁切参数无效。");
        var output = new byte[Width * Height * 4];
        Array.Fill(output, (byte)255);
        for (var dy = 0; dy < Height; dy++)
        for (var dx = 0; dx < Width; dx++)
        {
            // 从输出像素中心反算源坐标；框外保持白色，框内使用双线性插值。
            var sx = (dx + 0.5 - x) / scale;
            var sy = (dy + 0.5 - y) / scale;
            if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
            sx = Math.Clamp(sx - 0.5, 0, width - 1);
            sy = Math.Clamp(sy - 0.5, 0, height - 1);
            var x0 = (int)sx;
            var y0 = (int)sy;
            var x1 = Math.Min(x0 + 1, width - 1);
            var y1 = Math.Min(y0 + 1, height - 1);
            var fx = sx - x0;
            var fy = sy - y0;
            double Sample(int c) =>
                (source[(y0 * width + x0) * 4 + c] * (1 - fx) + source[(y0 * width + x1) * 4 + c] * fx) * (1 - fy)
                + (source[(y1 * width + x0) * 4 + c] * (1 - fx) + source[(y1 * width + x1) * 4 + c] * fx) * fy;
            var alpha = Sample(3);
            var index = (dy * Width + dx) * 4;
            // Input is premultiplied BGRA; composite transparent pixels over opaque white.
            for (var c = 0; c < 3; c++)
                output[index + c] = (byte)Math.Clamp(Math.Round(Sample(c) + 255 - alpha), 0, 255);
        }
        return output;
    }
}
