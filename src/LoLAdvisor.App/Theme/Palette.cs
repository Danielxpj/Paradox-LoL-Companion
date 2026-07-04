using System.Windows.Media;

namespace LoLAdvisor.App.Theme;

/// <summary>
/// Colores del tema "Hextech HUD" (oscuro) para uso desde ViewModels.
/// Deben mantenerse en sintonía con los brushes de App.xaml.
/// </summary>
public static class Palette
{
    public static readonly Brush Blue = Frozen("#5383E8");
    public static readonly Brush Green = Frozen("#0AC8B9");
    public static readonly Brush Amber = Frozen("#E8A33D");
    public static readonly Brush Red = Frozen("#E84057");
    public static readonly Brush Purple = Frozen("#B07CE8");
    public static readonly Brush Gold = Frozen("#C8AA6E");
    public static readonly Brush Muted = Frozen("#8CA3BC");
    public static readonly Brush TeamOrder = Frozen("#5383E8"); // azul
    public static readonly Brush TeamChaos = Frozen("#E84057"); // rojo

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
