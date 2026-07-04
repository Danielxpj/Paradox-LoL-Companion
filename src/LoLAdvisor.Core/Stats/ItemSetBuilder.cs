namespace LoLAdvisor.Core.Stats;

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

    public static IReadOnlyList<ItemSetPage> Build(ChampionBuildStats stats, string championName)
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
            pages.Add(new ItemSetPage($"{TitlePrefix}{championName} #{pages.Count + 1}", blocks));
        }
        return pages;
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
