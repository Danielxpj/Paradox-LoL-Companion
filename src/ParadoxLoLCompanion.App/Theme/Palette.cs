using System.Windows.Media;

namespace ParadoxLoLCompanion.App.Theme;

/// <summary>
/// Colores del tema "Tactical HUD" (oscuro, verde neón) para uso desde ViewModels.
/// Deben mantenerse en sintonía con los brushes de App.xaml.
/// </summary>
public static class Palette
{
    public static readonly Brush Blue = Frozen("#3AA6FF");
    public static readonly Brush Green = Frozen("#2BE38B");
    public static readonly Brush Amber = Frozen("#F0B44C");
    public static readonly Brush Red = Frozen("#FF4A3C");
    public static readonly Brush Purple = Frozen("#B07CE8");
    public static readonly Brush Gold = Frozen("#F0B44C");
    public static readonly Brush Cyan = Frozen("#3ADFEF");
    public static readonly Brush Muted = Frozen("#8CA396");
    public static readonly Brush TeamOrder = Frozen("#3AA6FF"); // azul
    public static readonly Brush TeamChaos = Frozen("#FF4A3C"); // rojo

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
