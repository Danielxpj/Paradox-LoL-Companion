namespace ParadoxLoLCompanion.Core.Augments;

public enum AugmentRarity { Silver = 0, Gold = 1, Prismatic = 2 }

/// <summary>Un augment de ARAM: Mayhem según el tier list de Blitz.gg.</summary>
public sealed class AugmentInfo
{
    /// <summary>Id del card de Blitz (coincide con el id de augment del juego).</summary>
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public AugmentRarity Rarity { get; init; }
    /// <summary>1 (mejor) a 5, o <c>null</c> si Blitz aún no lo rankeó.</summary>
    public int? Tier { get; init; }
    /// <summary>Texto plano, sin tags ni entidades.</summary>
    public string Description { get; init; } = "";
    /// <summary>Slug del icono en el CDN de Blitz, p.ej. "eureka_large.webp".</summary>
    public string IconSlug { get; init; } = "";
    /// <summary>Keys tipo ddragon de los campeones donde Blitz lo marca top.</summary>
    public IReadOnlyList<string> TopChampions { get; init; } = Array.Empty<string>();

    public string IconUrl => IconSlug.Length == 0
        ? "" : $"https://blitz-cdn.blitz.gg/blitz/lol/arena/augments/{IconSlug}";

    /// <summary>Etiqueta S/A/B/C/D del tier numérico (1..5), o "—" sin rankear.</summary>
    public string TierLabel => Tier switch
    { 1 => "S", 2 => "A", 3 => "B", 4 => "C", 5 => "D", _ => "—" };
}

/// <summary>Tier list completo de augments (fuente: Blitz.gg), cacheable por parche.</summary>
public sealed class AugmentTierList
{
    public IReadOnlyList<AugmentInfo> Augments { get; init; } = Array.Empty<AugmentInfo>();

    /// <summary>¿Blitz lista este augment entre los top para mi campeón?</summary>
    public bool FitsChampion(AugmentInfo augment, string? championKey) =>
        championKey is not null && augment.TopChampions.Contains(
            championKey, StringComparer.OrdinalIgnoreCase);
}
