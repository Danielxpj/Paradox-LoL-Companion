using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Core.Items;

/// <summary>
/// Cuánto queda de partida. El asesor lo usa para descontar lo que no va a rendir a tiempo:
/// una Lágrima a los 14 min de un ARAM no se apila, y un item de 3.000 a los 17 min se compra
/// una vez o nunca. Estimación, no predicción: duración típica del modo, recortada si la
/// partida está desbalanceada o si cayó un inhibidor hace poco.
/// </summary>
public static class GameClock
{
    /// <param name="ahead">Grado [0,1] de ir adelante (ventaja propia); <paramref name="behind"/> ídem atrás.</param>
    public static double RemainingSeconds(GameState state, ItemsConfig config, bool isAram,
        double ahead = 0, double behind = 0)
    {
        var t = state.GameData.GameTime;
        var expected = isAram ? config.AramExpectedGameSeconds : config.RiftExpectedGameSeconds;
        expected *= 1 - config.LeadShortensGame * Fuzzy.Clamp01(Math.Max(ahead, behind));
        var remaining = Math.Max(expected - t, config.MinRemainingSeconds);

        foreach (var ev in state.Events.Events)
        {
            if (!string.Equals(ev.EventName, "InhibKilled", StringComparison.OrdinalIgnoreCase))
                continue;
            if (t - ev.EventTime <= config.InhibitorRecencySeconds)
                remaining = Math.Min(remaining, config.InhibitorRemainingSeconds);
        }
        return remaining;
    }
}
