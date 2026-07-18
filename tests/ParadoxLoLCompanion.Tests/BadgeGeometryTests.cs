using ParadoxLoLCompanion.Core.Augments;

namespace ParadoxLoLCompanion.Tests;

/// <summary>
/// Mapeo de cajas OCR del recorte central reescalado de vuelta a píxeles nativos
/// del frame (= coordenadas de cliente del juego). Calibración real: 1080p,
/// crop x 15 % (288 px), y 20 % (216 px), scale 2 (FrameOps).
/// </summary>
public class BadgeGeometryTests
{
    [Fact]
    public void CalibratedCropTransform_MapsBoxBackToNative()
    {
        var t = new FrameTransform(OffsetX: 288, OffsetY: 216, Scale: 2);
        var native = t.ToNative(new OcrBox(X: 100, Y: 50, Width: 40, Height: 20));
        Assert.Equal(new OcrBox(338, 241, 20, 10), native);
    }

    [Fact]
    public void IdentityTransform_IsPassthrough()
    {
        var box = new OcrBox(10, 20, 30, 40);
        Assert.Equal(box, FrameTransform.Identity.ToNative(box));
    }
}
