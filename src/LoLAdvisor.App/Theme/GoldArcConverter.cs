using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LoLAdvisor.App.Theme;

/// <summary>
/// Fracción [0,1] → arco circular (geometría) para el anillo de progreso de oro
/// alrededor del ícono del item. Centro y radio fijos: caja de 58px, radio 26.
/// </summary>
public sealed class GoldArcConverter : IValueConverter
{
    private const double Center = 29;
    private const double Radius = 26;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fraction = value is double d ? Math.Clamp(d, 0, 1) : 0;
        if (fraction <= 0.001)
            return Geometry.Empty;
        if (fraction >= 0.999)
            return new EllipseGeometry(new System.Windows.Point(Center, Center), Radius, Radius);

        // Arranca arriba (−90°) y barre en sentido horario.
        var angle = fraction * 2 * Math.PI - Math.PI / 2;
        var start = new System.Windows.Point(Center, Center - Radius);
        var end = new System.Windows.Point(
            Center + Radius * Math.Cos(angle),
            Center + Radius * Math.Sin(angle));
        var figure = new PathFigure(start, new[]
        {
            (PathSegment)new ArcSegment(end, new System.Windows.Size(Radius, Radius),
                0, isLargeArc: fraction > 0.5, SweepDirection.Clockwise, isStroked: true),
        }, closed: false);
        return new PathGeometry(new[] { figure });
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
