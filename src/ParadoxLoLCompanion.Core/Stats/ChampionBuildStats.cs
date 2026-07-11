namespace ParadoxLoLCompanion.Core.Stats;

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
    /// <summary>Candidatos del 4.º item (sets de un solo item, rankeados por OP.GG).</summary>
    public IReadOnlyList<ItemSetStats> FourthItems { get; init; } = Array.Empty<ItemSetStats>();
    /// <summary>Candidatos del 5.º item.</summary>
    public IReadOnlyList<ItemSetStats> FifthItems { get; init; } = Array.Empty<ItemSetStats>();
    /// <summary>Candidatos del 6.º item (muestras chicas: la rampa de confianza los modera).</summary>
    public IReadOnlyList<ItemSetStats> SixthItems { get; init; } = Array.Empty<ItemSetStats>();
    /// <summary>
    /// Candidatos tardíos aplanados (4.º+5.º+6.º) para el prior del asesor. Se
    /// persiste en la caché (compatibilidad con entradas viejas que solo traían esto).
    /// </summary>
    public IReadOnlyList<ItemSetStats> LateItems { get; init; } = Array.Empty<ItemSetStats>();
    public RunePageStats? Runes { get; init; }
    public SkillOrderStats? Skills { get; init; }
    /// <summary>Win rate global del campeón en este rol (0 si no vino).</summary>
    public double WinRate { get; init; }

    /// <summary>
    /// Prior de un item: primero el core build (la señal fuerte), luego los candidatos
    /// tardíos. <c>null</c> si el item no aparece en las builds del campeón.
    /// Trae la fuente y la muestra porque están en ESCALAS distintas: el pick del
    /// core es la probabilidad de un combo de 3 items (~0.05–0.35) y el de un
    /// candidato tardío es de un item suelto (~0.1–0.5); el scoring los pondera aparte.
    /// </summary>
    public (double PickRate, double WinRate, int Play, bool IsCore)? ItemPriorFor(
        int itemId, int completedCount = -1)
    {
        // Con el core ya terminado (>=3 items completos), mirar primero la lista del slot
        // que corresponde (4.º/5.º/6.º): un item que aparece en varios slots hereda las
        // stats del slot correcto según el progreso, no siempre las del 4.º.
        if (completedCount >= 3)
        {
            var slot = completedCount switch
            {
                3 => FourthItems,
                4 => FifthItems,
                _ => SixthItems,
            };
            if (FindIn(slot, itemId) is { } slotHit)
                return slotHit;
            foreach (var other in new[] { FourthItems, FifthItems, SixthItems })
                if (FindIn(other, itemId) is { } otherHit)
                    return otherHit;
        }
        if (CoreItems is { } core && core.ItemIds.Contains(itemId))
            return (core.PickRate, core.WinRate, core.Play, true);
        // Fallback al aplanado (compat con cachés viejas que solo traían LateItems).
        foreach (var set in LateItems)
            if (set.ItemIds.Contains(itemId))
                return (set.PickRate, set.WinRate, set.Play, false);
        return null;
    }

    private static (double PickRate, double WinRate, int Play, bool IsCore)? FindIn(
        IReadOnlyList<ItemSetStats> sets, int itemId)
    {
        foreach (var s in sets)
            if (s.ItemIds.Contains(itemId))
                return (s.PickRate, s.WinRate, s.Play, false);
        return null;
    }
}
