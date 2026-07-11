using System.Globalization;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Core.Advice.Rules;

/// <summary>
/// Adaptador fino sobre <see cref="ItemAdvisor"/>: publica en el feed de consejos lo
/// más accionable del plan (anti-curación urgente, la compra recomendada con su
/// siguiente componente, y botas). El panel "Asesor de items" muestra el plan completo.
/// Requiere el catálogo de Data Dragon; si no cargó, no emite nada.
/// </summary>
public sealed class ItemRecommendationRule : IAdviceRule
{
    private readonly ItemAdvisor _advisor;
    private readonly Func<BuildArchetype?>? _forcedArchetype;
    private readonly Func<ChampionBuildStats?>? _statsProvider;

    public ItemRecommendationRule(IStaticData data, ItemsConfig? config = null,
        Func<BuildArchetype?>? forcedArchetype = null,
        Func<ChampionBuildStats?>? statsProvider = null)
        : this(new ItemAdvisor(data, config), forcedArchetype, statsProvider)
    {
    }

    /// <param name="forcedArchetype">
    /// Provider del arquetipo forzado en la UI (se lee en cada tick): el feed debe
    /// decir lo mismo que el panel del asesor cuando el jugador elige build a mano.
    /// </param>
    /// <param name="statsProvider">
    /// Provider de las stats de op.gg cacheadas (se lee en cada tick): sin esto el feed
    /// pierde el prior estadístico y la regla "las botas meta mandan" no rige en el feed.
    /// </param>
    public ItemRecommendationRule(ItemAdvisor advisor,
        Func<BuildArchetype?>? forcedArchetype = null,
        Func<ChampionBuildStats?>? statsProvider = null)
    {
        _advisor = advisor;
        _forcedArchetype = forcedArchetype;
        _statsProvider = statsProvider;
    }

    public IEnumerable<AdviceItem> Evaluate(GameState state)
    {
        var plan = _advisor.Advise(state, _forcedArchetype?.Invoke(), _statsProvider?.Invoke());
        if (plan is null)
            yield break;

        var antiheal = plan.Recommendations.FirstOrDefault(r => r.Item.AppliesGrievousWounds);
        if (antiheal is not null)
            yield return new AdviceItem(AdviceCategory.Items, AdviceSeverity.Warning, "item-antiheal",
                $"Grievous Wounds: {Describe(antiheal.Item)} — {string.Join("; ", antiheal.Reasons)}.");
        else if (plan.UrgentPickup is { } urgent)
            yield return new AdviceItem(AdviceCategory.Items, AdviceSeverity.Warning, "item-antiheal",
                $"Grievous Wounds now: {Describe(urgent.Item)} — {urgent.Reason}.");

        var top = plan.Top;
        if (top is not null)
        {
            var line = $"Next item: {Describe(top.Item)} — {string.Join("; ", top.Reasons)}.";
            if (!top.Affordable && top.Purchase.NextComponent is { } component)
                line += $" Buy now: {component.Name} ({top.Purchase.NextComponentCost.ToString("N0", CultureInfo.InvariantCulture)}).";
            yield return new AdviceItem(AdviceCategory.Items, AdviceSeverity.Info, "item-next", line);
        }

        if (plan.Boots is not null)
            yield return new AdviceItem(AdviceCategory.Items, AdviceSeverity.Info, "item-boots",
                $"Boots: {Describe(plan.Boots.Boots)} — {plan.Boots.Reason}.");
    }

    private static string Describe(StaticItem item) =>
        $"{item.Name} ({item.GoldTotal.ToString("N0", CultureInfo.InvariantCulture)})";
}
