using System.Globalization;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Core.Mayhem;

/// <summary>Estado y guía de augments para una partida de ARAM: Mayhem.</summary>
/// <param name="UnlockedPicks">Picks de augment desbloqueados hasta ahora (1 inicial + por nivel).</param>
/// <param name="NextPickLevel">Nivel del próximo pick, o <c>null</c> si ya están todos.</param>
/// <param name="PickWindowNow">Estás muerto: es el momento de elegir/ajustar augments.</param>
public sealed record MayhemAdvice(
    int UnlockedPicks,
    int TotalPicks,
    int? NextPickLevel,
    bool PickWindowNow,
    string StatusLine,
    string? PickNowLine,
    IReadOnlyList<string> Guidance);

/// <summary>
/// Asesor de ARAM: Mayhem. Las opciones de augment ofrecidas NO se exponen por API,
/// así que esto se centra en lo que sí es determinista: cuántos picks llevas
/// desbloqueados (inicial + niveles configurables), que se eligen estando muerto, y una
/// guía general según tu arquetipo y la amenaza enemiga real.
/// </summary>
public sealed class MayhemAdvisor
{
    private readonly ItemsConfig _itemsConfig;
    private readonly MayhemConfig _config;
    private readonly ChampionProfiler _profiler;
    private readonly ThreatAnalyzer _threats;

    public MayhemAdvisor(IStaticData data, ItemsConfig? itemsConfig = null, MayhemConfig? config = null)
    {
        _itemsConfig = itemsConfig ?? new ItemsConfig();
        _config = config ?? new MayhemConfig();
        _profiler = new ChampionProfiler(data, _itemsConfig);
        _threats = new ThreatAnalyzer(data, _itemsConfig, _profiler);
    }

    /// <summary>
    /// El llamador decide si la partida es Mayhem (cola de la LCU); acá solo se calcula.
    /// <c>null</c> si no hay datos del jugador activo. <paramref name="forcedArchetype"/>:
    /// arquetipo forzado por el jugador en la UI (anula la detección por inventario).
    /// </summary>
    public MayhemAdvice? Advise(GameState state, BuildArchetype? forcedArchetype = null)
    {
        var me = state.ActivePlayerEntry;
        if (me is null)
            return null;

        var level = Math.Max(state.ActivePlayer?.Level ?? 0, me.Level);
        var total = 1 + _config.PickLevels.Count;
        var unlocked = 1 + _config.PickLevels.Count(t => level >= t);
        int? next = _config.PickLevels.Where(t => level < t).OrderBy(t => t)
            .Select(t => (int?)t).FirstOrDefault();

        var status = next is int n
            ? $"Augment picks unlocked: {unlocked} of {total} — next at level {n}."
            : $"All {total} augment picks unlocked.";

        string? pickNow = null;
        if (me.IsDead)
        {
            var respawn = me.RespawnTimer > 0
                ? $" ({me.RespawnTimer.ToString("0", CultureInfo.InvariantCulture)}s to respawn)"
                : "";
            pickNow = $"You're dead{respawn} — augments can only be picked now.";
        }

        return new MayhemAdvice(unlocked, total, next, me.IsDead, status, pickNow,
            Guidance(state, me, forcedArchetype));
    }

    private IReadOnlyList<string> Guidance(GameState state, Player me, BuildArchetype? forcedArchetype)
    {
        var guidance = new List<string>();
        var profile = forcedArchetype is { } forced
            ? _profiler.Profile(me) with { Archetype = forced }
            : _profiler.ProfileWithInventory(me);
        guidance.Add(ArchetypeGuidance(profile.Archetype));

        var threat = _threats.Analyze(state);
        if (!threat.HasEnemies)
            return guidance;

        if (threat.PhysicalShare >= _itemsConfig.SkewedDamageShare)
            guidance.Add($"Enemy damage is {Pct(threat.PhysicalShare)} physical — " +
                         "defensive augments against physical damage are high value.");
        else if (threat.MagicalShare >= _itemsConfig.SkewedDamageShare)
            guidance.Add($"Enemy damage is {Pct(threat.MagicalShare)} magic — " +
                         "defensive augments against magic damage are high value.");

        // Mayhem es ARAM: usar el umbral de sustain ya rebajado del modo.
        if (threat.SustainScore >= _itemsConfig.SustainThreshold * _itemsConfig.AramSustainThresholdFactor)
            guidance.Add($"Enemies heal a lot ({threat.TopSustainName}) — anti-heal augments are strong.");

        if (threat.BurstScore >= _itemsConfig.BurstThreshold && profile.IsSquishy)
            guidance.Add($"A survival augment (shield/stasis/revive) helps against the burst from {threat.TopBurstName}.");

        return guidance;
    }

    private static string ArchetypeGuidance(BuildArchetype archetype) => archetype switch
    {
        BuildArchetype.Marksman =>
            "As a marksman, prioritize sustained-damage augments: attack speed, on-hit and crit.",
        BuildArchetype.Mage =>
            "As a mage, prioritize ability power, ability haste and mana augments.",
        BuildArchetype.AdAssassin =>
            "As an assassin, prioritize burst, lethality and mobility augments.",
        BuildArchetype.AdFighter =>
            "As a fighter, mix damage augments with durability — you want long fights.",
        BuildArchetype.ApFighter =>
            "As an AP fighter, mix ability power with durability augments.",
        BuildArchetype.Tank =>
            "As a tank, prioritize durability, healing and crowd-control augments.",
        BuildArchetype.Enchanter =>
            "As a support, prioritize heal & shield power and ability haste augments.",
        _ => "Pick augments that cover what your team is missing.",
    };

    private static string Pct(double share) =>
        (int)Math.Round(share * 100, MidpointRounding.AwayFromZero) + "%";
}
