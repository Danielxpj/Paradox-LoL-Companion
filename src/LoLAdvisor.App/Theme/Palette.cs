using System.Windows.Media;

namespace LoLAdvisor.App.Theme;

/// <summary>Colores del tema (estilo Data Studio / Looker) para uso desde ViewModels.</summary>
public static class Palette
{
    public static readonly Brush Blue = Frozen("#1A73E8");
    public static readonly Brush Green = Frozen("#34A853");
    public static readonly Brush Amber = Frozen("#F9AB00");
    public static readonly Brush Red = Frozen("#EA4335");
    public static readonly Brush Purple = Frozen("#9334E8");
    public static readonly Brush Muted = Frozen("#5F6368");
    public static readonly Brush TeamOrder = Frozen("#1A73E8"); // azul
    public static readonly Brush TeamChaos = Frozen("#EA4335"); // rojo

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
