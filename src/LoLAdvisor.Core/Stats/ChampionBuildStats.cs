namespace LoLAdvisor.Core.Stats;

/// <summary>Un conjunto de items con sus estadísticas (core build, botas, starter o candidato tardío).</summary>
public sealed record ItemSetStats(IReadOnlyList<int> ItemIds, double PickRate, int Play, int Win)
{
    public double WinRate => Play > 0 ? (double)Win / Play : 0;
}

/// <summary>Página de runas más popular (ids de perks + nombres ya localizados por OP.GG).</summary>
public sealed record RunePageStats(
    int PrimaryPageId, string PrimaryPageName,
    IReadOnlyList<int> PrimaryRuneIds, IReadOnlyList<string> PrimaryRuneNames,
    int SecondaryPageId, string SecondaryPageName,
    IReadOnlyList<int> SecondaryRuneIds, IReadOnlyList<string> SecondaryRuneNames,
    IReadOnlyList<int> StatModIds, double PickRate);

/// <summary>Orden de subida de habilidades más popular (letras Q/W/E/R, 15 niveles).</summary>
public sealed record SkillOrderStats(IReadOnlyList<string> Order, double PickRate);

/// <summary>
/// Estadísticas agregadas de build para un campeón+rol+modo (fuente: OP.GG).
/// Serializable a JSON tal cual para la caché por parche.
/// </summary>
public sealed class ChampionBuildStats
{
    public string ChampionKey { get; init; } = "";
    public string GameMode { get; init; } = "";
    public string Position { get; init; } = "";
    public ItemSetStats? CoreItems { get; init; }
    public ItemSetStats? Boots { get; init; }
    public ItemSetStats? Starter { get; init; }
    /// <summary>Candidatos de 4.º/5.º item (sets de un solo item, típicamente).</summary>
    public IReadOnlyList<ItemSetStats> LateItems { get; init; } = Array.Empty<ItemSetStats>();
    public RunePageStats? Runes { get; init; }
    public SkillOrderStats? Skills { get; init; }
    /// <summary>Win rate global del campeón en este rol (0 si no vino).</summary>
    public double WinRate { get; init; }

    /// <summary>
    /// Prior de un item: primero el core build (la señal fuerte), luego los candidatos
    /// tardíos. <c>null</c> si el item no aparece en las builds del campeón.
    /// </summary>
    public (double PickRate, double WinRate)? ItemPriorFor(int itemId)
    {
        if (CoreItems is { } core && core.ItemIds.Contains(itemId))
            return (core.PickRate, core.WinRate);
        foreach (var set in LateItems)
            if (set.ItemIds.Contains(itemId))
                return (set.PickRate, set.WinRate);
        return null;
    }
}
