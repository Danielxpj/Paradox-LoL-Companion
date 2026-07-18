using System.Runtime.InteropServices.WindowsRuntime;
using ParadoxLoLCompanion.Core.Augments;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ParadoxLoLCompanion.App.Capture;

/// <summary>Línea OCR con su caja (unión de las cajas de sus palabras), en
/// píxeles del frame que se le pasó al motor.</summary>
public sealed record OcrLineResult(string Text, OcrBox Box);

/// <summary>
/// Windows.Media.Ocr sobre un frame capturado → líneas de texto con geometría.
/// El motor viene con Windows (sin dependencias); si el idioma del perfil no
/// tiene OCR instalado, cae a inglés, que además es el idioma de Blitz.
/// </summary>
public static class WindowsOcrReader
{
    public static async Task<IReadOnlyList<string>> ReadLinesAsync(CapturedFrame frame)
        => (await ReadLinesWithBoxesAsync(frame)).Select(l => l.Text).ToArray();

    public static async Task<IReadOnlyList<OcrLineResult>> ReadLinesWithBoxesAsync(
        CapturedFrame frame)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null)
            return Array.Empty<OcrLineResult>();

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            frame.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, frame.Width, frame.Height,
            BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bitmap);
        return result.Lines.Select(line =>
        {
            double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                left = Math.Min(left, r.X);
                top = Math.Min(top, r.Y);
                right = Math.Max(right, r.X + r.Width);
                bottom = Math.Max(bottom, r.Y + r.Height);
            }
            var box = line.Words.Count == 0
                ? default
                : new OcrBox((int)left, (int)top, (int)(right - left), (int)(bottom - top));
            return new OcrLineResult(line.Text, box);
        }).ToArray();
    }
}
