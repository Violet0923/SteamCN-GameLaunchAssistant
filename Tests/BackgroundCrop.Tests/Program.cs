using SteamCNGameLaunchAssistant.Services;

var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    Console.WriteLine("PASS: " + label);
    checks++;
}
byte[] Pixel(byte[] image, int x, int y) => image.Skip((y * 1440 + x) * 4).Take(4).ToArray();
var red = new byte[] { 0, 0, 255, 255 };
var white = new byte[] { 255, 255, 255, 255 };
var centered = BackgroundCropRenderer.Render(red, 1, 1, 100, 670, 355);
Check(centered.Length == 1440 * 810 * 4, "output is exactly 1440 x 810 BGRA");
Check(Pixel(centered, 720, 405).SequenceEqual(red) && Pixel(centered, 0, 0).SequenceEqual(white),
    "shrinking centers the image with opaque white padding");
Check(Pixel(centered, 669, 405).SequenceEqual(white) && Pixel(centered, 670, 405).SequenceEqual(red)
    && Pixel(centered, 770, 405).SequenceEqual(white), "crop boundary uses the requested position and scale");
var outside = BackgroundCropRenderer.Render(red, 1, 1, 100, 2000, 2000);
Check(outside.All(b => b == 255), "moving image outside crop produces all white");
var transparent = BackgroundCropRenderer.Render(new byte[] { 0, 0, 128, 128 }, 1, 1, 2000, 0, 0);
Check(Pixel(transparent, 720, 405).SequenceEqual(new byte[] { 127, 127, 255, 255 }),
    "premultiplied transparent PNG is composited over white");
var oversized = BackgroundCropRenderer.Render(red, 1, 1, 2000, -200, -300);
Check(Pixel(oversized, 0, 0).SequenceEqual(red) && Pixel(oversized, 1439, 809).SequenceEqual(red),
    "zooming and negative offsets fill the complete frame");
var invalidRejected = false;
try { BackgroundCropRenderer.Render(red, 1, 1, double.NaN, 0, 0); }
catch (ArgumentException) { invalidRejected = true; }
Check(invalidRejected, "invalid scale rejected");
Console.WriteLine($"All {checks} checks passed.");
