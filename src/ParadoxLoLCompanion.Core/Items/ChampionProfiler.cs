using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Core.Items;

/// <summary>
/// Clasifica campeones en perfil de daño + arquetipo de build. El perfil de daño sale
/// de los valores <c>info.attack/magic</c> de Data Dragon (con overrides de config para
/// los casos donde engañan, p.ej. luchadores de daño mágico); el arquetipo, del tag
/// primario de clase cruzado con el perfil.
/// </summary>
public sealed class ChampionProfiler
{
    private readonly IStaticData _data;
    private readonly ItemsConfig _config;

    public ChampionProfiler(IStaticData data, ItemsConfig? config = null)
    {
        _data = data;
        _config = config ?? new ItemsConfig();
    }

    /// <summary>Resuelve el campeón de un jugador de la Live Client API (locale-independiente).</summary>
    public StaticChampion? Resolve(Player player) =>
        _data.ResolveChampion(player.ChampionName, player.RawChampionName);

    /// <summary>Perfil de un jugador; cae a <see cref="ChampionProfile.Fallback"/> si no se resuelve.</summary>
    public ChampionProfile Profile(Player player) => Profile(Resolve(player));

    /// <summary>
    /// Perfil del jugador con el arquetipo inferido de su inventario REAL. Cada item
    /// "vota" (ponderado por su costo) por el arquetipo que mejor lo explica, y el
    /// arquetipo por defecto del campeón actúa como prior configurable. Detecta builds
    /// alternativas (Garen tanque, Janna AP) y, como se recalcula en cada tick desde el
    /// inventario, vender todo y armar otra build cambia la detección sola.
    /// El tipo de daño NO se infiere: es del kit del campeón, no de las compras.
    /// </summary>
    public ChampionProfile ProfileWithInventory(Player player)
    {
        var baseline = Profile(player);
        if (!_config.DetectBuildFromItems)
            return baseline;

        var detected = DetectArchetype(player, baseline.Archetype);
        return detected == baseline.Archetype
            ? baseline
            : baseline with { Archetype = detected, InferredFromItems = true };
    }

    private BuildArchetype DetectArchetype(Player player, BuildArchetype fallback)
    {
        var weights = ArchetypeWeights.All
            .ToDictionary(a => a, a => ArchetypeWeights.For(a, _config));
        var evidence = ArchetypeWeights.All.ToDictionary(a => a, _ => 0.0);
        var anyEvidence = false;

        foreach (var slot in player.Items)
        {
            var item = _data.ItemById(slot.ItemID);
            // Botas, consumibles y items de quest no definen la build.
            if (item is null || item.IsBoots || item.Consumable
                || item.HasTag("Trinket") || item.HasTag("GoldPer"))
                continue;

            var fits = ArchetypeWeights.All
                .ToDictionary(a => a, a => item.Tags.Sum(t => weights[a].GetValueOrDefault(t)));
            var best = fits.Values.Max();
            if (best <= 0)
                continue;

            // Voto ponderado por oro: un item terminado pesa mucho más que un componente.
            var gold = item.GoldTotal * Math.Max(slot.Count, 1);
            anyEvidence = true;
            foreach (var archetype in ArchetypeWeights.All)
                evidence[archetype] += gold * (fits[archetype] / best);
        }

        if (!anyEvidence)
            return fallback;

        // El arquetipo por defecto arranca con ventaja: hace falta evidencia real para moverlo.
        evidence[fallback] += _config.BuildDetectionPriorGold;

        var winner = fallback;
        var winnerEvidence = evidence[fallback];
        foreach (var archetype in ArchetypeWeights.All)
        {
            if (evidence[archetype] > winnerEvidence)
            {
                winner = archetype;
                winnerEvidence = evidence[archetype];
            }
        }
        return winner;
    }

    public ChampionProfile Profile(StaticChampion? champ)
    {
        if (champ is null)
            return ChampionProfile.Fallback;

        var damage = DamageOf(champ);
        return new ChampionProfile(damage, ArchetypeOf(champ, damage));
    }

    private DamageProfile DamageOf(StaticChampion champ)
    {
        if (_config.DamageProfileOverrides.TryGetValue(champ.Key, out var forced)
            && Enum.TryParse<DamageProfile>(forced, ignoreCase: true, out var parsed))
            return parsed;

        var diff = champ.Info.Attack - champ.Info.Magic;
        return diff >= 3 ? DamageProfile.Physical
             : diff <= -3 ? DamageProfile.Magical
             : DamageProfile.Mixed;
    }

    private BuildArchetype ArchetypeOf(StaticChampion champ, DamageProfile damage)
    {
        if (_config.ArchetypeOverrides.TryGetValue(champ.Key, out var forced)
            && Enum.TryParse<BuildArchetype>(forced, ignoreCase: true, out var parsed))
            return parsed;

        return champ.PrimaryTag switch
        {
            "Marksman" => damage == DamageProfile.Magical ? BuildArchetype.ApFighter : BuildArchetype.Marksman,
            "Mage" => BuildArchetype.Mage,
            "Tank" => BuildArchetype.Tank,
            "Assassin" => damage == DamageProfile.Magical ? BuildArchetype.Mage : BuildArchetype.AdAssassin,
            "Fighter" => damage == DamageProfile.Magical ? BuildArchetype.ApFighter : BuildArchetype.AdFighter,
            "Support" => damage == DamageProfile.Physical ? BuildArchetype.AdAssassin
                       : champ.Info.Defense >= 7 ? BuildArchetype.Tank
                       : BuildArchetype.Enchanter,
            _ => damage == DamageProfile.Magical ? BuildArchetype.Mage : BuildArchetype.AdFighter,
        };
    }
}
