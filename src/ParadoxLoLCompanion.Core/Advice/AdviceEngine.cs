using ParadoxLoLCompanion.Core.Advice.Rules;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Core.Advice;

/// <summary>
/// Ejecuta todas las reglas registradas contra un <see cref="GameState"/> y agrega
/// sus resultados, deduplicando por <see cref="AdviceItem.Key"/> (gana la primera regla).
/// </summary>
public sealed class AdviceEngine
{
    private readonly IReadOnlyList<IAdviceRule> _rules;

    public AdviceEngine(IEnumerable<IAdviceRule> rules) => _rules = rules.ToList();

    /// <summary>Motor con el set de reglas por defecto (config opcional).</summary>
    public static AdviceEngine CreateDefault(AdvisorConfig? config = null) =>
        new(DefaultRules(config ?? AdvisorConfig.Default));

    /// <summary>
    /// Motor por defecto más la recomendación de items (requiere datos estáticos
    /// cargados). <paramref name="forcedArchetype"/>: provider del override de build de
    /// la UI, para que el feed coincida con el panel del asesor.
    /// </summary>
    public static AdviceEngine CreateWith(IStaticData staticData, AdvisorConfig? config = null,
        Func<BuildArchetype?>? forcedArchetype = null)
    {
        config ??= AdvisorConfig.Default;
        return new(DefaultRules(config)
            .Append(new ItemRecommendationRule(staticData, config.Items, forcedArchetype)));
    }

    private static IEnumerable<IAdviceRule> DefaultRules(AdvisorConfig config) => new IAdviceRule[]
    {
        new ObjectiveTimerRule(config.Objectives),
        new GoldForItemRule(config.Gold),
        new CsPerMinuteRule(config.CsPerMinute),
    };

    public IReadOnlyList<AdviceItem> Evaluate(GameState state)
    {
        var result = new List<AdviceItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in _rules)
        {
            foreach (var item in rule.Evaluate(state))
            {
                if (seen.Add(item.Key))
                    result.Add(item);
            }
        }

        return result;
    }
}
