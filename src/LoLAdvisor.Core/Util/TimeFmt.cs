using System.Globalization;

namespace LoLAdvisor.Core.Util;

/// <summary>Formateo de tiempos de partida (segundos → reloj mm:ss).</summary>
public static class TimeFmt
{
    /// <summary>Convierte segundos a "m:ss" (p. ej. 630 → "10:30"). Negativos se muestran como 0:00.</summary>
    public static string Clock(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds))
            seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        var totalMinutes = (int)ts.TotalMinutes;
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", totalMinutes, ts.Seconds);
    }
}
