namespace ParadoxLoLCompanion.App.Capture;

/// <summary>
/// Operaciones de frame para mejorar el OCR. Todo en coordenadas RELATIVAS:
/// sin geometría absoluta de cartas, cualquier resolución sirve.
/// </summary>
public static class FrameOps
{
    /// <summary>Windows OCR rechaza imágenes por encima de ~2600 px de lado.</summary>
    private const int MaxOcrDimension = 2600;

    /// <summary>
    /// Recorte central (las cartas de augment viven en el centro de la pantalla)
    /// reescalado x2 vecino-más-cercano: los títulos pasan de ~20 px a ~40 px de
    /// alto, donde el OCR de Windows rinde mucho mejor. El factor se reduce si
    /// el resultado excede el máximo que acepta el motor.
    /// </summary>
    public static CapturedFrame CenterCropUpscaled(CapturedFrame frame)
    {
        int cropX = (int)(frame.Width * 0.10), cropW = (int)(frame.Width * 0.80);
        int cropY = (int)(frame.Height * 0.15), cropH = (int)(frame.Height * 0.70);

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
