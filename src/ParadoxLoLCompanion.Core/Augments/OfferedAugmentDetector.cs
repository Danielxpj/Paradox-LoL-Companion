namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>Un augment reconocido en pantalla durante la ventana de pick.
/// <paramref name="LineIndex"/>: índice de la línea OCR donde apareció el nombre
/// (-1 si no aplica) — el App lo usa para anclar el badge sobre la carta.</summary>
public sealed record OfferedAugment(
    int Id, string Name, AugmentRarity Rarity, int? Tier, string TierLabel, bool IsBest,
    int LineIndex = -1);

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
        var hits = new List<(AugmentInfo Info, int Line)>();
        for (var i = 0; i < ocrLines.Count; i++)
            if (Matcher.Match(ocrLines[i]) is { } augment
                && hits.All(h => h.Info.Id != augment.Id))
                hits.Add((augment, i));

        if (hits.Count < 2)
            return Array.Empty<OfferedAugment>();

        return hits
            .OrderBy(h => h.Info.Tier ?? int.MaxValue)
            .ThenByDescending(h => h.Info.Rarity)
            .Select((h, i) => new OfferedAugment(
                h.Info.Id, h.Info.Name, h.Info.Rarity, h.Info.Tier, h.Info.TierLabel,
                IsBest: i == 0, LineIndex: h.Line))
            .ToArray();
    }
}
