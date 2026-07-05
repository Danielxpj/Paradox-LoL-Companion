namespace ParadoxLoLCompanion.Core.Stats;

/// <summary>Bloque de una página de items (sección de la tienda).</summary>
public sealed record ItemSetBlock(string Title, IReadOnlyList<int> ItemIds);

/// <summary>Página de items lista para escribir en el cliente (modelo puro, sin JSON).</summary>
public sealed record ItemSetPage(string Title, IReadOnlyList<ItemSetBlock> Blocks);

/// <summary>
/// Compone hasta 3 variantes del meta desde las stats de OP.GG: core fijo +
/// candidato i-ésimo de cada slot tardío. Variantes duplicadas se descartan
/// (campeones con pocos candidatos emiten menos de 3 páginas).
/// </summary>
public static class ItemSetBuilder
{
    /// <summary>Prefijo que identifica nuestras páginas al reemplazarlas en el cliente.</summary>
    public const string TitlePrefix = "Paradox: ";
    private const int MaxVariants = 3;

    /// <param name="itemName">Resuelve id → nombre (catálogo) para nombrar las alternativas;
    /// sin él, las variantes caen a "Alt N".</param>
    public static IReadOnlyList<ItemSetPage> Build(ChampionBuildStats stats, string championName,
        Func<int, string?>? itemName = null)
    {
        if (stats.CoreItems is not { } core || core.ItemIds.Count == 0)
            return Array.Empty<ItemSetPage>();

        var pages = new List<ItemSetPage>();
        var seen = new HashSet<string>();
        for (var i = 0; i < MaxVariants; i++)
        {
            var blocks = new List<ItemSetBlock>();
            Add(blocks, "Starter", stats.Starter?.ItemIds);
            Add(blocks, "Boots", stats.Boots?.ItemIds);
            Add(blocks, "Core build", core.ItemIds);
            Add(blocks, "4th item", Candidate(stats.FourthItems, i));
            Add(blocks, "5th item", Candidate(stats.FifthItems, i));
            Add(blocks, "6th item", Candidate(stats.SixthItems, i));

            var signature = string.Join("|",
                blocks.Select(b => b.Title + ":" + string.Join(",", b.ItemIds)));
            if (!seen.Add(signature))
                continue;
            pages.Add(new ItemSetPage(
                $"{TitlePrefix}{championName} · {VariantLabel(stats, i, itemName)}", blocks));
        }
        return pages;
    }

    /// <summary>
    /// Nombre legible de la variante: la primera es "Meta"; las alternativas llevan
    /// el nombre del primer item que las distingue de la meta ("· Edge of Night").
    /// </summary>
    private static string VariantLabel(ChampionBuildStats stats, int variant,
        Func<int, string?>? itemName)
    {
        if (variant == 0)
            return "Meta";
        foreach (var slot in new[] { stats.FourthItems, stats.FifthItems, stats.SixthItems })
        {
            var pick = Candidate(slot, variant);
            var meta = Candidate(slot, 0);
            if (pick is null || meta is null || pick.SequenceEqual(meta))
                continue;
            if (itemName?.Invoke(pick[0]) is { Length: > 0 } name)
                return name;
            break;
        }
        return $"Alt {variant + 1}";
    }

    /// <summary>Candidato i-ésimo del slot; si el slot tiene menos, cae al último disponible.</summary>
    private static IReadOnlyList<int>? Candidate(IReadOnlyList<ItemSetStats> sets, int index) =>
        sets.Count == 0 ? null : sets[Math.Min(index, sets.Count - 1)].ItemIds;

    private static void Add(List<ItemSetBlock> blocks, string title, IReadOnlyList<int>? ids)
    {
        if (ids is { Count: > 0 })
            blocks.Add(new ItemSetBlock(title, ids));
    }
}
