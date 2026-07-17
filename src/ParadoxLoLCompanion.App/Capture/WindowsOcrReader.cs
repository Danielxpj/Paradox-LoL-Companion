using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ParadoxLoLCompanion.App.Capture;

/// <summary>
/// Windows.Media.Ocr sobre un frame capturado → líneas de texto. El motor viene
/// con Windows (sin dependencias); si el idioma del perfil no tiene OCR
/// instalado, cae a inglés, que además es el idioma de los nombres de Blitz.
/// </summary>
public static class WindowsOcrReader
{
    public static async Task<IReadOnlyList<string>> ReadLinesAsync(CapturedFrame frame)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null)
            return Array.Empty<string>();

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            frame.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, frame.Width, frame.Height,
            BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bitmap);
        return result.Lines.Select(l => l.Text).ToArray();
    }
}
