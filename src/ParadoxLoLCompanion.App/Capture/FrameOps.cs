namespace ParadoxLoLCompanion.App.Capture;

/// <summary>
/// Operaciones de frame para mejorar el OCR. Todo en coordenadas RELATIVAS:
/// sin geometría absoluta de cartas, cualquier resolución sirve.
/// </summary>
public static class FrameOps
{
    /// <summary>
    /// OcrEngine.MaxImageDimension reporta 10000 (verificado en Windows 11,
    /// 2026-07-17); margen por si otras versiones lo bajan. El valor viejo
    /// (2600) era un mito: anulaba el reescalado y el OCR leía 0 líneas.
    /// </summary>
    private const int MaxOcrDimension = 9000;

    /// <summary>
    /// Recorte central reescalado x2, calibrado contra un frame real del picker
    /// de Mayhem a 1080p (tools/OcrProbe): las cartas viven en x 15-85 %,
    /// y 20-70 %, y a x2 los títulos pasan de ~14 px (ilegibles para el OCR de
    /// Windows: 0 líneas) a ~28 px (lee títulos Y descripciones completas).
    /// </summary>
    public static CapturedFrame CenterCropUpscaled(CapturedFrame frame)
    {
        int cropX = (int)(frame.Width * 0.15), cropW = (int)(frame.Width * 0.70);
        int cropY = (int)(frame.Height * 0.20), cropH = (int)(frame.Height * 0.50);

        var scale = 2;
        while (scale > 1 && (cropW * scale > MaxOcrDimension || cropH * scale > MaxOcrDimension))
            scale--;

        int outW = cropW * scale, outH = cropH * scale;
        var output = new byte[outW * outH * 4];
        for (var y = 0; y < outH; y++)
        {
            var srcY = cropY + y / scale;
            var srcRow = srcY * frame.Width;
            var dstRow = y * outW;
            for (var x = 0; x < outW; x++)
            {
                var src = (srcRow + cropX + x / scale) * 4;
                var dst = (dstRow + x) * 4;
                output[dst] = frame.Bgra[src];
                output[dst + 1] = frame.Bgra[src + 1];
                output[dst + 2] = frame.Bgra[src + 2];
                output[dst + 3] = frame.Bgra[src + 3];
            }
        }
        return new CapturedFrame(output, outW, outH);
    }
}
