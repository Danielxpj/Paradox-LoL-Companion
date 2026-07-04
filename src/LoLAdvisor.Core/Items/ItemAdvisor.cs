using System.Globalization;
using LoLAdvisor.Core.Config;
using LoLAdvisor.Core.DataDragon;
using LoLAdvisor.Core.Models;
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Core.Items;

/// <summary>
/// El motor del asesor de items. Combina el perfil del jugador (qué compra su
/// arquetipo), la radiografía del equipo enemigo (quién va fed y con qué daño) y el
/// catálogo estático para producir un <see cref="ItemAdvicePlan"/>: top de items con
/// razones legibles, plan de compra por componentes y botas sugeridas.
/// </summary>
public sealed class ItemAdvisor
{
    private readonly IStaticData _data;
    private readonly ItemsConfig _config;
    private readonly ChampionProfiler _profiler;
    private readonly ThreatAnalyzer _threats;

    // Magnitudes de cada regla situacional: el bono real = magnitud × grado difuso [0,1].
    // El grado (de ThreatAnalyzer) modula continuamente; aquí solo vive "cuánto pesa" cada
    // contrapartida cuando la amenaza es máxima. Calibradas para que el orden de selección
    // de la v2 se preserve y las nuevas contrapartidas manden cuando la amenaza es clara.
    private const double AntiHealMag = 2.0;
    private const double PenMag = 3.0;
    private const double DefenseMag = 2.0;
    private const double AntiBurstMag = 2.0;
    private const double CleanseMag = 3.0;
    private const double ShieldBreakMag = 1.0;
    private const double AntiCritMag = 2.5;
    private const double AntiTankMag = 2.0;
    private const double HardEngageMag = 1.5;
    private const double LifestealDevalMag = 0.6;
    // Prior estadístico (OP.GG): magnitud del bono por "esto es lo que compran los
    // jugadores de tu campeón". Rampa sobre el pick rate del SET de build (los sets
    // completos rondan 0.05–0.35, no confundir con pick rate por item), escalada
    // ±25 % por win rate. Calibrado para reforzar el fit sin aplastar counters.
    private const double StatCoreMag = 2.5;
    private const double StatPickFoot = 0.05;
    private const double StatPickShoulder = 0.30;
    private const double StatWinFoot = 0.48;
    private const double StatWinShoulder = 0.54;
    /// <summary>μ mínimo para que una regla aporte bono y emita razón (evita ruido sub-umbral).</summary>
    private const double MuGate = 0.05;

    public ItemAdvisor(IStaticData data, ItemsConfig? config = null)
    {
        _data = data;
        _config = config ?? new ItemsConfig();
        _profiler = new ChampionProfiler(data, _config);
        _threats = new ThreatAnalyzer(data, _config, _profiler);
    }

    /// <summary>
    /// <c>null</c> si el catálogo no cargó o no hay datos suficientes de la partida.
    /// <paramref name="forcedArchetype"/>: arquetipo elegido a mano por el jugador en la
    /// UI; anula la detección por campeón e inventario (el perfil de daño no cambia:
    /// es del kit del campeón, no de la build).
    /// </summary>
    public ItemAdvicePlan? Advise(GameState state, BuildArchetype? forcedArchetype = null,
        ChampionBuildStats? stats = null)
    {
        if (!_data.IsLoaded)
            return null;
        var me = state.ActivePlayerEntry;
        if (me is null)
            return null;

        var threat = _threats.Analyze(state);
        if (!threat.HasEnemies)
            return null;

        var isAram = state.GameData.MapNumber == 12
            || string.Equals(state.GameData.GameMode, "ARAM", StringComparison.OrdinalIgnoreCase);
        var mapNumber = isAram ? 12 : state.GameData.MapNumber == 0 ? 11 : state.GameData.MapNumber;
        var sustainThreshold = isAram
            ? _config.SustainThreshold * _config.AramSustainThresholdFactor
            : _config.SustainThreshold;
        var affordBoost = isAram ? _config.AramAffordabilityBoost : 1.15;

        // El arquetipo sale del inventario real: si vas Garen tanque (o vendés todo y
        // cambiás de build), las recomendaciones siguen a lo que estás comprando.
        // Salvo que el jugador lo haya forzado en la UI: la elección manual manda.
        var profile = forcedArchetype is { } forced
            ? _profiler.Profile(me) with { Archetype = forced }
            : _profiler.ProfileWithInventory(me);
        var gold = state.ActivePlayer?.CurrentGold ?? 0;
        var ownedIds = me.Items
            .SelectMany(i => Enumerable.Repeat(i.ItemID, Math.Max(i.Count, 1)))
            .ToList();

        // "Ya lo tengo" incluye el árbol de construcción de lo que llevo: si tengo una
        // mejora/transformación (Muramana, item Obra Maestra de Ornn…), su item base y
        // componentes cuentan como comprados y no se vuelven a recomendar.
        var owned = new HashSet<int>(ownedIds);
        foreach (var id in ownedIds)
            AddBuildTree(id, owned);

        // El catálogo trae items duplicados con distinto id (variantes por modo/época):
        // también se excluye por NOMBRE lo que ya llevas puesto.
        var ownedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ownedIds)
            if (_data.ItemById(id) is { } ownedItem)
                ownedNames.Add(ownedItem.Name);

        var teamHasGw = state.AllPlayers
            .Where(p => p.Team == me.Team)
            .SelectMany(p => p.Items)
            .Any(i => _data.ItemById(i.ItemID)?.AppliesGrievousWounds == true);

        var weights = WeightsFor(profile.Archetype);
        // Campeón sin maná (partype): los items de maná no le aportan nada — su
        // AD/AP está tasado asumiendo la pasiva de maná. Se excluyen por completo.
        var myChampion = _data.ResolveChampion(me.ChampionName, me.RawChampionName);
        var skipManaItems = myChampion is { UsesMana: false };
        var champName = string.IsNullOrEmpty(me.ChampionName) ? "your champion" : me.ChampionName;
        var scored = new List<(StaticItem Item, double Score, List<string> Reasons, RecommendationCategory Category)>();

        foreach (var item in _data.CompletedItemsFor(mapNumber))
        {
            if (owned.Contains(item.Id) || ownedNames.Contains(item.Name))
                continue;
            if (skipManaItems && (item.HasTag("Mana") || item.HasTag("ManaRegen")))
                continue;

            var (score, reasons, category) = ScoreItem(item, profile, threat, weights, teamHasGw, stats, champName);
            if (score > 0)
                scored.Add((item, score, reasons, category));
        }

        // Alcanzable ya = empujón: ante puntajes parejos gana lo comprable ahora
        // (más fuerte en ARAM, donde se compra al reaparecer y no hay recall).
        var ranked = scored
            .Select(c =>
            {
                var plan = BuildPathPlanner.Plan(_data, c.Item, ownedIds, gold);
                var score = plan.CanFinishNow ? c.Score * affordBoost : c.Score;
                return (c.Item, Score: score, c.Reasons, c.Category, Plan: plan);
            })
            .OrderByDescending(c => c.Score)
            .ToList();

        var recommendations = new List<ItemRecommendation>();
        var recommendedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gwTaken = false;
        var topScore = 0.0;
        foreach (var (item, score, reasons, category, plan) in ranked)
        {
            if (recommendations.Count >= _config.MaxRecommendations)
                break;
            // Nunca dos recomendaciones con el mismo nombre (ids duplicados del catálogo).
            if (!recommendedNames.Add(item.Name))
                continue;
            if (item.AppliesGrievousWounds)
            {
                // Un solo item de Heridas Graves: el efecto no se acumula.
                if (gwTaken)
                    continue;
                gwTaken = true;
            }

            if (reasons.Count == 0)
                reasons.Add(FitReason(item, profile.Archetype, weights));
            var missing = (int)Math.Max(0, plan.RemainingCost - gold);
            // La primera recomendación fija el 100 %: la prioridad del resto es su fracción.
            if (recommendations.Count == 0)
                topScore = score;
            // Un item que ya alcanzás y que domina por fit (no por un counter) es un power spike.
            var finalCategory = plan.CanFinishNow && category == RecommendationCategory.Core
                ? RecommendationCategory.Spike
                : category;
            recommendations.Add(new ItemRecommendation(item, score, reasons, plan, missing)
            {
                Priority = topScore > 0 ? Fuzzy.Clamp01(score / topScore) : 1.0,
                Category = finalCategory,
            });
        }

        var summary = ThreatSummary(threat, isAram);
        if (forcedArchetype is not null)
            summary += $" | Build override: {ArchetypeLabel(profile.Archetype)}";
        else if (profile.InferredFromItems)
            summary += $" | Build detected from your items: {ArchetypeLabel(profile.Archetype)}";

        return new ItemAdvicePlan(
            summary,
            profile,
            threat,
            recommendations,
            BootsFor(me, profile, threat, mapNumber, stats),
            SellSuggestions(me, profile, threat, weights, sustainThreshold,
                recommendations.Count > 0 ? recommendations[0] : null),
            StarterFor(me, profile, state.GameData.GameTime, isAram, weights, stats),
            ShopAlertFor(me, isAram, recommendations));
    }

    // --- Compra inicial (ARAM) ---

    /// <summary>
    /// Al arranque de ARAM (ventana configurable, inventario sin items reales) sugiere
    /// el starter (tag <c>Lane</c>) con mejor fit para el arquetipo; en empate gana el
    /// más caro (los Guardian's de 950 son la apertura estándar del mapa).
    /// </summary>
    private StarterAdvice? StarterFor(Player me, ChampionProfile profile, double gameTime,
        bool isAram, IReadOnlyDictionary<string, double> weights, ChampionBuildStats? stats)
    {
        if (!isAram || gameTime > _config.StarterWindowSeconds)
            return null;
        var ownsRealItem = me.Items.Any(s =>
            _data.ItemById(s.ItemID) is { } owned && !owned.Consumable && !owned.HasTag("Trinket"));
        if (ownsRealItem)
            return null;

        var statStarterIds = stats?.Starter?.ItemIds ?? Array.Empty<int>();
        var best = _data.AramStarterItems
            .OrderByDescending(i => statStarterIds.Contains(i.Id) ? 1 : 0)
            .ThenByDescending(i => i.Tags.Sum(t => weights.GetValueOrDefault(t)))
            .ThenByDescending(i => i.GoldTotal)
            .FirstOrDefault();
        return best is null
            ? null
            : new StarterAdvice(best, $"opening buy for your {ArchetypeLabel(profile.Archetype)} build");
    }

    // --- Tienda abierta (en ARAM solo se compra estando muerto) ---

    /// <summary>El único momento en que el consejo es accionable en ARAM merece su aviso.</summary>
    private static string? ShopAlertFor(Player me, bool isAram, IReadOnlyList<ItemRecommendation> recommendations)
    {
        if (!isAram || !me.IsDead || recommendations.Count == 0)
            return null;
        var top = recommendations[0];
        if (top.Purchase.CanFinishNow)
            return $"Shop open while dead — finish {top.Item.Name} now ({GoldFmt(top.Item.GoldTotal)}).";
        if (top.Purchase.NextComponent is { } component)
            return $"Shop open while dead — buy {component.Name} ({GoldFmt(component.GoldTotal)}) toward {top.Item.Name}.";
        return null;
    }

    private static string GoldFmt(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

    // --- Venta de items incoherentes ---

    /// <summary>
    /// Items del inventario que ya no pegan con la build actual (fit relativo bajo
    /// contra el arquetipo que mejor los explica) y cuya venta no sacrifica un rol
    /// situacional activo (anti-heal, cleanse, rompe-escudos, defensa necesaria).
    /// Máximo 2, ordenados por oro de venta.
    /// </summary>
    private List<SellSuggestion> SellSuggestions(
        Player me, ChampionProfile profile, TeamThreat threat,
        IReadOnlyDictionary<string, double> myWeights, double sustainThreshold,
        ItemRecommendation? top)
    {
        var allWeights = ArchetypeWeights.All
            .ToDictionary(a => a, a => ArchetypeWeights.For(a, _config));

        var candidates = new List<SellSuggestion>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var slot in me.Items)
        {
            var item = _data.ItemById(slot.ItemID);
            // Solo items finales con valor real: componentes, botas, consumibles,
            // trinkets e items de quest no son decisiones de venta interesantes.
            if (item is null || item.IsBoots || item.Consumable || item.BuildsIntoSomething
                || item.HasTag("Trinket") || item.HasTag("GoldPer")
                || item.SellGold <= 0 || item.GoldTotal < _config.MinCompletedItemGold)
                continue;
            if (!seenNames.Add(item.Name))
                continue;

            var myFit = item.Tags.Sum(t => myWeights.GetValueOrDefault(t));
            var bestArchetype = profile.Archetype;
            var bestFit = myFit;
            foreach (var (archetype, archetypeWeights) in allWeights)
            {
                var fit = item.Tags.Sum(t => archetypeWeights.GetValueOrDefault(t));
                if (fit > bestFit)
                {
                    bestArchetype = archetype;
                    bestFit = fit;
                }
            }
            if (bestFit <= 0 || myFit / bestFit >= _config.SellFitRatioThreshold)
                continue;

            // Salvaguardas: aunque no pegue con la build, cumple un rol situacional.
            if (item.AppliesGrievousWounds && threat.SustainScore >= sustainThreshold)
                continue;
            if (item.RemovesCc && threat.HasSuppression)
                continue;
            if (item.BreaksShields && threat.HasShields)
                continue;
            if (item.HasTag("Armor") && threat.PhysicalShare >= _config.SkewedDamageShare)
                continue;
            if (item.HasTag("SpellBlock") && threat.MagicalShare >= _config.SkewedDamageShare)
                continue;

            candidates.Add(new SellSuggestion(item, item.SellGold,
                $"{ArchetypeLabel(bestArchetype)} item — doesn't fit your {ArchetypeLabel(profile.Archetype)} build"));
        }

        var sells = candidates.OrderByDescending(s => s.SellGold).Take(2).ToList();

        // Si el oro de venta cubre lo que falta para el item top, decirlo.
        if (sells.Count > 0 && top is { MissingGold: > 0 }
            && sells.Sum(s => s.SellGold) >= top.MissingGold)
            sells[0] = sells[0] with { Reason = sells[0].Reason + $" · selling funds {top.Item.Name} now" };

        return sells;
    }

    /// <summary>Agrega recursivamente los componentes (y bases) de un item poseído.</summary>
    private void AddBuildTree(int itemId, HashSet<int> owned)
    {
        var item = _data.ItemById(itemId);
        if (item is null)
            return;
        foreach (var componentId in item.From)
            if (owned.Add(componentId))
                AddBuildTree(componentId, owned);
    }

    // --- Puntaje de un item ---

    /// <summary>
    /// Puntúa un item combinando la base de arquetipo con contrapartidas situacionales
    /// <b>difusas</b>: cada regla escala su magnitud por un grado ∈ [0,1] de
    /// <see cref="TeamThreat"/> (el umbral de la v2 es ahora el cruce μ≈0.5), de modo que
    /// las recomendaciones entran y salen de forma gradual y estable, no a saltos.
    /// Devuelve además la categoría que explica por qué el item está donde está.
    /// </summary>
    private (double Score, List<string> Reasons, RecommendationCategory Category) ScoreItem(
        StaticItem item, ChampionProfile me, TeamThreat threat,
        IReadOnlyDictionary<string, double> weights, bool teamHasGw,
        ChampionBuildStats? stats, string champName)
    {
        var reasons = new List<string>();
        var fit = item.Tags.Sum(t => weights.GetValueOrDefault(t));
        // Raíz cuadrada: comprime el fit para que apilar tags no aplaste a los bonos
        // situacionales (un counter necesario debe poder ganarle a más stats crudos).
        var core = 3 * Math.Sqrt(fit) * (0.8 + 0.4 * Math.Min(Efficiency(item), 1.2));
        var df = DefenseFactor(me);
        double offense = 0, defense = 0;

        // Anti-curación (grado de sustain, ya calibrado por mapa), con bono si además pega
        // con tu perfil de daño (Morello AP / Recordatorio AD / Cota tanque).
        if (item.AppliesGrievousWounds && !teamHasGw && threat.Sustain > MuGate)
        {
            offense += (AntiHealMag + (fit > 0 ? 0.5 : 0)) * threat.Sustain;
            reasons.Add($"cuts the healing of {threat.TopSustainName}");
        }

        // Penetración cuando el enemigo apila la resistencia que bloquea tu daño.
        if (me.DealsPhysical && item.HasTag("ArmorPenetration") && threat.ArmorStack > MuGate)
        {
            offense += PenMag * threat.ArmorStack;
            reasons.Add($"enemies already bought {threat.EnemyBonusArmor:0} armor");
        }
        if (me.DealsMagical && item.HasTag("MagicPenetration") && threat.MrStack > MuGate)
        {
            offense += PenMag * threat.MrStack;
            reasons.Add($"enemies already bought {threat.EnemyBonusMr:0} magic resist");
        }

        // Defensa contra el tipo de daño dominante — atenuada si el enemigo ignora
        // resistencias (% de vida / daño verdadero): apilar un solo muro rinde menos.
        var wallDamp = 1 - 0.4 * threat.PercentHpTrue;
        if (item.HasTag("Armor") && threat.PhysicalSkew > MuGate)
        {
            defense += DefenseMag * threat.PhysicalSkew * df * wallDamp;
            reasons.Add($"{Pct(threat.PhysicalShare)} of enemy damage is physical ({threat.TopPhysicalName})");
        }
        if (item.HasTag("SpellBlock") && threat.MagicalSkew > MuGate)
        {
            defense += DefenseMag * threat.MagicalSkew * df * wallDamp;
            reasons.Add($"{Pct(threat.MagicalShare)} of enemy damage is magic ({threat.TopMagicalName})");
        }

        // Híbridos defensa+ofensa contra un asesino fed (Zhonya / Ángel / Fauces / Banshee).
        if (threat.Burst > MuGate && me.IsSquishy)
        {
            var defTag = threat.BurstDamage == DamageProfile.Magical ? "SpellBlock" : "Armor";
            if (item.HasTag(defTag) && OffensiveMatch(item, me))
            {
                defense += AntiBurstMag * threat.Burst;
                reasons.Add($"to survive the burst from {threat.TopBurstName}");
            }
        }

        // Anti-crítico cuando el rival apila crítico (tiradores / Filo Infinito).
        if (item.ReducesCritDamage && threat.CritThreat > MuGate)
        {
            defense += AntiCritMag * threat.CritThreat;
            reasons.Add("reduces the enemy crit damage");
        }

        // Supervivencia vs. enganche/pick duro (GA / Zhonya / QSS / Banshee).
        if (threat.HardEngage > MuGate && me.IsSquishy
            && (item.HasTag("Armor") || item.HasTag("SpellBlock") || item.RemovesCc)
            && OffensiveMatch(item, me))
        {
            defense += HardEngageMag * threat.HardEngage;
            reasons.Add("survives the enemy engage");
        }

        // Limpieza de supresión (QSS/Mercurial) para carries: quedar suprimido es muerte segura.
        if (threat.HasSuppression && item.RemovesCc
            && me.Archetype is BuildArchetype.Marksman or BuildArchetype.AdFighter or BuildArchetype.AdAssassin)
        {
            offense += CleanseMag;
            reasons.Add($"cleanses the suppression from {threat.SuppressionName}");
        }

        // Anti-tanque: contra un equipo gordo tu daño quiere penetrar/on-hit, no rebotar.
        if (threat.EnemyTankiness > MuGate && AntiTankItem(item, me))
        {
            offense += AntiTankMag * threat.EnemyTankiness;
            reasons.Add("cuts through the enemy's durability");
        }

        // Rompe-escudos cuando el rival trae escudos grandes.
        if (threat.HasShields && item.BreaksShields && me.DealsPhysical)
        {
            offense += ShieldBreakMag;
            reasons.Add("shreds enemy shields");
        }

        // Prior estadístico: lo que los jugadores de este campeón realmente compran
        // (OP.GG, por rol y parche). Entra como refuerzo del fit — NO cuenta como
        // situacional para la categoría — así el item sigue siendo Core/Spike.
        double statBonus = 0;
        if (stats is not null && PriorFor(item, stats) is { } prior)
        {
            var mu = Fuzzy.Ramp(prior.PickRate, StatPickFoot, StatPickShoulder)
                   * (0.75 + 0.5 * Fuzzy.Ramp(prior.WinRate, StatWinFoot, StatWinShoulder));
            if (mu > MuGate)
            {
                statBonus = StatCoreMag * mu;
                reasons.Add($"bought in {Pct(prior.PickRate)} of {champName} builds"
                    + (prior.WinRate > 0 ? $" ({Pct(prior.WinRate)} WR)" : ""));
            }
        }

        // Devaluación del robo de vida si el enemigo YA compró anti-curación contra vos.
        var penalty = LifestealDevalMag * threat.EnemyAntiHeal * SustainTagWeight(item, weights);

        var score = core + offense + defense + statBonus - penalty;
        // Categoría = qué explica el puntaje: si lo situacional es una fracción relevante
        // del total, manda la contrapartida dominante (defensa vs. counter ofensivo);
        // si no, el item se recomienda por fit puro (Core). El prior estadístico
        // refuerza el fit, no lo situacional.
        var situational = offense + defense;
        var category =
            score <= 0 || situational < 0.2 * score ? RecommendationCategory.Core
            : defense >= offense ? RecommendationCategory.Defense
            : RecommendationCategory.Counter;
        return (score, reasons, category);
    }

    private static bool OffensiveMatch(StaticItem item, ChampionProfile me) =>
        (me.DealsMagical && HasAp(item)) || (me.DealsPhysical && HasAd(item));

    /// <summary>Item que "atraviesa" tanques: penetración para tu tipo de daño, u on-hit.</summary>
    private static bool AntiTankItem(StaticItem item, ChampionProfile me) =>
        (me.DealsPhysical && (item.HasTag("ArmorPenetration") || item.HasTag("OnHit")))
        || (me.DealsMagical && item.HasTag("MagicPenetration"));

    /// <summary>Peso de los tags de sustain del item (para devaluarlos si el enemigo tiene anti-heal).</summary>
    private double SustainTagWeight(StaticItem item, IReadOnlyDictionary<string, double> weights) =>
        item.Tags.Where(_config.SustainTags.Contains).Sum(t => weights.GetValueOrDefault(t));

    /// <summary>
    /// Prior del item en las builds del campeón. OP.GG registra evoluciones
    /// (Muramana) mientras el catálogo recomienda la forma comprable (Manamune);
    /// el mapa de la config traduce la evolución al item recomendable.
    /// </summary>
    private (double PickRate, double WinRate)? PriorFor(StaticItem item, ChampionBuildStats stats)
    {
        if (stats.ItemPriorFor(item.Id) is { } direct)
            return direct;
        foreach (var (evolved, buyable) in _config.ItemEvolutions)
            if (buyable == item.Id && stats.ItemPriorFor(evolved) is { } viaEvolution)
                return viaEvolution;
        return null;
    }

    private static double DefenseFactor(ChampionProfile me) =>
        me.Archetype == BuildArchetype.Tank ? 1.2 : me.IsSquishy ? 1.0 : 0.6;

    /// <summary>
    /// Valor en oro de los stats planos del item vs. su costo (eficiencia clásica de oro;
    /// las pasivas no cuentan, por eso solo modula suavemente el fit).
    /// </summary>
    private static double Efficiency(StaticItem item)
    {
        var value = item.AttackDamage * 35 + item.AbilityPower * 21.75
                  + item.Armor * 20 + item.SpellBlock * 18 + item.Health * 2.67
                  + item.AttackSpeedPct * 2500 + item.CritChance * 4000;
        return value / Math.Max(item.GoldTotal, 1);
    }

    private static bool HasAd(StaticItem item) => item.HasTag("Damage") || item.HasTag("AttackDamage");
    private static bool HasAp(StaticItem item) => item.HasTag("SpellDamage");

    /// <summary>Razón por defecto: los tags que más pesan para el arquetipo, con nombre legible.</summary>
    private static string FitReason(StaticItem item, BuildArchetype archetype,
        IReadOnlyDictionary<string, double> weights)
    {
        var top = item.Tags
            .Select(t => (Tag: t, Weight: weights.GetValueOrDefault(t)))
            .Where(x => x.Weight > 0)
            .OrderByDescending(x => x.Weight)
            .Take(2)
            .Select(x => TagLabel(x.Tag))
            .ToList();
        return top.Count == 0
            ? $"core for your {ArchetypeLabel(archetype)} build"
            : $"fits your {ArchetypeLabel(archetype)} build: {string.Join(" + ", top)}";
    }

    private static string TagLabel(string tag) => tag switch
    {
        "Damage" or "AttackDamage" => "attack damage",
        "SpellDamage" => "ability power",
        "AbilityHaste" => "ability haste",
        "CriticalStrike" => "crit",
        "AttackSpeed" => "attack speed",
        "LifeSteal" => "lifesteal",
        "ArmorPenetration" => "armor pen",
        "MagicPenetration" => "magic pen",
        "SpellBlock" => "magic resist",
        "HealthRegen" => "health regen",
        "ManaRegen" => "mana regen",
        "NonbootsMovement" => "move speed",
        "OnHit" => "on-hit",
        "Aura" => "team aura",
        _ => tag.ToLowerInvariant(),
    };

    // --- Botas ---

    private BootsAdvice? BootsFor(Player me, ChampionProfile profile, TeamThreat threat,
        int mapNumber, ChampionBuildStats? stats)
    {
        var candidates = _data.FinishedBootsFor(mapNumber);
        if (candidates.Count == 0)
            return null;

        // Si ya llevas botas terminadas, no hay nada que sugerir.
        var hasFinished = me.Items
            .Select(i => _data.ItemById(i.ItemID))
            .Any(i => i is { IsBoots: true } && i.GoldTotal >= _config.MinFinishedBootsGold);
        if (hasFinished)
            return null;

        if (threat.HeavyCcCount >= _config.CcCountForMercs
            || threat.MagicalShare >= _config.SkewedDamageShare)
        {
            var mercs = ByTag(candidates, "SpellBlock");
            if (mercs is not null)
                return new BootsAdvice(mercs, threat.HeavyCcCount >= _config.CcCountForMercs
                    ? $"the enemy has {threat.HeavyCcCount} heavy-CC champions"
                    : $"{Pct(threat.MagicalShare)} of enemy damage is magic");
        }

        if (threat.PhysicalShare >= _config.SkewedDamageShare && threat.AutoAttackShare >= 0.35)
        {
            var steelcaps = ByTag(candidates, "Armor");
            if (steelcaps is not null)
                return new BootsAdvice(steelcaps,
                    $"heavy physical auto-attack damage ({threat.TopPhysicalName})");
        }

        // Sin amenaza que decida: las botas que más compran los jugadores de tu campeón.
        if (stats?.Boots is { } statBoots)
        {
            var popular = candidates.FirstOrDefault(c => statBoots.ItemIds.Contains(c.Id));
            if (popular is not null)
                return new BootsAdvice(popular,
                    $"most common boots on your champion ({Pct(statBoots.PickRate)} pick rate)");
        }

        var byArchetype = profile.Archetype switch
        {
            BuildArchetype.Marksman => ByTag(candidates, "AttackSpeed"),
            BuildArchetype.Mage or BuildArchetype.ApFighter => ByTag(candidates, "MagicPenetration"),
            BuildArchetype.AdAssassin or BuildArchetype.Enchanter =>
                ByTag(candidates, "AbilityHaste"),
            BuildArchetype.Tank => threat.PhysicalShare >= threat.MagicalShare
                ? ByTag(candidates, "Armor")
                : ByTag(candidates, "SpellBlock"),
            _ => threat.PhysicalShare >= threat.MagicalShare
                ? ByTag(candidates, "Armor")
                : ByTag(candidates, "SpellBlock"),
        };
        var pick = byArchetype ?? candidates[0];
        return new BootsAdvice(pick, $"standard for your {ArchetypeLabel(profile.Archetype)} build");
    }

    private static StaticItem? ByTag(IReadOnlyList<StaticItem> items, string tag) =>
        items.FirstOrDefault(i => i.HasTag(tag));

    // --- Textos ---

    private static string ThreatSummary(TeamThreat threat, bool isAram) =>
        (isAram ? "ARAM — " : "") +
        $"Enemy damage: {Pct(threat.PhysicalShare)} physical · {Pct(threat.MagicalShare)} magic" +
        (threat.TopThreatName is null ? "" : $" — biggest threat: {threat.TopThreatName}");

    private static string Pct(double share) =>
        ((int)Math.Round(share * 100, MidpointRounding.AwayFromZero))
            .ToString(CultureInfo.InvariantCulture) + "%";

    internal static string ArchetypeLabel(BuildArchetype archetype) => archetype switch
    {
        BuildArchetype.Marksman => "marksman",
        BuildArchetype.Mage => "mage",
        BuildArchetype.AdAssassin => "assassin",
        BuildArchetype.AdFighter => "fighter",
        BuildArchetype.ApFighter => "AP fighter",
        BuildArchetype.Tank => "tank",
        BuildArchetype.Enchanter => "support",
        _ => "champion",
    };

    // --- Pesos por arquetipo ---

    private IReadOnlyDictionary<string, double> WeightsFor(BuildArchetype archetype) =>
        ArchetypeWeights.For(archetype, _config);
}
