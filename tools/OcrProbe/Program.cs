using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

// Uso: OcrProbe <imagen.png>
// Imprime, por receta de recorte/escala, las líneas que el OCR de Windows lee.

var path = args.Length > 0 ? args[0] : throw new ArgumentException("usage: OcrProbe <png>");
var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(Path.GetFullPath(path)),
    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
var source = new FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
int width = source.PixelWidth, height = source.PixelHeight;
var pixels = new byte[width * height * 4];
source.CopyPixels(pixels, width * 4, 0);
Console.WriteLine($"image {width}x{height}");

var engine = OcrEngine.TryCreateFromUserProfileLanguages()
    ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
Console.WriteLine($"engine: {engine?.RecognizerLanguage.LanguageTag ?? "NONE"}  maxDim: {OcrEngine.MaxImageDimension}");
if (engine is null)
    return;

await Run("full 1x", (pixels, width, height));
await Run("center 80x70 1x", Crop(0.10, 0.15, 0.80, 0.70, 1.0));
await Run("center 80x70 fit-scale", CropFit(0.10, 0.15, 0.80, 0.70));
await Run("title band 2x", Crop(0.15, 0.25, 0.70, 0.30, 2.0));
await Run("title band 3x", Crop(0.15, 0.25, 0.70, 0.30, 3.0));
await Run("card L 3x", Crop(0.18, 0.20, 0.22, 0.50, 3.0));
await Run("card M 3x", Crop(0.39, 0.20, 0.22, 0.50, 3.0));
await Run("card R 3x", Crop(0.60, 0.20, 0.22, 0.50, 3.0));
await Run("center 70x50 2x", Crop(0.15, 0.20, 0.70, 0.50, 2.0));
return;

(byte[] Buf, int W, int H) Crop(double rx, double ry, double rw, double rh, double scale)
{
    int cx = (int)(width * rx), cy = (int)(height * ry);
    int cw = (int)(width * rw), ch = (int)(height * rh);
    var maxSide = Math.Max(cw, ch) * scale;
    if (maxSide > OcrEngine.MaxImageDimension)
        scale = OcrEngine.MaxImageDimension / (double)Math.Max(cw, ch);
    int ow = (int)(cw * scale), oh = (int)(ch * scale);
    var output = new byte[ow * oh * 4];
    for (var y = 0; y < oh; y++)
    {
        var sy = cy + (int)(y / scale);
        for (var x = 0; x < ow; x++)
        {
            var sx = cx + (int)(x / scale);
            var src = (sy * width + sx) * 4;
            var dst = (y * ow + x) * 4;
            output[dst] = pixels[src];
            output[dst + 1] = pixels[src + 1];
            output[dst + 2] = pixels[src + 2];
            output[dst + 3] = pixels[src + 3];
        }
    }
    return (output, ow, oh);
}

(byte[] Buf, int W, int H) CropFit(double rx, double ry, double rw, double rh)
{
    int cw = (int)(width * rw), ch = (int)(height * rh);
    var scale = Math.Min(3.0, OcrEngine.MaxImageDimension / (double)Math.Max(cw, ch));
    return Crop(rx, ry, rw, rh, scale);
}

async Task Run(string label, (byte[] Buf, int W, int H) frame)
{
    using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(frame.Buf.AsBuffer(),
        BitmapPixelFormat.Bgra8, frame.W, frame.H, BitmapAlphaMode.Ignore);
    var result = await engine.RecognizeAsync(bitmap);
    var lines = result.Lines.Select(l => l.Text).Where(t => t.Trim().Length > 1).ToList();
    Console.WriteLine($"--- {label} ({frame.W}x{frame.H}): {lines.Count} lines");
    Console.WriteLine("    " + string.Join(" | ", lines.Take(25)));
}
