using System.Globalization;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Core.Items;

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
    // Supervivencia unificada (burst de asesino OR enganche duro): reemplaza AntiBurst+HardEngage.
    private const double SurvivalMag = 2.0;
    private const double CleanseMag = 3.0;
    private const double ShieldBreakMag = 1.0;
    private const double AntiCritMag = 2.5;
    private const double AntiTankMag = 2.0;
    private const double CcCounterMag = 2.0;
    /// <summary>Tope de la suma defensiva por item: evita que un solo muro aspire todo el presupuesto del top-3.</summary>
    private const double DefenseCap = 4.0;
    // Nudge ahead/behind: atrás sube la defensa, adelante sube los counters ofensivos.
    private const double BehindDefenseBoost = 0.35;
    private const double AheadOffenseBoost = 0.15;
    private const double LifestealDevalMag = 0.6;
    private const double LethalityDevalMag = 0.6;
    // Prior estadístico (OP.GG): magnitud del bono por "esto es lo que compran los
    // jugadores de tu campeón", escalada ±25 % por win rate. Calibrado para
    // reforzar el fit sin aplastar counters (bono máximo = 2.5 × 1.25 = 3.125).
    private const double StatCoreMag = 2.5;
    // Core: estar en el set núcleo ES la señal — el pick del SET (probabilidad de
    // un combo de 3 items, ~0.05–0.35 en datos reales) solo gradúa qué tan fijo
    // es el meta, por eso arranca de un piso alto en vez de cero.
    private const double StatCoreFloor = 0.6;
    private const double StatPickFoot = 0.05;
    private const double StatPickShoulder = 0.30;
    // Candidatos tardíos (4.º/5.º item): pick de item SUELTO (~0.1–0.5, escala
    // distinta al set) con rampa propia y factor < 1 — son opciones, no "la build".
    private const double StatLatePickFoot = 0.08;
    private const double StatLatePickShoulder = 0.40;
    private const double StatLateFactor = 0.85;
    // Confianza por muestra: los slots tardíos bajan a ~1.5k partidas con win
    // rates ruidosos; por debajo del pie el prior es ruido y no aporta.
    private const double StatPlayFoot = 300;
    private const double StatPlayShoulder = 2500;
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
        ChampionBuildStats? stats = null, IReadOnlyList<int>? previousTopIds = null)
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
        var affordMag = isAram ? _config.AramAffordabilityBonus : _config.AffordabilityBonus;

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
        // Los 6 slots de items (el 7.º es el trinket) ocupados: no hay dónde poner
        // una compra nueva salvo que consuma componentes que ya están en el inventario.
        var inventoryFull = me.Items.Count(i => i.Slot is >= 0 and <= 5) >= 6;
        var slotItemIds = new HashSet<int>(me.Items
            .Where(i => i.Slot is >= 0 and <= 5)
            .Select(i => i.ItemID));

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

        // Grupos "límite de 1" que ddragon no expone (el trío de pen mágica del Vacío no
        // comparte ni pasiva ni componente): vienen explícitos de la config. La membresía
        // se resuelve por id Y por nombre (variantes con id duplicado del catálogo).
        var exclusiveGroups = _config.ExclusiveItemGroups
            .Select(g => (Ids: new HashSet<int>(g),
                Names: new HashSet<string>(
                    g.Select(id => _data.ItemById(id)?.Name).OfType<string>(),
                    StringComparer.OrdinalIgnoreCase)))
            .ToList();
        int ExclusiveGroupOf(StaticItem item)
        {
            for (var g = 0; g < exclusiveGroups.Count; g++)
                if (exclusiveGroups[g].Ids.Contains(item.Id)
                    || exclusiveGroups[g].Names.Contains(item.Name))
                    return g;
            return -1;
        }
        var ownedExclusiveGroups = new HashSet<int>();
        foreach (var id in ownedIds)
            if (_data.ItemById(id) is { } groupOwned && ExclusiveGroupOf(groupOwned) is >= 0 and var og)
                ownedExclusiveGroups.Add(og);

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

        // Items ya comprados con pasiva con nombre: un candidato que comparta la pasiva
        // (Cleave, Lifeline, Annul, Immolate, Awe…) es ilegal o redundante — el juego los
        // limita a 1 y el efecto no se acumula. Se exceptúan los componentes del propio
        // candidato (Tiamat → Hidra: mejorar siempre es legal).
        var ownedWithPassives = ownedIds
            .Select(_data.ItemById)
            .Where(i => i is { PassiveNames.Count: > 0 })
            .Cast<StaticItem>()
            .ToList();

        // Crítico actual: el dato vivo del juego si está (incluye pasivas tipo Yasuo
        // y runas); si falta (replays/datos viejos), la suma del crítico comprado.
        var liveCrit = state.ActivePlayer?.ChampionStats.CritChance ?? 0;
        if (liveCrit > 1)
            liveCrit /= 100;
        var currentCrit = Math.Max(liveCrit,
            ownedIds.Sum(id => _data.ItemById(id)?.CritChance ?? 0));

        // Necesidad defensiva real (stats vivos): amortigua los muros ya cubiertos.
        var defenseNeed = DefenseNeedFrom(state.ActivePlayer?.ChampionStats, me.Level);

        // Items completos ya comprados: da el contexto de slot al prior de op.gg (comprando
        // el 4.º/5.º/6.º item se pondera por la lista de ESE slot, no siempre por la del 4.º).
        var completedCount = ownedIds
            .Select(_data.ItemById)
            .Count(i => i is { BuildsIntoSomething: false, IsBoots: false }
                        && i.GoldTotal >= _config.MinCompletedItemGold);

        // Ventaja/desventaja: atrás ⇒ durabilidad (comprar tiempo); adelante ⇒ greed
        // (snowball de daño). Nudge asimétrico: cada canal solo puede CRECER, nunca encoger.
        var ahead = threat.AvgEnemyWeight > 0
            ? Fuzzy.Ramp(threat.MyWeight / threat.AvgEnemyWeight, 1.15, 1.9) : 0;
        var behind = threat.MyWeight > 0
            ? Fuzzy.Ramp(threat.AvgEnemyWeight / threat.MyWeight, 1.15, 1.9) : 0;

        // Items de dupla/soporte (Zeke's, Locket, Knight's Vow…): sus efectos viven
        // pegados a un aliado — fuera del pool salvo que seas el soporte del equipo.
        var isSupport = profile.Archetype == BuildArchetype.Enchanter
            || string.Equals(me.Position, "UTILITY", StringComparison.OrdinalIgnoreCase)
            || ownedIds.Any(id => _data.ItemById(id)?.HasTag("GoldPer") == true);
        var supportOnlyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var supportId in _config.SupportOnlyItemIds)
            if (_data.ItemById(supportId) is { } supportItem)
                supportOnlyNames.Add(supportItem.Name);

        // Mejai's exige ir bien: recomendarle stacks a alguien que viene muriendo
        // es regalar oro (los stacks se pierden al morir).
        var snowballing = me.Scores.Kills + me.Scores.Assists >= _config.SnowballMinTakedowns
            && me.Scores.Deaths <= _config.SnowballMaxDeaths;

        var scored = new List<(StaticItem Item, double Score, List<string> Reasons, RecommendationCategory Category)>();

        foreach (var item in _data.CompletedItemsFor(mapNumber))
        {
            if (owned.Contains(item.Id) || ownedNames.Contains(item.Name))
                continue;
            if (skipManaItems && (item.HasTag("Mana") || item.HasTag("ManaRegen")))
                continue;
            // Un item cuyo único stat ofensivo no existe ni en tu kit ni en tu build
            // nunca es coherente (Morello para Darius): fuera del pool por completo.
            if (OffensiveMismatch(item, profile))
                continue;
            if (BlockedByOwnedPassive(item, ownedWithPassives))
                continue;
            // Ya tenés un item de su grupo excluyente ("límite de 1"): comprarlo es ilegal.
            if (ExclusiveGroupOf(item) is >= 0 and var itemGroup
                && ownedExclusiveGroups.Contains(itemGroup))
                continue;
            if (!isSupport && (supportOnlyNames.Contains(item.Name)
                || _config.SupportOnlyItemIds.Contains(item.Id)))
                continue;
            if (!snowballing && _config.SnowballItemIds.Contains(item.Id))
                continue;

            var (score, reasons, category) = ScoreItem(item, profile, threat, weights, teamHasGw,
                defenseNeed, stats, champName, currentCrit, ahead, behind, completedCount);
            if (score > 0)
                scored.Add((item, score, reasons, category));
        }

        // Alcanzable ya = empujón: ante puntajes parejos gana lo comprable ahora
        // (más fuerte en ARAM, donde se compra al reaparecer y no hay recall).
        var ranked = scored
            .Select(c =>
            {
                var plan = BuildPathPlanner.Plan(_data, c.Item, ownedIds, gold);
                // Empujón aditivo continuo en oro: entra gradual entre 0.5·faltante y el
                // faltante, en vez del salto multiplicativo ×1.25 que flipeaba el orden al
                // cruzar cada umbral de compra (la mayor discontinuidad que quedaba en v3).
                var afford = affordMag * Fuzzy.Ramp(gold, 0.5 * plan.RemainingCost, plan.RemainingCost);
                // Histéresis: un pequeño bono al que ya estaba en el top el tick anterior,
                // para que dos items casi empatados no intercambien rank con cada twitch de
                // input (la "lista que tiembla"). Muy por debajo de cualquier counter.
                var incumbent = previousTopIds is not null && previousTopIds.Contains(c.Item.Id)
                    ? _config.HysteresisBonus : 0;
                var score = c.Score + afford + incumbent;
                return (c.Item, Score: score, c.Reasons, c.Category, Plan: plan);
            })
            .OrderByDescending(c => c.Score)
            .ToList();

        var recommendations = new List<ItemRecommendation>();
        var recommendedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var takenPassives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var takenExclusiveGroups = new HashSet<int>();
        var gwTaken = false;
        var topScore = 0.0;
        foreach (var (item, score, reasons, category, plan) in ranked)
        {
            if (recommendations.Count >= _config.MaxRecommendations)
                break;
            // Chequeos PUROS primero; el estado (nombre/grupo/pasivas/GW) se marca recién
            // cuando el item pasa TODAS las puertas — un item salteado (p.ej. un segundo
            // item de Heridas Graves) no debe envenenar el grupo excluyente de los que
            // vienen detrás y bloquear una recomendación válida (Lord Dominik's).
            // Nunca dos recomendaciones con el mismo nombre (ids duplicados del catálogo).
            if (recommendedNames.Contains(item.Name))
                continue;
            // Ni dos items del mismo grupo excluyente (misma pasiva con nombre): dos
            // Hidras, dos Lifeline, dos Immolate… no pueden convivir en una build.
            if (item.PassiveNames.Count > 0 && item.PassiveNames.Overlaps(takenPassives))
                continue;
            // Ni dos del mismo grupo "límite de 1" de la config: comprar el primero
            // vuelve ilegal al segundo (Void Staff / Cryptbloom / Bloodletter's Curse).
            var rankedGroup = ExclusiveGroupOf(item);
            if (rankedGroup >= 0 && takenExclusiveGroups.Contains(rankedGroup))
                continue;
            if (item.AppliesGrievousWounds)
            {
                // Un solo item de Heridas Graves: el efecto no se acumula (aunque las
                // pasivas tengan nombres distintos: Hackshorn, Thorns…).
                if (gwTaken)
                    continue;
                gwTaken = true;
            }
            recommendedNames.Add(item.Name);
            if (rankedGroup >= 0)
                takenExclusiveGroups.Add(rankedGroup);
            takenPassives.UnionWith(item.PassiveNames);

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
                BlockedByFullInventory = inventoryFull && !MergesOwnedComponent(item, slotItemIds),
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
            BootsFor(me, profile, threat, mapNumber, stats, ownedIds, gold),
            SellSuggestions(me, profile, threat, weights, sustainThreshold,
                recommendations.Count > 0 ? recommendations[0] : null, inventoryFull),
            StarterFor(me, profile, state.GameData.GameTime, isAram, weights, stats, gold),
            ShopAlertFor(me, isAram, recommendations))
        {
            InventoryFull = inventoryFull,
            LateTips = LateTips(profile, inventoryFull, mapNumber,
                state.GameData.GameTime, gold, ownedIds),
            UrgentPickup = UrgentAntiHealPickup(profile, threat, teamHasGw, owned, mapNumber, recommendations),
        };
    }

    /// <summary>
    /// Pieza urgente de Heridas Graves (~800) cuando el enemigo cura mucho y ningún item
    /// COMPLETO de GW entró al top: el efecto vive en la pieza y se compra ya, en la primera
    /// muerte. Se elige por perfil de daño (Orbe AP / Ejecutor AD / Bramble tanque).
    /// </summary>
    private UrgentPickup? UrgentAntiHealPickup(ChampionProfile me, TeamThreat threat, bool teamHasGw,
        IReadOnlySet<int> owned, int mapNumber, IReadOnlyList<ItemRecommendation> recommendations)
    {
        if (threat.Sustain < 0.4 || teamHasGw)
            return null;
        // Ya tengo una fuente de GW, o el top ya recomienda un item de GW (su plan de
        // compra ya guía hacia la pieza): no dupliques el consejo.
        if (owned.Any(id => _data.ItemById(id)?.AppliesGrievousWounds == true)
            || recommendations.Any(r => r.Item.AppliesGrievousWounds))
            return null;

        var components = _data.GrievousWoundsComponentsFor(mapNumber);
        var pick = (me.DealsMagical ? components.FirstOrDefault(HasAp) : null)
                ?? (me.DealsPhysical ? components.FirstOrDefault(HasAd) : null)
                ?? components.FirstOrDefault(c => c.HasTag("Armor"))
                ?? components.FirstOrDefault();
        return pick is null
            ? null
            : new UrgentPickup(pick,
                $"buy the {GoldFmt(pick.GoldTotal)}g piece now, finish it later — {threat.TopSustainName} heals a lot");
    }

    /// <summary>
    /// Consejos de late game que ninguna app da: con la build completa, el oro
    /// sobrante va a elixires; y en la Grieta, jugar sin Control Ward regala visión.
    /// </summary>
    private List<string> LateTips(ChampionProfile profile, bool inventoryFull,
        int mapNumber, double gameTime, double gold, IReadOnlyList<int> ownedIds)
    {
        var tips = new List<string>();

        if (inventoryFull)
        {
            var elixirId = profile.Archetype == BuildArchetype.Tank
                ? _config.ElixirOfIronId
                : profile.Archetype is BuildArchetype.Marksman or BuildArchetype.AdFighter
                    or BuildArchetype.AdAssassin
                    ? _config.ElixirOfWrathId
                    : _config.ElixirOfSorceryId;
            if (_data.ItemById(elixirId) is { } elixir
                && gold >= elixir.GoldTotal && !ownedIds.Contains(elixirId))
                tips.Add($"Build complete — drink {elixir.Name} ({GoldFmt(elixir.GoldTotal)} g) before big fights.");
        }

        if (mapNumber == 11 && gameTime >= _config.ControlWardAdviceSeconds
            && !ownedIds.Contains(_config.ControlWardId)
            && _data.ItemById(_config.ControlWardId) is { } ward && gold >= ward.GoldTotal * 2)
            tips.Add($"No Control Ward — keep one ({GoldFmt(ward.GoldTotal)} g) for objectives.");

        return tips;
    }

    /// <summary>
    /// Comprar el item consumiría algún componente que HOY ocupa un slot del
    /// inventario (la compra libera espacio aunque esté lleno).
    /// </summary>
    private bool MergesOwnedComponent(StaticItem item, IReadOnlySet<int> slotItemIds)
    {
        foreach (var id in item.From)
        {
            if (slotItemIds.Contains(id))
                return true;
            if (_data.ItemById(id) is { } component && MergesOwnedComponent(component, slotItemIds))
                return true;
        }
        return false;
    }

    // --- Compra inicial (ARAM) ---

    /// <summary>
    /// Al arranque de ARAM (ventana configurable, inventario sin items reales) sugiere
    /// el starter (tag <c>Lane</c>) con mejor fit para el arquetipo; en empate gana el
    /// más caro (los Guardian's de 950 son la apertura estándar del mapa).
    /// </summary>
    private StarterAdvice? StarterFor(Player me, ChampionProfile profile, double gameTime,
        bool isAram, IReadOnlyDictionary<string, double> weights, ChampionBuildStats? stats, double gold)
    {
        if (!isAram || gameTime > _config.StarterWindowSeconds)
            return null;
        var ownsRealItem = me.Items.Any(s =>
            _data.ItemById(s.ItemID) is { } owned && !owned.Consumable && !owned.HasTag("Trinket"));
        if (ownsRealItem)
            return null;

        // op.gg trae un SET de apertura: resolverlo directo (comprable en ARAM) y recomendar
        // el conjunto mientras el presupuesto alcance — no solo un item con tag Lane, así
        // aperturas tipo Tear/Dark Seal/componentes también se pueden expresar.
        var resolved = (stats?.Starter?.ItemIds ?? Array.Empty<int>())
            .Select(_data.ItemById)
            .OfType<StaticItem>()
            .Where(i => i.Purchasable && i.OnAram)
            .ToList();
        if (resolved.Count > 0)
        {
            var set = new List<StaticItem>();
            double spent = 0;
            foreach (var it in resolved)
            {
                if (gold > 0 && spent + it.GoldTotal > gold && set.Count > 0)
                    break;
                set.Add(it);
                spent += it.GoldTotal;
            }
            return new StarterAdvice(set[0],
                $"opening buy for your {ArchetypeLabel(profile.Archetype)} build", set);
        }

        // Fallback sin datos de op.gg: el starter con tag Lane de mejor fit (más caro en empate).
        var best = _data.AramStarterItems
            .OrderByDescending(i => i.Tags.Sum(t => weights.GetValueOrDefault(t)))
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
            return $"Shop open while dead — finish {top.Item.Name} now ({GoldFmt(top.Purchase.RemainingCost)}).";
        if (top.Purchase.NextComponent is { } component)
            return $"Shop open while dead — buy {component.Name} ({GoldFmt(top.Purchase.NextComponentCost)}) toward {top.Item.Name}.";
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
        ItemRecommendation? top, bool inventoryFull)
    {
        var allWeights = ArchetypeWeights.All
            .ToDictionary(a => a, a => ArchetypeWeights.For(a, _config));

        var candidates = new List<SellSuggestion>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var slot in me.Items)
        {
            var item = _data.ItemById(slot.ItemID);
            if (item is null || item.SellGold <= 0)
                continue;

            // Reglas explícitas, ANTES de los filtros de "item final":
            // Item de stacks (Mejai's/Dark Seal) cuando venís muriendo: los stacks se
            // pierden en cada muerte — el oro invertido no vuelve.
            if (_config.SnowballItemIds.Contains(item.Id)
                && me.Scores.Deaths >= _config.SnowballSellDeaths
                && me.Scores.Kills < me.Scores.Deaths)
            {
                if (seenNames.Add(item.Name))
                    candidates.Add(new SellSuggestion(item, item.SellGold,
                        "dying too often — its stacks keep resetting"));
                continue;
            }
            // Starter con el inventario lleno: su slot vale más que sus stats.
            if (inventoryFull && item.HasTag("Lane"))
            {
                if (seenNames.Add(item.Name))
                    candidates.Add(new SellSuggestion(item, item.SellGold,
                        "starter outlived its value — selling frees the slot"));
                continue;
            }

            // Solo items finales con valor real: componentes, botas, consumibles,
            // trinkets e items de quest no son decisiones de venta interesantes.
            if (item.IsBoots || item.Consumable || item.BuildsIntoSomething
                || item.HasTag("Trinket") || item.HasTag("GoldPer")
                || item.GoldTotal < _config.MinCompletedItemGold)
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
        IReadOnlyDictionary<string, double> weights, bool teamHasGw, DefenseNeed need,
        ChampionBuildStats? stats, string champName, double currentCrit = 0,
        double ahead = 0, double behind = 0, int completedCount = 0)
    {
        var reasons = new List<string>();
        var fit = item.Tags.Sum(t => weights.GetValueOrDefault(t));
        // Crítico saturado: solo puntúa la fracción que NO desborda el cap de 100%
        // (a 75% un item de 25% entra entero; a 100% su crítico vale cero).
        var critWaste = CritWaste(item, currentCrit);
        if (critWaste > 0)
            fit = Math.Max(0, fit - weights.GetValueOrDefault("CriticalStrike") * critWaste);
        // Raíz cuadrada: comprime el fit para que apilar tags no aplaste a los bonos
        // situacionales (un counter necesario debe poder ganarle a más stats crudos).
        var core = 3 * Math.Sqrt(fit) * (0.8 + 0.4 * Math.Min(Efficiency(item, critWaste), 1.2));
        var df = DefenseFactor(me);
        double offense = 0, defense = 0;

        // Anti-curación (grado de sustain, ya calibrado por mapa), con bono si además
        // aporta fit. Los items anti-heal de un perfil de daño ajeno (Morello para un
        // AD puro) ya quedaron fuera del pool por OffensiveMismatch.
        // Con GW aliado ya en juego el bono anti-heal se amortigua (no se anula): contra
        // una comp de sustain extremo todavía conviene una SEGUNDA fuente de Heridas Graves
        // (la cobertura depende de quién pega al objetivo, no de un único item aliado).
        var allyGwDamp = teamHasGw
            ? Fuzzy.Ramp(threat.Sustain, _config.AllyGwDampFoot, _config.AllyGwDampShoulder)
            : 1.0;
        if (item.AppliesGrievousWounds && allyGwDamp > 0 && threat.Sustain > MuGate)
        {
            // La magnitud sube con la intensidad del sustain (2.0 → 3.0) para que un item
            // GW completo pueda alcanzar el top contra comps de doble curación.
            var antiHealMag = AntiHealMag + Fuzzy.Ramp(threat.Sustain, 0.5, 0.9);
            offense += (antiHealMag + (fit > 0 ? 0.5 : 0)) * threat.Sustain * allyGwDamp;
            reasons.Add($"cuts the healing of {threat.TopSustainName}");
        }

        // Penetración cuando el enemigo apila la resistencia que bloquea tu daño.
        // La letalidad NO responde a la armadura apilada (pen plana): solo el %pen.
        if (me.DealsPhysical && item.HasTag("ArmorPenetration") && !item.HasLethality
            && threat.ArmorStack > MuGate)
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
            defense += DefenseMag * threat.PhysicalSkew * df * wallDamp * need.Armor;
            reasons.Add($"{Pct(threat.PhysicalShare)} of enemy damage is physical ({threat.TopPhysicalName})");
        }
        if (item.HasTag("SpellBlock") && threat.MagicalSkew > MuGate)
        {
            defense += DefenseMag * threat.MagicalSkew * df * wallDamp * need.Mr;
            reasons.Add($"{Pct(threat.MagicalShare)} of enemy damage is magic ({threat.TopMagicalName})");
        }

        // Muro de HP puro (Heartsteel/Warmog): la vida defiende contra AMBOS tipos de
        // daño — a pleno contra comps mixtas (donde un solo muro rinde menos), parcial
        // contra sesgadas — pero pierde contra daño por % de vida, que escala con tu HP.
        // Solo items sin resistencias ni ofensa: el resto ya cobra por Armor/SpellBlock.
        if (item.HasTag("Health") && !item.HasTag("Armor") && !item.HasTag("SpellBlock")
            && !HasAd(item) && !HasAp(item))
        {
            var hpDegree = Math.Max(threat.MixedDamage,
                0.4 * Math.Max(threat.PhysicalSkew, threat.MagicalSkew));
            if (hpDegree > MuGate)
            {
                defense += DefenseMag * hpDegree * df * (1 - 0.6 * threat.PercentHpTrue) * need.Hp;
                reasons.Add("raw health holds against both damage types");
            }
        }

        // Supervivencia al "te van a matar" (Zhonya/GA/QSS/Banshee): burst de asesino y
        // enganche/pick duro son señales CORRELACIONADAS — se combinan por OR difuso, no se
        // suman, así un solo item defensivo no cobra dos veces por la misma amenaza. Para
        // squishies con ofensa acompañante; el factor de necesidad aplica al resist del burst.
        var survival = Fuzzy.Or(threat.Burst, threat.HardEngage);
        if (survival > MuGate && me.IsSquishy && OffensiveMatch(item, me)
            && (item.HasTag("Armor") || item.HasTag("SpellBlock") || item.RemovesCc))
        {
            var burstMagical = threat.BurstDamage == DamageProfile.Magical;
            var needFactor = item.HasTag(burstMagical ? "SpellBlock" : "Armor")
                ? (burstMagical ? need.Mr : need.Armor) : 1.0;
            defense += SurvivalMag * survival * needFactor;
            reasons.Add(threat.Burst >= threat.HardEngage
                ? $"to survive the burst from {threat.TopBurstName}"
                : "survives the enemy engage");
        }

        // Anti-crítico cuando el rival apila crítico (tiradores CON crit comprado / Filo Infinito).
        if (item.ReducesCritDamage && threat.CritThreat > MuGate)
        {
            defense += AntiCritMag * threat.CritThreat;
            reasons.Add("reduces the enemy crit damage");
        }

        // Anti-CC unificado (difuso): la supresión (lockdown letal, limpieza para carries
        // AD) y el CC pesado general (wombo, limpieza/tenacidad para todos) se combinan por
        // MÁXIMO, no por suma — un supresor que además es CC pesado (Malzahar) no paga dos
        // veces el mismo QSS. Ambas magnitudes escalan por su grado difuso (ya no crisp).
        double antiCc = 0;
        string? antiCcReason = null;
        if (item.RemovesCc && threat.Suppression > MuGate
            && me.Archetype is BuildArchetype.Marksman or BuildArchetype.AdFighter or BuildArchetype.AdAssassin)
        {
            antiCc = CleanseMag * threat.Suppression;
            antiCcReason = $"cleanses the suppression from {threat.SuppressionName}";
        }
        if (threat.CcThreat > MuGate && (item.RemovesCc || item.GrantsTenacity))
        {
            var cc = CcCounterMag * threat.CcThreat;
            if (cc > antiCc)
            {
                antiCc = cc;
                antiCcReason = "cuts through the enemy crowd control";
            }
        }
        if (antiCc > 0)
        {
            offense += antiCc;
            reasons.Add(antiCcReason!);
        }

        // Anti-tanque: contra un equipo gordo tu daño quiere penetrar/on-hit, no rebotar.
        if (threat.EnemyTankiness > MuGate && AntiTankItem(item, me))
        {
            offense += AntiTankMag * threat.EnemyTankiness;
            reasons.Add("cuts through the enemy's durability");
        }

        // Rompe-escudos, escalado por el grado de escudos enemigos (fed-weighted), no crisp.
        if (threat.ShieldThreat > MuGate && item.BreaksShields && me.DealsPhysical)
        {
            offense += ShieldBreakMag * threat.ShieldThreat;
            reasons.Add("shreds enemy shields");
        }

        // Prior estadístico: lo que los jugadores de este campeón realmente compran
        // (OP.GG, por rol y parche). Entra como refuerzo del fit — NO cuenta como
        // situacional para la categoría — así el item sigue siendo Core/Spike.
        double statBonus = 0;
        if (stats is not null && PriorFor(item, stats, completedCount) is { } prior)
        {
            // Core y tardíos se ponderan aparte: sus pick rates están en escalas
            // distintas (combo de 3 items vs. item suelto) — ver constantes.
            var pickMu = prior.IsCore
                ? StatCoreFloor + (1 - StatCoreFloor)
                    * Fuzzy.Ramp(prior.PickRate, StatPickFoot, StatPickShoulder)
                : StatLateFactor
                    * Fuzzy.Ramp(prior.PickRate, StatLatePickFoot, StatLatePickShoulder);
            var mu = pickMu
                   * Fuzzy.Ramp(prior.Play, StatPlayFoot, StatPlayShoulder)
                   * (0.75 + 0.5 * Fuzzy.Ramp(prior.WinRate, StatWinFoot, StatWinShoulder));
            if (mu > MuGate)
            {
                statBonus = StatCoreMag * mu;
                reasons.Add($"bought in {Pct(prior.PickRate)} of {champName} builds"
                    + (prior.WinRate > 0 ? $" ({Pct(prior.WinRate)} WR)" : ""));
            }
        }

        // Devaluaciones: robo de vida si el enemigo YA compró anti-curación, y
        // letalidad contra un equipo gordo (la pen plana se diluye contra armadura alta).
        var penalty = LifestealDevalMag * threat.EnemyAntiHeal * SustainTagWeight(item, weights)
            + (item.HasLethality
                ? LethalityDevalMag * threat.EnemyTankiness * weights.GetValueOrDefault("ArmorPenetration")
                : 0);

        // Tope defensivo por item: contra una comp que dispara varios muros correlacionados
        // (armadura + anti-crit + supervivencia), un solo item no debe aspirar todo el
        // presupuesto del top-3 y desplazar la penetración/anti-heal que la comp también pide.
        defense = Math.Min(defense, DefenseCap);

        // Categoría desde componentes LIMPIOS (fit + situacional) y ANTES del nudge
        // ahead/behind: sin el prior estadístico ni las penalidades (un statBonus grande ya
        // no disfraza de Core a un counter) y estable frente a ir adelante/atrás — la
        // etiqueta explica el item, no el marcador. Si lo situacional es una fracción
        // relevante, manda la contrapartida dominante; si no, es fit puro (Core).
        var situational = offense + defense;
        var coherent = core + situational;
        var category =
            coherent <= 0 || situational < 0.2 * coherent ? RecommendationCategory.Core
            : defense >= offense ? RecommendationCategory.Defense
            : RecommendationCategory.Counter;

        // Nudge ahead/behind (asimétrico: cada canal solo crece): afecta el PUNTAJE, no la categoría.
        offense *= 1 + AheadOffenseBoost * ahead;
        defense *= 1 + BehindDefenseBoost * behind;
        var score = core + offense + defense + statBonus - penalty;
        return (score, reasons, category);
    }

    private static bool OffensiveMatch(StaticItem item, ChampionProfile me) =>
        (me.DealsMagical && HasAp(item)) || (me.DealsPhysical && HasAd(item));

    /// <summary>
    /// El único stat ofensivo del item no existe ni en el kit del campeón ni en el
    /// arquetipo de su build (Morello para un luchador AD puro). Los híbridos AD+AP y
    /// los items sin ofensa (tanque/soporte) nunca se filtran; el arquetipo forzado o
    /// detectado cuenta como evidencia de daño (AP Tristana forzada a Mage compra AP).
    /// </summary>
    private static bool OffensiveMismatch(StaticItem item, ChampionProfile me)
    {
        var buildDealsMagic = me.DealsMagical || me.Archetype
            is BuildArchetype.Mage or BuildArchetype.ApFighter or BuildArchetype.Enchanter;
        var buildDealsPhysical = me.DealsPhysical || me.Archetype
            is BuildArchetype.Marksman or BuildArchetype.AdFighter or BuildArchetype.AdAssassin;
        return (HasAp(item) && !HasAd(item) && !buildDealsMagic)
            || (HasAd(item) && !HasAp(item) && !buildDealsPhysical);
    }

    /// <summary>
    /// Algún item ya comprado comparte una pasiva con nombre con el candidato y no es
    /// componente suyo: comprar el candidato sería ilegal (grupos "límite de 1") o
    /// redundante (la pasiva no se acumula).
    /// </summary>
    private bool BlockedByOwnedPassive(StaticItem item, IReadOnlyList<StaticItem> ownedWithPassives)
    {
        if (item.PassiveNames.Count == 0 || ownedWithPassives.Count == 0)
            return false;
        HashSet<int>? buildTree = null;
        foreach (var ownedItem in ownedWithPassives)
        {
            if (!item.PassiveNames.Overlaps(ownedItem.PassiveNames))
                continue;
            if (buildTree is null)
            {
                buildTree = new HashSet<int>();
                AddBuildTree(item.Id, buildTree);
            }
            if (!buildTree.Contains(ownedItem.Id))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Item que "atraviesa" tanques: %pen u on-hit para físico, pen mágica para AP.
    /// La letalidad queda fuera: es pen plana, se diluye contra armadura alta.
    /// </summary>
    private static bool AntiTankItem(StaticItem item, ChampionProfile me) =>
        (me.DealsPhysical && ((item.HasTag("ArmorPenetration") && !item.HasLethality)
            || item.HasTag("OnHit")))
        || (me.DealsMagical && item.HasTag("MagicPenetration"));

    /// <summary>Fracción del crítico del item que desbordaría el cap de 100% (1 = sobra todo).</summary>
    private static double CritWaste(StaticItem item, double currentCrit)
    {
        if (item.CritChance <= 0)
            return currentCrit >= 1 && item.HasTag("CriticalStrike") ? 1 : 0;
        var headroom = Math.Max(0, 1 - currentCrit);
        return 1 - Math.Min(item.CritChance, headroom) / item.CritChance;
    }

    /// <summary>Peso de los tags de sustain del item (para devaluarlos si el enemigo tiene anti-heal).</summary>
    private double SustainTagWeight(StaticItem item, IReadOnlyDictionary<string, double> weights) =>
        item.Tags.Where(_config.SustainTags.Contains).Sum(t => weights.GetValueOrDefault(t));

    /// <summary>
    /// Prior del item en las builds del campeón. OP.GG registra evoluciones
    /// (Muramana) mientras el catálogo recomienda la forma comprable (Manamune);
    /// el mapa de la config traduce la evolución al item recomendable.
    /// </summary>
    private (double PickRate, double WinRate, int Play, bool IsCore)? PriorFor(
        StaticItem item, ChampionBuildStats stats, int completedCount)
    {
        if (stats.ItemPriorFor(item.Id, completedCount) is { } direct)
            return direct;
        foreach (var (evolved, buyable) in _config.ItemEvolutions)
            if (buyable == item.Id && stats.ItemPriorFor(evolved, completedCount) is { } viaEvolution)
                return viaEvolution;
        return null;
    }

    private static double DefenseFactor(ChampionProfile me) =>
        me.Archetype == BuildArchetype.Tank ? 1.2 : me.IsSquishy ? 1.0 : 0.6;

    /// <summary>
    /// Necesidad defensiva por tipo [0,1] derivada de los stats VIVOS del jugador: con
    /// armadura/RM/vida ya altas (dos defensivos comprados) un tercer muro rinde menos.
    /// Sin datos vivos (replays, MaxHealth=0) devuelve 1 en todo: cae al factor por arquetipo.
    /// </summary>
    private readonly record struct DefenseNeed(double Armor, double Mr, double Hp)
    {
        public static DefenseNeed Full { get; } = new(1, 1, 1);
    }

    private static DefenseNeed DefenseNeedFrom(ChampionStats? live, int level)
    {
        if (live is null || live.MaxHealth <= 0)
            return DefenseNeed.Full;
        var lvl = Math.Max(level, 1);
        return new DefenseNeed(
            1 - Fuzzy.Ramp(live.Armor, 40 + 5 * lvl, 120 + 8 * lvl),
            1 - Fuzzy.Ramp(live.MagicResist, 30 + 4 * lvl, 100 + 6 * lvl),
            1 - Fuzzy.Ramp(live.MaxHealth, 800 + 90 * lvl, 1800 + 150 * lvl));
    }

    /// <summary>
    /// Valor en oro de los stats planos del item vs. su costo (eficiencia clásica de oro;
    /// las pasivas no cuentan, por eso solo modula suavemente el fit). El crítico que
    /// desbordaría el cap de 100% no vale nada.
    /// </summary>
    private static double Efficiency(StaticItem item, double critWaste = 0)
    {
        // Valores de oro estándar por stat (incluye maná/MS/lifesteal/regen: items de mago,
        // enchanter y asesino gastan su oro ahí, y sin esto leían como "ineficientes").
        var value = item.AttackDamage * 35 + item.AbilityPower * 21.75
                  + item.Armor * 20 + item.SpellBlock * 18 + item.Health * 2.67
                  + item.AttackSpeedPct * 2500 + item.CritChance * 4000 * (1 - critWaste)
                  + item.Mana * 1.4 + item.MoveSpeed * 12 + item.LifeStealPct * 3750
                  + item.HealthRegen * 3;
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
        int mapNumber, ChampionBuildStats? stats, IReadOnlyList<int> ownedIds, double gold)
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

        // El consejo es accionable: trae el plan de compra (si no tenés ni las Botas
        // básicas y el oro no llega al tier 2, el siguiente paso son las básicas).
        BootsAdvice Advice(StaticItem boots, string reason)
        {
            var plan = BuildPathPlanner.Plan(_data, boots, ownedIds, gold);
            return new BootsAdvice(boots, reason, plan,
                (int)Math.Max(0, plan.RemainingCost - gold));
        }

        // La amenaza ya no decide sola: queda como candidata, y si la meta
        // (op.gg) recomienda otra bota, acompaña como nota en la razón.
        (StaticItem Boots, string Reason)? threatPick = null;
        if (threat.HeavyCcCount >= _config.CcCountForMercs
            || threat.MagicalShare >= _config.SkewedDamageShare)
        {
            var mercs = ByTag(candidates, "SpellBlock");
            if (mercs is not null)
                threatPick = (mercs, threat.HeavyCcCount >= _config.CcCountForMercs
                    ? $"the enemy has {threat.HeavyCcCount} heavy-CC champions"
                    : $"{Pct(threat.MagicalShare)} of enemy damage is magic");
        }

        if (threatPick is null
            && threat.PhysicalShare >= _config.SkewedDamageShare && threat.AutoAttackShare >= 0.35)
        {
            var steelcaps = ByTag(candidates, "Armor");
            if (steelcaps is not null)
                threatPick = (steelcaps,
                    $"heavy physical auto-attack damage ({threat.TopPhysicalName})");
        }

        // La meta manda: las botas que más compran los jugadores de tu campeón,
        // en el orden de op.gg (popularidad), no en el orden del catálogo.
        // La meta manda para CUALQUIER muestra real; solo se cierra el hueco de muestra
        // trivial (día 1 de parche, un puñado de partidas) con la misma rampa de confianza
        // por juego que ya tienen los priors de items — no es un árbitro amenaza-vs-meta.
        if (stats?.Boots is { } statBoots
            && Fuzzy.Ramp(statBoots.Play, StatPlayFoot, StatPlayShoulder) > MuGate)
        {
            var popular = statBoots.ItemIds
                .Select(id => candidates.FirstOrDefault(c => c.Id == id))
                .FirstOrDefault(c => c is not null);
            if (popular is not null)
            {
                var wr = statBoots.WinRate > 0 ? $", {Pct(statBoots.WinRate)} WR" : "";
                var reason =
                    $"most common boots on your champion ({Pct(statBoots.PickRate)} pick rate{wr})";
                if (threatPick is { } alt && alt.Boots.Id != popular.Id)
                    reason += $" — but {alt.Reason}: consider {alt.Boots.Name}";
                return Advice(popular, reason);
            }
        }

        // Sin datos de op.gg: la amenaza decide como hasta ahora.
        if (threatPick is { } picked)
            return Advice(picked.Boots, picked.Reason);

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
        return Advice(pick, $"standard for your {ArchetypeLabel(profile.Archetype)} build");
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
