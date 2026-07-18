namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>Caja de una línea OCR en píxeles enteros (sin tipos de Windows en Core).</summary>
public readonly record struct OcrBox(int X, int Y, int Width, int Height);

/// <summary>
/// Transformación frame→recorte usada por el OCR de pase 2: invertirla lleva una
/// caja detectada en el recorte reescalado de vuelta a píxeles nativos del frame
/// (que son coordenadas de cliente de la ventana del juego).
/// </summary>
public readonly record struct FrameTransform(int OffsetX, int OffsetY, int Scale)
{
    public static readonly FrameTransform Identity = new(0, 0, 1);

    public OcrBox ToNative(OcrBox box) => new(
        OffsetX + box.X / Scale,
        OffsetY + box.Y / Scale,
        box.Width / Scale,
        box.Height / Scale);
}
