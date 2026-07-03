using LoLAdvisor.Core.DataDragon;

namespace LoLAdvisor.Core.Items;

/// <summary>Plan de compra hacia un item objetivo: cuánto falta y qué comprar ya.</summary>
/// <param name="Target">El item final al que se apunta.</param>
/// <param name="RemainingCost">Oro que falta gastar (descontando componentes ya comprados).</param>
/// <param name="NextComponent">La mejor compra inmediata con el oro actual (el objetivo mismo si alcanza).</param>
public sealed record PurchasePlan(StaticItem Target, int RemainingCost, StaticItem? NextComponent)
{
    public bool CanFinishNow => NextComponent is not null && NextComponent.Id == Target.Id;
}

/// <summary>
/// Recorre el árbol de construcción (<c>from</c>) de un item descontando los componentes
/// que ya están en el inventario, y elige el componente más caro que el oro actual
/// permite completar — la compra que menos oro deja "muerto" en el bolsillo.
/// </summary>
public static class BuildPathPlanner
{
    public static PurchasePlan Plan(IStaticData data, StaticItem target, IEnumerable<int> ownedItemIds, double gold)
    {
        // Multiconjunto: cada item del inventario solo puede descontar una vez.
        var owned = new Dictionary<int, int>();
        foreach (var id in ownedItemIds)
            owned[id] = owned.GetValueOrDefault(id) + 1;

        // RemainingCost consume del multiconjunto: se sondea sobre una copia.
        var remaining = RemainingCost(data, target, new Dictionary<int, int>(owned));
        if (remaining <= gold)
            return new PurchasePlan(target, remaining, target);

        var next = BestAffordableComponent(data, target, owned, gold);
        return new PurchasePlan(target, remaining, next);
    }

    /// <summary>Oro que falta para terminar <paramref name="item"/>, consumiendo componentes poseídos.</summary>
    private static int RemainingCost(IStaticData data, StaticItem item, Dictionary<int, int> owned)
    {
        if (Consume(owned, item.Id))
            return 0;
        if (item.From.Count == 0)
            return item.GoldTotal;

        var components = Components(data, item);
        var recipe = item.GoldTotal - components.Sum(c => c.GoldTotal);
        return Math.Max(recipe, 0) + components.Sum(c => RemainingCost(data, c, owned));
    }

    /// <summary>
    /// El componente faltante más caro que se puede completar con el oro actual,
    /// bajando recursivamente cuando un componente directo no alcanza.
    /// </summary>
    private static StaticItem? BestAffordableComponent(
        IStaticData data, StaticItem item, Dictionary<int, int> owned, double gold)
    {
        StaticItem? best = null;
        var bestCost = -1;

        foreach (var component in Components(data, item))
        {
            // Copia para sondear sin consumir: cada rama evalúa su propio faltante.
            var probe = new Dictionary<int, int>(owned);
            var cost = RemainingCost(data, component, probe);
            if (cost == 0)
            {
                Consume(owned, component.Id); // ya está en el inventario: descartarlo de otras ramas
                continue;
            }

            var candidate = cost <= gold
                ? component
                : BestAffordableComponent(data, component, new Dictionary<int, int>(owned), gold);

            if (candidate is not null)
            {
                var candidateCost = candidate.Id == component.Id ? cost : candidate.GoldTotal;
                if (candidateCost > bestCost)
                {
                    best = candidate;
                    bestCost = candidateCost;
                }
            }
        }

        return best;
    }

    private static List<StaticItem> Components(IStaticData data, StaticItem item) =>
        item.From.Select(data.ItemById).Where(c => c is not null).Cast<StaticItem>().ToList();

    private static bool Consume(Dictionary<int, int> owned, int id)
    {
        if (owned.GetValueOrDefault(id) <= 0)
            return false;
        owned[id]--;
        return true;
    }
}
