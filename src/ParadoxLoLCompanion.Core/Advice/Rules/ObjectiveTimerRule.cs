using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Objectives;
using ParadoxLoLCompanion.Core.Util;

namespace ParadoxLoLCompanion.Core.Advice.Rules;

/// <summary>
/// Convierte los timers de objetivos (<see cref="ObjectiveTimers"/>) en consejos: avisa
/// cuando Dragón o Barón están por salir o ya disponibles. Tiempos configurables.
/// </summary>
public sealed class ObjectiveTimerRule : IAdviceRule
{
    private readonly ObjectivesConfig _config;

    public ObjectiveTimerRule(ObjectivesConfig? config = null) => _config = config ?? new ObjectivesConfig();

    public IEnumerable<AdviceItem> Evaluate(GameState state)
    {
        // Dragón/Barón solo existen en la Grieta: en ARAM u otros modos no hay timers.
        if (!string.Equals(state.GameData.GameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase))
            yield break;

        var dragon = ObjectiveTimers.Dragon(state, _config.DragonFirstSpawn, _config.DragonRespawn);
        yield return ToAdvice(dragon, "obj-dragon");

        var baron = ObjectiveTimers.Baron(state, _config.BaronFirstSpawn, _config.BaronRespawn);
        yield return ToAdvice(baron, "obj-baron");
    }

    private AdviceItem ToAdvice(ObjectiveTiming t, string key)
    {
        if (t.IsUp)
        {
            return new AdviceItem(AdviceCategory.Objective, AdviceSeverity.Warning, key,
                $"{t.Label} is up now ({TimeFmt.Clock(t.NextSpawn)}).");
        }

        var severity = t.Remaining <= _config.SoonThreshold ? AdviceSeverity.Important : AdviceSeverity.Info;
        return new AdviceItem(AdviceCategory.Objective, severity, key,
            $"{t.Label} spawns ~{TimeFmt.Clock(t.NextSpawn)} (in {t.Remaining:0}s, estimated).");
    }
}
