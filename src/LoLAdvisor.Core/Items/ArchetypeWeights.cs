using LoLAdvisor.Core.Config;

namespace LoLAdvisor.Core.Items;

/// <summary>
/// Pesos por tag de item para cada arquetipo de build. Los usa tanto el puntaje del
/// asesor de items como la detección de build por inventario. Tags canónicos de ddragon
/// tras la normalización del catálogo ("Damage" = daño físico, "AbilityHaste" absorbe
/// CooldownReduction, "SpellBlock" absorbe MagicResist); "AttackDamage" queda como alias
/// defensivo por si el origen de datos cambia.
/// </summary>
internal static class ArchetypeWeights
{
    private static readonly Dictionary<BuildArchetype, IReadOnlyDictionary<string, double>> Defaults = new()
    {
        [BuildArchetype.Marksman] = W(("Damage", 3), ("AttackDamage", 3), ("CriticalStrike", 3),
            ("AttackSpeed", 2.5), ("OnHit", 2), ("LifeSteal", 1.5), ("ArmorPenetration", 1.5)),
        [BuildArchetype.Mage] = W(("SpellDamage", 3), ("MagicPenetration", 2), ("Mana", 1.5),
            ("AbilityHaste", 1.5), ("ManaRegen", 1), ("Health", 0.7)),
        [BuildArchetype.AdAssassin] = W(("Damage", 3), ("AttackDamage", 3), ("ArmorPenetration", 3),
            ("AbilityHaste", 1.5), ("LifeSteal", 0.5), ("Health", 0.5)),
        [BuildArchetype.AdFighter] = W(("Damage", 2.5), ("AttackDamage", 2.5), ("Health", 2),
            ("AbilityHaste", 1.5), ("AttackSpeed", 1.2), ("LifeSteal", 1),
            ("Armor", 0.5), ("SpellBlock", 0.5)),
        [BuildArchetype.ApFighter] = W(("SpellDamage", 3), ("Health", 1.8), ("AttackSpeed", 1.2),
            ("AbilityHaste", 1.2), ("MagicPenetration", 1.2)),
        [BuildArchetype.Tank] = W(("Health", 3), ("Armor", 2), ("SpellBlock", 2),
            ("AbilityHaste", 1), ("HealthRegen", 0.5), ("Aura", 0.5)),
        [BuildArchetype.Enchanter] = W(("AbilityHaste", 2), ("ManaRegen", 2),
            ("Health", 1.5), ("Aura", 1.5), ("Mana", 1), ("SpellDamage", 1),
            ("Armor", 0.5), ("SpellBlock", 0.5)),
    };

    /// <summary>Todos los arquetipos, en orden de declaración (estable para desempates).</summary>
    public static IReadOnlyList<BuildArchetype> All { get; } = Defaults.Keys.ToList();

    /// <summary>Pesos del arquetipo, con override opcional desde la config.</summary>
    public static IReadOnlyDictionary<string, double> For(BuildArchetype archetype, ItemsConfig config)
    {
        if (config.ArchetypeTagWeights is not null
            && config.ArchetypeTagWeights.TryGetValue(archetype.ToString(), out var custom))
            return new Dictionary<string, double>(custom, StringComparer.OrdinalIgnoreCase);
        return Defaults[archetype];
    }

    private static IReadOnlyDictionary<string, double> W(params (string Tag, double Weight)[] entries)
    {
        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tag, weight) in entries)
            dict[tag] = weight;
        return dict;
    }
}
