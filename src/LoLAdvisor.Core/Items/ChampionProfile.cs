namespace LoLAdvisor.Core.Items;

/// <summary>Tipo de daño predominante de un campeón.</summary>
public enum DamageProfile
{
    Physical,
    Magical,
    Mixed,
}

/// <summary>Arquetipo de build: define qué bolsa de items compra un campeón.</summary>
public enum BuildArchetype
{
    Marksman,
    Mage,
    AdAssassin,
    AdFighter,
    ApFighter,
    Tank,
    Enchanter,
}

/// <summary>Perfil de build de un campeón: qué daño hace, qué compra y qué tan frágil es.</summary>
public sealed record ChampionProfile(DamageProfile Damage, BuildArchetype Archetype)
{
    /// <summary>
    /// El arquetipo se infirió del inventario real del jugador (build alternativa,
    /// p.ej. Garen tanque o Janna AP) en vez del perfil por defecto del campeón.
    /// </summary>
    public bool InferredFromItems { get; init; }

    /// <summary>Frágil: muere rápido si lo enfocan (define cuánto pesa la defensa situacional).</summary>
    public bool IsSquishy => Archetype is BuildArchetype.Marksman or BuildArchetype.Mage
        or BuildArchetype.AdAssassin or BuildArchetype.Enchanter;

    /// <summary>Hace daño físico relevante (penetración de armadura le sirve).</summary>
    public bool DealsPhysical => Damage is DamageProfile.Physical or DamageProfile.Mixed;

    /// <summary>Hace daño mágico relevante (penetración mágica le sirve).</summary>
    public bool DealsMagical => Damage is DamageProfile.Magical or DamageProfile.Mixed;

    /// <summary>Perfil por defecto cuando el campeón no se pudo resolver: bruiser mixto.</summary>
    public static ChampionProfile Fallback { get; } = new(DamageProfile.Mixed, BuildArchetype.AdFighter);
}
