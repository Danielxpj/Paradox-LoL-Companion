using System.Text.Json;
using System.Text.Json.Nodes;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;

namespace ParadoxLoLCompanion.Core.Items;

/// <summary>Una configuración candidata con nombre ("antiHealMag=3").</summary>
public sealed record SweepVariant(string Name, ItemsConfig Config);

/// <summary>
/// Resultado de una variante sobre el corpus: cuántas decisiones (ticks con una compra
/// completa posterior) y en cuántas el top mostrado contenía lo que el jugador compró.
/// </summary>
public sealed record SweepResult(string Variant, int Games, int Decisions, int Agreed)
{
    public double Agreement => Decisions > 0 ? (double)Agreed / Decisions : 0;
}

/// <summary>
/// Barrido de calibración offline sobre los diarios reales: reproduce cada snapshot con
/// una configuración candidata y mide <b>agreement@k</b> contra el próximo item COMPLETO
/// que el jugador compró después de ese tick. Reemplaza "13 magnitudes a mano" por
/// "13 magnitudes medidas contra tus partidas". Mide acuerdo con el jugador, no causalidad
/// sobre el resultado: un jugador que compra mal baja el acuerdo de una configuración buena.
/// </summary>
public static class CalibrationSweep
{
    /// <summary>
    /// Variantes desde un JSON <c>{"campo":[v1,v2,…], "otro":[…]}</c>: una variante por
    /// valor por campo (barrido unidimensional), cada una = base + ese único cambio. La
    /// base misma va primero como "baseline".
    /// </summary>
    public static IReadOnlyList<SweepVariant> Variants(ItemsConfig baseline, JsonObject sweep)
    {
        var variants = new List<SweepVariant> { new("baseline", baseline) };
        foreach (var (field, values) in sweep)
        {
            if (values is not JsonArray array)
                throw new InvalidDataException($"'{field}' must be an array of values.");
            foreach (var value in array)
                variants.Add(new($"{field}={value?.ToJsonString()}",
                    Overlay(baseline, new JsonObject { [field] = value?.DeepClone() })));
        }
        return variants;
    }

    /// <summary>Copia de <paramref name="baseline"/> con las propiedades de <paramref name="overrides"/> aplicadas (nombres sin distinguir mayúsculas).</summary>
    public static ItemsConfig Overlay(ItemsConfig baseline, JsonObject overrides)
    {
        var node = JsonSerializer.SerializeToNode(baseline)!.AsObject();
        foreach (var (key, value) in overrides)
        {
            var existing = node.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Key;
            if (existing is null)
                throw new InvalidDataException($"Unknown ItemsConfig property '{key}'.");
            node[existing] = value?.DeepClone();
        }
        return node.Deserialize<ItemsConfig>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Overlay produced an empty configuration.");
    }

    /// <summary>
    /// Evalúa una variante. <paramref name="catalogFor"/> construye el catálogo para la
    /// configuración (los flags derivados por keywords dependen de ella); se llama una vez.
    /// </summary>
    public static SweepResult Evaluate(SweepVariant variant, IEnumerable<JournalGame> games,
        Func<ItemsConfig, IStaticData> catalogFor, int topK = 3)
    {
        var catalog = catalogFor(variant.Config);
        int gamesCount = 0, decisions = 0, agreed = 0;
        foreach (var game in games)
        {
            if (!game.Replayable)
                continue;
            gamesCount++;
            foreach (var snapshot in game.Snapshots)
            {
                if (NextCompletedPurchase(game, snapshot.State.GameData.GameTime, catalog, variant.Config) is not { } target)
                    continue;
                var result = RecommendationReplay.Evaluate(snapshot, catalog, variant.Config);
                decisions++;
                if (result.ReplayedIds.Take(topK).Contains(target))
                    agreed++;
            }
        }
        return new SweepResult(variant.Name, gamesCount, decisions, agreed);
    }

    /// <summary>El primer item COMPLETO (no componente, no botas, no consumible) comprado después de <paramref name="time"/>.</summary>
    public static int? NextCompletedPurchase(JournalGame game, double time, IStaticData catalog, ItemsConfig config)
    {
        foreach (var buy in game.Buys.Where(b => b.Time > time).OrderBy(b => b.Time))
        {
            if (catalog.ItemById(buy.ItemId) is { } item && !item.BuildsIntoSomething && !item.IsBoots
                && !item.Consumable && item.GoldTotal >= config.MinCompletedItemGold)
                return item.Id;
        }
        return null;
    }
}
