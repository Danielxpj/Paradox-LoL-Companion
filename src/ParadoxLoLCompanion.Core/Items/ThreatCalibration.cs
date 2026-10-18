using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;

namespace ParadoxLoLCompanion.Core.Items;

/// <summary>Una señal de amenaza en una partida: lo que se predijo temprano y lo que terminó pasando.</summary>
public sealed record ThreatSignalCheck(string Signal, double Early, double Final)
{
    public double Error => Math.Abs(Early - Final);
    /// <summary>Se anunció (μ ≥ 0.5) y no ocurrió.</summary>
    public bool FalseAlarm => Early >= 0.5 && Final < 0.5;
    /// <summary>Ocurrió y no se anunció.</summary>
    public bool Miss => Early < 0.5 && Final >= 0.5;
}

public sealed record ThreatCalibrationGame(string File, double EarlyTime, double FinalTime,
    IReadOnlyList<ThreatSignalCheck> Signals);

public sealed record ThreatSignalSummary(string Signal, int Games, double MeanAbsError, int FalseAlarms, int Misses);

/// <summary>
/// ¿Se materializó la amenaza que el analizador anunció al minuto 5? Compara los grados
/// difusos calculados sobre el snapshot temprano (con y sin proyección de la build enemiga)
/// contra los mismos grados sobre el ÚLTIMO snapshot de la partida (el inventario enemigo
/// final: lo que realmente apilaron/compraron). Valida el analizador directamente, que es
/// donde nacen la mayoría de las recomendaciones erradas, sin depender de que el jugador
/// haya comprado bien ni del resultado.
/// </summary>
public static class ThreatCalibration
{
    public const string ProjectedSuffix = "+proj";

    public static ThreatCalibrationGame? Evaluate(JournalGame game, IStaticData catalog, ItemsConfig config,
        double earlySeconds = 300)
    {
        if (game.Snapshots.Count < 2)
            return null;
        var early = game.Snapshots.LastOrDefault(s => s.State.GameData.GameTime <= earlySeconds) ?? game.Snapshots[0];
        var final = game.Snapshots[^1];
        if (final.State.GameData.GameTime <= early.State.GameData.GameTime)
            return null;

        var analyzer = new ThreatAnalyzer(catalog, config);
        var blind = analyzer.Analyze(early.State);
        var projected = analyzer.Analyze(early.State, early.Context.EnemyStats);
        var actual = analyzer.Analyze(final.State);

        var checks = new List<ThreatSignalCheck>
        {
            new("ArmorStack", blind.ArmorStack, actual.ArmorStack),
            new("ArmorStack" + ProjectedSuffix, Math.Max(projected.ArmorStack, projected.ProjectedArmorStack), actual.ArmorStack),
            new("MrStack", blind.MrStack, actual.MrStack),
            new("MrStack" + ProjectedSuffix, Math.Max(projected.MrStack, projected.ProjectedMrStack), actual.MrStack),
            new("EnemyAntiHeal", blind.EnemyAntiHeal, actual.EnemyAntiHeal),
            new("EnemyAntiHeal" + ProjectedSuffix, Math.Max(projected.EnemyAntiHeal, projected.ProjectedEnemyAntiHeal), actual.EnemyAntiHeal),
            new("Sustain", blind.Sustain, actual.Sustain),
            new("CritThreat", blind.CritThreat, actual.CritThreat),
            new("EnemyTankiness", blind.EnemyTankiness, actual.EnemyTankiness),
            new("Burst", blind.Burst, actual.Burst),
        };
        return new ThreatCalibrationGame(game.File, early.State.GameData.GameTime,
            final.State.GameData.GameTime, checks);
    }

    public static IReadOnlyList<ThreatSignalSummary> Summarize(IEnumerable<ThreatCalibrationGame> games)
    {
        var list = games.ToList();
        return list.SelectMany(g => g.Signals).GroupBy(c => c.Signal)
            .Select(g => new ThreatSignalSummary(g.Key, g.Count(), g.Average(c => c.Error),
                g.Count(c => c.FalseAlarm), g.Count(c => c.Miss)))
            .ToList();
    }
}
