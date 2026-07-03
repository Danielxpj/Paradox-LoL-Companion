using LoLAdvisor.Core.Models;

namespace LoLAdvisor.Core.Objectives;

/// <summary>Estimación de reaparición de un objetivo en un instante dado.</summary>
public sealed record ObjectiveTiming(string Label, double NextSpawn, double Remaining, bool IsUp);

/// <summary>
/// Calcula la reaparición estimada de objetivos neutrales a partir de los eventos.
/// Tiempos aproximados y dependientes del parche (defaults configurables).
/// Compartido por la regla de consejo y el panel de timers de la UI.
/// </summary>
public static class ObjectiveTimers
{
    public static ObjectiveTiming Dragon(GameState state, double firstSpawn = 300, double respawn = 300)
        => Compute(state, "DragonKill", "Dragon", firstSpawn, respawn);

    public static ObjectiveTiming Baron(GameState state, double firstSpawn = 1200, double respawn = 360)
        => Compute(state, "BaronKill", "Baron", firstSpawn, respawn);

    private static ObjectiveTiming Compute(
        GameState state, string eventName, string label, double firstSpawn, double respawn)
    {
        var now = state.GameData.GameTime;

        double lastKill = double.NaN;
        foreach (var e in state.Events.Events)
        {
            if (e.EventName == eventName && (double.IsNaN(lastKill) || e.EventTime > lastKill))
                lastKill = e.EventTime;
        }

        var nextSpawn = double.IsNaN(lastKill) ? firstSpawn : lastKill + respawn;
        var remaining = nextSpawn - now;
        return remaining <= 0
            ? new ObjectiveTiming(label, nextSpawn, 0, IsUp: true)
            : new ObjectiveTiming(label, nextSpawn, remaining, IsUp: false);
    }
}
