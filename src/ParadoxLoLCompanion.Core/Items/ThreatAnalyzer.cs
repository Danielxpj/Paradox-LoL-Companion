using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Core.Items;

/// <summary>
/// Analiza al equipo enemigo y produce un <see cref="TeamThreat"/>. Cada enemigo pesa
/// según lo fed que va (kills/assists/CS/nivel menos muertes), de modo que el split de
/// daño y las amenazas top reflejen quién está ganando la partida, no solo la
/// composición del draft. Además de los valores crudos (para mensajes), produce los
/// <b>grados difusos</b> ∈ [0,1] que el scoring usa en vez de umbrales duros: el umbral
/// de la v2 se preserva como el cruce μ≈0.5 de cada rampa.
/// </summary>
public sealed class ThreatAnalyzer
{
    private readonly IStaticData _data;
    private readonly ItemsConfig _config;
    private readonly ChampionProfiler _profiler;

    // Bandas de las rampas de skew de daño (0.5 = parejo → μ=0; 0.75 = casi todo un tipo → μ=1).
    private const double SkewFoot = 0.50, SkewShoulder = 0.75;

    public ThreatAnalyzer(IStaticData data, ItemsConfig? config = null, ChampionProfiler? profiler = null)
    {
        _data = data;
        _config = config ?? new ItemsConfig();
        _profiler = profiler ?? new ChampionProfiler(data, _config);
    }

    public TeamThreat Analyze(GameState state)
    {
        var me = state.ActivePlayerEntry;
        if (me is null || string.IsNullOrEmpty(me.Team))
            return TeamThreat.None;

        var enemies = state.AllPlayers
            .Where(p => !string.IsNullOrEmpty(p.Team) && p.Team != me.Team)
            .ToList();
        if (enemies.Count == 0)
            return TeamThreat.None;

        var isAram = state.GameData.MapNumber == 12
            || string.Equals(state.GameData.GameMode, "ARAM", StringComparison.OrdinalIgnoreCase);
        var sustainThr = isAram
            ? _config.SustainThreshold * _config.AramSustainThresholdFactor
            : _config.SustainThreshold;

        double totalW = 0, physical = 0, magical = 0, autoAttack = 0, sustain = 0;
        double bonusArmor = 0, bonusMr = 0, bonusHealth = 0;
        double critSum = 0, gwHolderW = 0, pctHpTrueW = 0, hardEngageW = 0;
        double topW = 0, minW = double.MaxValue, topPhysW = 0, topMagW = 0, topSustainW = 0, topBurstW = 0;
        string? topName = null, topPhys = null, topMag = null, topSustain = null, topBurst = null;
        var burstDamage = DamageProfile.Mixed;
        string? suppression = null;
        var heavyCc = 0;
        var shields = false;

        foreach (var enemy in enemies)
        {
            var w = Weight(enemy);
            totalW += w;

            var champ = _profiler.Resolve(enemy);
            var profile = _profiler.Profile(champ);
            var label = Label(enemy);

            // Stats e inversión ofensiva de sus items: para leer su daño REAL, no solo el kit.
            double adGold = 0, apGold = 0;
            var holdsGw = false;
            foreach (var owned in enemy.Items)
            {
                var item = _data.ItemById(owned.ItemID);
                if (item is null)
                    continue;
                var count = Math.Max(owned.Count, 1);
                bonusArmor += item.Armor * count;
                bonusMr += item.SpellBlock * count;
                bonusHealth += item.Health * count;
                critSum += item.CritChance * count;
                holdsGw |= item.AppliesGrievousWounds;
                // Valor-oro ofensivo (mismas constantes que ItemAdvisor.Efficiency).
                adGold += (item.AttackDamage * 35 + item.AttackSpeedPct * 2500 + item.CritChance * 4000) * count;
                apGold += item.AbilityPower * 21.75 * count;
            }
            if (holdsGw)
                gwHolderW += w;

            // Split de daño: prior del kit mezclado con lo que REALMENTE compró. Un "Mixed"
            // que fue full-AP deja de contar 50/50; un kit sesgado se corrige menos (tope 0.5
            // del blend) porque su daño base pesa aunque compre algo off-build.
            var kitPhys = profile.Damage switch
            {
                DamageProfile.Physical => 1.0,
                DamageProfile.Magical => 0.0,
                _ => 0.5,
            };
            var phys = kitPhys;
            var offGold = adGold + apGold;
            if (offGold > 0)
            {
                var blend = Fuzzy.Ramp(offGold, _config.DamageMixGoldFoot, _config.DamageMixGoldShoulder);
                if (profile.Damage != DamageProfile.Mixed)
                    blend = Math.Min(blend, 0.5);
                phys += (adGold / offGold - kitPhys) * blend;
            }
            physical += w * phys;
            magical += w * (1 - phys);

            if (champ?.HasTag("Marksman") == true)
                autoAttack += w;

            if (w > topW) { topW = w; topName = label; }
            if (w < minW) minW = w;
            if (phys >= 0.5 && w * phys > topPhysW) { topPhysW = w * phys; topPhys = label; }
            if (phys <= 0.5 && w * (1 - phys) > topMagW) { topMagW = w * (1 - phys); topMag = label; }

            var sustainDeg = SustainDegree(enemy, champ);
            if (sustainDeg > 0)
            {
                var sw = w * sustainDeg;
                sustain += sw;
                if (sw > topSustainW) { topSustainW = sw; topSustain = label; }
            }

            // Perfil de daño observado (kit + compras) para el tipo de resistencia anti-burst.
            var enemyDamage = phys >= 0.6 ? DamageProfile.Physical
                : phys <= 0.4 ? DamageProfile.Magical : DamageProfile.Mixed;
            // Burst: asesinos a peso pleno; magos (burst mágico de ARAM) a peso reducido,
            // salvo los de DPS sostenido. Un Veigar fed ya no es invisible al anti-burst.
            var burstFactor = champ?.HasTag("Assassin") == true ? 1.0
                : champ?.HasTag("Mage") == true && !_config.SustainedDpsMages.Contains(champ.Key)
                    ? _config.MageBurstFactor
                    : 0.0;
            var burstW = w * burstFactor;
            if (burstW > topBurstW)
            {
                topBurstW = burstW;
                topBurst = label;
                burstDamage = enemyDamage;
            }

            if (champ is not null)
            {
                if (suppression is null && _config.SuppressionChampions.Contains(champ.Key))
                    suppression = label;
                if (_config.HeavyCcChampions.Contains(champ.Key))
                    heavyCc++;
                if (_config.ShieldChampions.Contains(champ.Key))
                    shields = true;
                if (_config.PercentHpTrueDamageChampions.Contains(champ.Key))
                    pctHpTrueW += w;
                if (_config.HardEngageChampions.Contains(champ.Key))
                    hardEngageW += w;
            }
        }

        // Con varios enemigos todos parejos (arranque de partida) no hay "mayor
        // amenaza" que nombrar: señalar a alguien con 0/0/0 es ruido inventado.
        if (enemies.Count > 1 && topW - minW < 0.001)
            topName = null;

        var avgW = totalW / enemies.Count;
        var physicalShare = physical / totalW;
        var magicalShare = magical / totalW;
        var autoAttackShare = autoAttack / totalW;
        var sustainScore = sustain / totalW;
        var burstScore = avgW > 0 ? topBurstW / avgW : 0;

        return new TeamThreat
        {
            HasEnemies = true,
            PhysicalShare = physicalShare,
            MagicalShare = magicalShare,
            AutoAttackShare = autoAttackShare,
            TopThreatName = topName,
            TopPhysicalName = topPhys,
            TopMagicalName = topMag,
            SustainScore = sustainScore,
            TopSustainName = topSustain,
            EnemyBonusArmor = bonusArmor,
            EnemyBonusMr = bonusMr,
            BurstScore = burstScore,
            BurstDamage = burstDamage,
            TopBurstName = topBurst,
            HasSuppression = suppression is not null,
            SuppressionName = suppression,
            HeavyCcCount = heavyCc,
            HasShields = shields,

            // --- Defuzzificación de perceptos ---
            PhysicalSkew = Fuzzy.Ramp(physicalShare, SkewFoot, SkewShoulder),
            MagicalSkew = Fuzzy.Ramp(magicalShare, SkewFoot, SkewShoulder),
            // Mixto = ambos tipos presentes con peso real (50/50 → 1): un solo muro de
            // resistencia rinde menos y la vida cruda (Heartsteel/Warmog) rinde más.
            MixedDamage = Fuzzy.And(
                Fuzzy.Ramp(physicalShare, 0.30, 0.50),
                Fuzzy.Ramp(magicalShare, 0.30, 0.50)),
            ArmorStack = Fuzzy.Ramp(bonusArmor,
                _config.ArmorStackThreshold * 0.4, _config.ArmorStackThreshold * 2),
            MrStack = Fuzzy.Ramp(bonusMr,
                _config.MrStackThreshold * 0.4, _config.MrStackThreshold * 2),
            // foot = umbral del mapa (μ=0 en el umbral, sube por encima): no dispara por
            // debajo (Grieta 0.23 < 0.25 → 0) y entra suave en ARAM (umbral más bajo).
            Sustain = Fuzzy.Ramp(sustainScore, sustainThr, sustainThr * 1.7),
            Burst = Fuzzy.Ramp(burstScore,
                _config.BurstThreshold * 0.75, _config.BurstThreshold * 1.5),
            CritThreat = Fuzzy.Or(
                Fuzzy.Ramp(critSum, 0.15, 0.9),           // items de crítico comprados
                Fuzzy.Ramp(autoAttackShare, 0.35, 0.75)), // tiradores que van a apilar crítico
            PercentHpTrue = Ratio(pctHpTrueW, totalW, 0.15, 0.5),
            HardEngage = Ratio(hardEngageW, totalW, 0.15, 0.5),
            EnemyAntiHeal = Ratio(gwHolderW, totalW, 0.05, 0.35),
            EnemyTankiness = Fuzzy.Ramp(
                (bonusHealth + 20 * (bonusArmor + bonusMr)) / enemies.Count, 800, 3500),
        };
    }

    /// <summary>Rampa sobre la fracción de la amenaza ponderada que cumple una condición.</summary>
    private static double Ratio(double part, double total, double foot, double shoulder) =>
        total > 0 ? Fuzzy.Ramp(part / total, foot, shoulder) : 0;

    /// <summary>Peso de "qué tan fuerte va" un enemigo; nunca baja de 0.5 para que todos cuenten.</summary>
    public static double Weight(Player p) =>
        Math.Max(0.5,
            1 + p.Scores.Kills * 2.5 + p.Scores.Assists * 0.8
              + p.Scores.CreepScore * 0.04 + p.Level * 0.5 - p.Scores.Deaths * 1.2);

    /// <summary>
    /// Grado de sustain [0,1] de un enemigo: 1.0 si su kit cura (lista curada); si no, una
    /// rampa sobre el ORO invertido en items con tag de sustain — excluyendo starters (tag
    /// Lane) y botas, para que un Doran's/Guardian's o unas Gluttonous no cuenten como robo
    /// de vida real y disparen anti-heal contra cero curación al minuto 0 (común en ARAM).
    /// </summary>
    private double SustainDegree(Player enemy, StaticChampion? champ)
    {
        if (champ is not null && _config.HealerChampions.Contains(champ.Key))
            return 1.0;
        double gold = 0;
        foreach (var i in enemy.Items)
        {
            var item = _data.ItemById(i.ItemID);
            if (item is null || item.IsBoots || item.HasTag("Lane"))
                continue;
            if (_config.SustainTags.Any(item.HasTag))
                gold += item.GoldTotal * Math.Max(i.Count, 1);
        }
        return Fuzzy.Ramp(gold, _config.SustainGoldFoot, _config.SustainGoldShoulder);
    }

    private static string Label(Player p) => $"{p.ChampionName} ({p.Scores.Kda})";
}
