using System.Globalization;
using LoLAdvisor.Core.Config;
using LoLAdvisor.Core.DataDragon;
using LoLAdvisor.Core.Items;
using LoLAdvisor.Core.Models;

namespace LoLAdvisor.Core.Advice.Rules;

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

    public ItemRecommendationRule(IStaticData data, ItemsConfig? config = null,
        Func<BuildArchetype?>? forcedArchetype = null)
        : this(new ItemAdvisor(data, config), forcedArchetype)
    {
    }

    /// <param name="forcedArchetype">
    /// Provider del arquetipo forzado en la UI (se lee en cada tick): el feed debe
    /// decir lo mismo que el panel del asesor cuando el jugador elige build a mano.
    /// </param>
    public ItemRecommendationRule(ItemAdvisor advisor, Func<BuildArchetype?>? forcedArchetype = null)
    {
        _advisor = advisor;
        _forcedArchetype = forcedArchetype;
    }

    public IEnumerable<AdviceItem> Evaluate(GameState state)
    {
        var plan = _advisor.Advise(state, _forcedArchetype?.Invoke());
        if (plan is null)
            yield break;

        var antiheal = plan.Recommendations.FirstOrDefault(r => r.Item.AppliesGrievousWounds);
        if (antiheal is not null)
            yield return new AdviceItem(AdviceCategory.Items, AdviceSeverity.Warning, "item-antiheal",
                $"Grievous Wounds: {Describe(antiheal.Item)} — {string.Join("; ", antiheal.Reasons)}.");

        var top = plan.Top;
        if (top is not null)
        {
            var line = $"Next item: {Describe(top.Item)} — {string.Join("; ", top.Reasons)}.";
            if (!top.Affordable && top.Purchase.NextComponent is not null)
                line += $" Buy now: {Describe(top.Purchase.NextComponent)}.";
            yield return new AdviceItem(AdviceCategory.Items, AdviceSeverity.Info, "item-next", line);
        }

        if (plan.Boots is not null)
            yield return new AdviceItem(AdviceCategory.Items, AdviceSeverity.Info, "item-boots",
                $"Boots: {Describe(plan.Boots.Boots)} — {plan.Boots.Reason}.");
    }

    private static string Describe(StaticItem item) =>
        $"{item.Name} ({item.GoldTotal.ToString("N0", CultureInfo.InvariantCulture)})";
}
