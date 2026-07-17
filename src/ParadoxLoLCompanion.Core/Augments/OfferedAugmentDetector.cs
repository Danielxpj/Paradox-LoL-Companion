namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>Un augment reconocido en pantalla durante la ventana de pick.</summary>
public sealed record OfferedAugment(
    int Id, string Name, AugmentRarity Rarity, int? Tier, string TierLabel, bool IsBest);

/// <summary>
/// De líneas de OCR del frame completo a los augments ofrecidos. Sin geometría
/// de cartas: los nombres son evidencia suficiente y sobreviven cambios de
/// resolución e idioma. Con menos de 2 matches no se afirma nada (una sola
/// línea puede ser el tooltip de un augment ya tomado).
/// </summary>
public sealed class OfferedAugmentDetector(AugmentTierList list)
{
    /// <summary>Expuesto para inyectarle aliases localizados (cdragon).</summary>
    public AugmentNameMatcher Matcher { get; } = new(list);

    public IReadOnlyList<OfferedAugment> Detect(IReadOnlyList<string> ocrLines)
    {
        var hits = new List<AugmentInfo>();
        foreach (var line in ocrLines)
            if (Matcher.Match(line) is { } augment && hits.All(h => h.Id != augment.Id))
                hits.Add(augment);

        if (hits.Count < 2)
            return Array.Empty<OfferedAugment>();

        return hits
            .OrderBy(a => a.Tier ?? int.MaxValue)
            .ThenByDescending(a => a.Rarity)
            .Select((a, i) => new OfferedAugment(
                a.Id, a.Name, a.Rarity, a.Tier, a.TierLabel, IsBest: i == 0))
            .ToArray();
    }
}
