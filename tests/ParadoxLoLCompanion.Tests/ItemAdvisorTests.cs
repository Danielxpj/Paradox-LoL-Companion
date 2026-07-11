using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Tests;

public class ItemAdvisorTests
{
    private static ItemAdvisor Advisor() => new(TestCatalog.Catalog());

    private static readonly int[] None = new int[0];

    [Fact]
    public void NotLoadedCatalog_ReturnsNull()
    {
        var advisor = new ItemAdvisor(DataDragonCatalog.Empty);
        var state = TestCatalog.State(5000, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 5, None));
        Assert.Null(advisor.Advise(state));
    }

    [Fact]
    public void NoEnemies_ReturnsNull()
    {
        var state = TestCatalog.State(5000, ("Ahri", "ORDER", 0, None));
        Assert.Null(Advisor().Advise(state));
    }

    [Fact]
    public void ApChampion_GetsApItems()
    {
        var state = TestCatalog.State(5000, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 2, None));
        var plan = Advisor().Advise(state)!;

        Assert.NotEmpty(plan.Recommendations);
        Assert.All(plan.Recommendations, r => Assert.True(
            r.Item.HasTag("SpellDamage") || r.Item.HasTag("Armor") || r.Item.HasTag("SpellBlock"),
            $"{r.Item.Name} does not fit a mage"));
        Assert.Equal(BuildArchetype.Mage, plan.MyProfile.Archetype);
    }

    [Fact]
    public void ForcedArchetype_OverridesDetectionAndInventory()
    {
        // Soraka (enchanter por defecto) con item de support ya comprado, forzada a maga:
        // el override manual le gana al campeón Y al inventario. Su perfil de daño no
        // cambia (sigue siendo mágico: es del kit, no de la build).
        var state = TestCatalog.State(5000,
            ("Soraka", "ORDER", 0, new[] { 2065 }),
            ("Jinx", "CHAOS", 2, None));
        var plan = Advisor().Advise(state, BuildArchetype.Mage)!;

        Assert.Equal(BuildArchetype.Mage, plan.MyProfile.Archetype);
        Assert.False(plan.MyProfile.InferredFromItems);
        Assert.Equal(DamageProfile.Magical, plan.MyProfile.Damage);
        Assert.Contains("Build override: mage", plan.ThreatSummary);
        Assert.Contains(plan.Recommendations, r => r.Item.HasTag("SpellDamage"));
    }

    [Fact]
    public void AramStart_RecommendsStarterByArchetype()
    {
        // Ahri maga al arranque de ARAM (t=0, sin items): Guardian's Orb.
        var state = TestCatalog.AramState(1400, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 0, None));
        var plan = Advisor().Advise(state)!;
        Assert.NotNull(plan.Starter);
        Assert.Equal(3112, plan.Starter!.Item.Id);
        Assert.Contains("mage", plan.Starter.Reason);

        // Jinx tiradora: Guardian's Hammer (empata fit con Doran's Blade → gana el más caro).
        var jinx = TestCatalog.AramState(1400, ("Jinx", "ORDER", 0, None), ("Ahri", "CHAOS", 0, None));
        Assert.Equal(3184, Advisor().Advise(jinx)!.Starter!.Item.Id);
    }

    [Fact]
    public void Starter_Suppressed_WhenOwningItems_OrLate_OrOnRift()
    {
        // Ya compraste algo real: sin starter.
        var bought = TestCatalog.AramState(500, ("Ahri", "ORDER", 0, new[] { 3112 }), ("Jinx", "CHAOS", 0, None));
        Assert.Null(Advisor().Advise(bought)!.Starter);

        // Fuera de la ventana inicial: sin starter.
        var late = TestCatalog.AramState(1400, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 0, None));
        late.GameData.GameTime = 300;
        Assert.Null(Advisor().Advise(late)!.Starter);

        // En la Grieta no aplica (el starter correcto depende de la línea).
        var rift = TestCatalog.State(500, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 0, None));
        Assert.Null(Advisor().Advise(rift)!.Starter);
    }

    [Fact]
    public void ShopAlert_OnlyWhileDeadInAram()
    {
        // Muerto en ARAM con oro de sobra: la tienda está abierta y alcanza el top.
        var state = TestCatalog.AramState(9000, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 2, None));
        state.AllPlayers[0].IsDead = true;
        var plan = Advisor().Advise(state)!;
        Assert.NotNull(plan.ShopAlert);
        Assert.Contains(plan.Top!.Item.Name, plan.ShopAlert);
        Assert.Contains("finish", plan.ShopAlert);

        // Vivo: sin aviso (la tienda no está disponible).
        var alive = TestCatalog.AramState(9000, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 2, None));
        Assert.Null(Advisor().Advise(alive)!.ShopAlert);

        // En la Grieta la tienda no depende de estar muerto.
        var rift = TestCatalog.State(9000, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 2, None));
        rift.AllPlayers[0].IsDead = true;
        Assert.Null(Advisor().Advise(rift)!.ShopAlert);
    }

    [Fact]
    public void FitReasons_NameTheContributingStats()
    {
        // Sin bonos situacionales, la razón dice qué stats pesan (no un texto genérico).
        var state = TestCatalog.AramState(1400, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 0, None));
        var plan = Advisor().Advise(state)!;
        Assert.Contains(plan.Recommendations,
            r => r.Reasons.Any(reason => reason.StartsWith("fits your mage build:")));
    }

    [Fact]
    public void ForcedArchetype_Null_KeepsAutoDetection()
    {
        // Sin override, nada cambia: detección automática y sin banner de override.
        var state = TestCatalog.State(5000, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 2, None));
        var plan = Advisor().Advise(state)!;

        Assert.Equal(BuildArchetype.Mage, plan.MyProfile.Archetype);
        Assert.DoesNotContain("Build override", plan.ThreatSummary);
    }

    [Fact]
    public void AntiHeal_MatchesMyDamageType()
    {
        // Yo AP vs Warwick (curador) fed: el GW recomendado debe ser el de AP (Morello),
        // no el físico (Recordatorio Mortal) ni el de tanque (Cota de Espinas).
        var state = TestCatalog.State(5000, ("Ahri", "ORDER", 0, None), ("Warwick", "CHAOS", 8, None));
        var plan = Advisor().Advise(state)!;

        var gw = plan.Recommendations.Where(r => r.Item.AppliesGrievousWounds).ToList();
        var reco = Assert.Single(gw); // nunca más de un item de Heridas Graves
        Assert.Equal(3165, reco.Item.Id);
        Assert.Contains(reco.Reasons, r => r.Contains("healing"));
    }

    [Fact]
    public void AntiHeal_Damped_WhenTeamHasItAndSustainModerate()
    {
        // Mi aliado ya lleva Morello y la curación enemiga es moderada (un healer entre
        // tres, ninguno fed): con GW aliado el bono anti-heal se amortigua — no se insiste.
        var state = TestCatalog.State(5000,
            ("Ahri", "ORDER", 0, None),
            ("Soraka", "ORDER", 0, new[] { 3165 }),
            ("Warwick", "CHAOS", 0, None),
            ("Zed", "CHAOS", 0, None),
            ("Malzahar", "CHAOS", 0, None));
        var plan = Advisor().Advise(state)!;

        Assert.DoesNotContain(plan.Recommendations,
            r => r.Reasons.Any(reason => reason.Contains("healing")));
    }

    [Fact]
    public void AntiHeal_StillRecommended_WhenTeamHasItButSustainExtreme()
    {
        // Aliado con Morello PERO comp de sustain extremo (Warwick+Aatrox, ambos healers):
        // la cobertura de GW depende de quién pega al objetivo, así que una SEGUNDA fuente
        // de Heridas Graves sigue valiendo — el gate aliado amortigua, no anula.
        var state = TestCatalog.State(5000,
            ("Ahri", "ORDER", 0, None),
            ("Soraka", "ORDER", 0, new[] { 3165 }),
            ("Warwick", "CHAOS", 0, None),
            ("Aatrox", "CHAOS", 0, None));
        var plan = Advisor().Advise(state)!;

        Assert.Contains(plan.Recommendations,
            r => r.Reasons.Any(reason => reason.Contains("healing")));
    }

    [Fact]
    public void StackedArmor_TriggersArmorPenetration()
    {
        // Zed (asesino físico) vs enemigos con 160 de armadura comprada. Portadores sin CC
        // pesado ni curación (Vayne/Pyke) para aislar la penetración del bono anti-CC.
        var state = TestCatalog.State(5000,
            ("Zed", "ORDER", 0, None),
            ("Vayne", "CHAOS", 0, new[] { 3075, 1029 }),
            ("Pyke", "CHAOS", 0, new[] { 3068 }));
        var plan = Advisor().Advise(state)!;

        var top = plan.Top!;
        Assert.True(top.Item.HasTag("ArmorPenetration"), $"top was {top.Item.Name}");
        Assert.Contains(top.Reasons, r => r.Contains("armor"));
    }

    [Fact]
    public void StackedMr_TriggersMagicPenetration_ForApChampion()
    {
        var state = TestCatalog.State(5000,
            ("Ahri", "ORDER", 0, None),
            ("Leona", "CHAOS", 0, new[] { 3065, 3111 }),   // 50 + 25 RM
            ("Amumu", "CHAOS", 0, new[] { 3065 }));        // 50 RM
        var plan = Advisor().Advise(state)!;

        // Un item de pen mágica (del grupo del Vacío sobrevive uno solo al dedup:
        // el que mejor puntúe, no necesariamente Void Staff).
        Assert.Contains(plan.Recommendations, r => r.Item.HasTag("MagicPenetration"));
        var pen = plan.Recommendations.First(r => r.Item.HasTag("MagicPenetration"));
        Assert.Contains(pen.Reasons, r => r.Contains("magic resist"));
    }

    [Fact]
    public void OwnedExclusiveGroupItem_BlocksTheWholeGroup()
    {
        // El juego limita a 1 los items de pen mágica del Vacío (Void Staff /
        // Cryptbloom / Bloodletter's Curse): teniendo uno, comprar otro es ilegal.
        // ddragon no expone el grupo (ni pasiva compartida ni componente común).
        var state = TestCatalog.State(5000,
            ("Ahri", "ORDER", 0, new[] { 3135 }),          // ya tengo Void Staff
            ("Leona", "CHAOS", 0, new[] { 3065, 3111 }),
            ("Amumu", "CHAOS", 0, new[] { 3065 }));
        var plan = Advisor().Advise(state)!;

        Assert.DoesNotContain(plan.Recommendations, r => r.Item.Id is 3137 or 8010);
    }

    [Fact]
    public void ExclusiveGroup_NeverRecommendsTwoTogether()
    {
        // Sin tener ninguno, la lista tampoco puede ofrecer dos del mismo grupo
        // excluyente a la vez: comprar uno vuelve ilegales a los otros.
        var state = TestCatalog.State(5000,
            ("Ahri", "ORDER", 0, None),
            ("Leona", "CHAOS", 0, new[] { 3065, 3111 }),
            ("Amumu", "CHAOS", 0, new[] { 3065 }));
        var plan = Advisor().Advise(state)!;

        Assert.True(plan.Recommendations.Count(r => r.Item.Id is 3135 or 3137 or 8010) <= 1,
            "two Void-pen items recommended together");
    }

    [Fact]
    public void FedAdAssassin_MakesSquishyApBuyZhonya()
    {
        var state = TestCatalog.State(5000,
            ("Ahri", "ORDER", 0, None),
            ("Zed", "CHAOS", 12, None),
            ("Soraka", "CHAOS", 0, None),
            ("Amumu", "CHAOS", 0, None));
        var plan = Advisor().Advise(state)!;

        Assert.Contains(plan.Recommendations, r => r.Item.Id == 3157); // Zhonya
        var zhonya = plan.Recommendations.First(r => r.Item.Id == 3157);
        Assert.Contains(zhonya.Reasons, r => r.Contains("burst"));
    }

    [Fact]
    public void Suppression_RecommendsCleanse_ForAdCarry()
    {
        var state = TestCatalog.State(9000,
            ("Jinx", "ORDER", 0, None),
            ("Malzahar", "CHAOS", 4, None),
            ("Warwick", "CHAOS", 3, None));
        var plan = Advisor().Advise(state)!;

        Assert.Contains(plan.Recommendations, r => r.Item.Id == 3139); // Mercurial
        var merc = plan.Recommendations.First(r => r.Item.Id == 3139);
        Assert.Contains(merc.Reasons, r => r.Contains("suppression"));
    }

    [Fact]
    public void OwnedItems_AreNeverRecommended()
    {
        var state = TestCatalog.State(9000,
            ("Jinx", "ORDER", 0, new[] { 3031, 3006 }),
            ("Leona", "CHAOS", 0, None));
        var plan = Advisor().Advise(state)!;

        Assert.DoesNotContain(plan.Recommendations, r => r.Item.Id == 3031);
    }

    [Fact]
    public void Recommendation_CarriesPurchasePlan()
    {
        // Jinx con 1.400 de oro: el top no alcanza, pero el plan trae el componente a comprar.
        var state = TestCatalog.State(1400,
            ("Jinx", "ORDER", 0, None),
            ("Soraka", "CHAOS", 0, None));
        var plan = Advisor().Advise(state)!;

        var top = plan.Top!;
        Assert.False(top.Affordable);
        Assert.NotNull(top.Purchase.NextComponent);
        Assert.True(top.MissingGold > 0);
    }

    [Fact]
    public void Boots_MercsAgainstCcAndMagic()
    {
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, None),
            ("Malzahar", "CHAOS", 0, None),
            ("Leona", "CHAOS", 0, None),
            ("Amumu", "CHAOS", 0, None));
        var plan = Advisor().Advise(state)!;

        Assert.NotNull(plan.Boots);
        Assert.Equal(3111, plan.Boots!.Boots.Id);
        Assert.Contains("CC", plan.Boots.Reason);
    }

    [Fact]
    public void Boots_SteelcapsAgainstPhysicalAutoAttackers()
    {
        var state = TestCatalog.State(2000,
            ("Ahri", "ORDER", 0, None),
            ("Jinx", "CHAOS", 3, None),
            ("Vayne", "CHAOS", 3, None),
            ("Zed", "CHAOS", 1, None));
        var plan = Advisor().Advise(state)!;

        Assert.NotNull(plan.Boots);
        Assert.Equal(3047, plan.Boots!.Boots.Id);
    }

    [Fact]
    public void Boots_ArchetypeDefault_WhenNoSkew()
    {
        // Un físico y un mágico parejos: sin sesgo ni CC → botas del arquetipo (Berserker).
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, None),
            ("Zed", "CHAOS", 2, None),
            ("Ahri", "CHAOS", 2, None));
        var plan = Advisor().Advise(state)!;

        Assert.NotNull(plan.Boots);
        Assert.Equal(3006, plan.Boots!.Boots.Id);
    }

    [Fact]
    public void Boots_NoAdvice_WhenAlreadyFinished()
    {
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, new[] { 3006 }),
            ("Zed", "CHAOS", 2, None),
            ("Ahri", "CHAOS", 2, None));
        var plan = Advisor().Advise(state)!;

        Assert.Null(plan.Boots);
    }

    [Fact]
    public void ThreatSummary_MentionsSplitAndTopThreat()
    {
        var state = TestCatalog.State(1000,
            ("Ahri", "ORDER", 0, None),
            ("Jinx", "CHAOS", 10, None));
        var plan = Advisor().Advise(state)!;

        Assert.Contains("physical", plan.ThreatSummary);
        Assert.Contains("Jinx", plan.ThreatSummary);
    }

    [Fact]
    public void Aram_UsesAramItemPool_ExcludingRiftOnlyItems()
    {
        // Kraken Slayer (6672) es solo de Grieta en este catálogo: en la Grieta puede
        // recomendarse, en ARAM jamás. (Enemigo físico para no sesgar hacia defensa mágica.)
        var onRift = Advisor().Advise(TestCatalog.State(9000,
            ("Jinx", "ORDER", 0, None), ("Zed", "CHAOS", 0, None)))!;
        var onAram = Advisor().Advise(TestCatalog.AramState(9000,
            ("Jinx", "ORDER", 0, None), ("Zed", "CHAOS", 0, None)))!;

        Assert.Contains(onRift.Recommendations, r => r.Item.Id == 6672);
        Assert.DoesNotContain(onAram.Recommendations, r => r.Item.Id == 6672);
        Assert.StartsWith("ARAM", onAram.ThreatSummary);
    }

    [Fact]
    public void Aram_TriggersAntiHealEarlier()
    {
        // Warwick con poca ventaja: sustain ~0.23, bajo el umbral normal (0.25) pero
        // sobre el de ARAM (0.25 × 0.6) — en ARAM las peleas son constantes.
        var players = new[]
        {
            ("Ahri", "ORDER", 0, None),
            ("Warwick", "CHAOS", 1, None),
            ("Zed", "CHAOS", 2, None),
            ("Jinx", "CHAOS", 2, None),
        };
        var onRift = Advisor().Advise(TestCatalog.State(5000, players))!;
        var onAram = Advisor().Advise(TestCatalog.AramState(5000, players))!;

        Assert.DoesNotContain(onRift.Recommendations,
            r => r.Reasons.Any(reason => reason.Contains("healing")));
        Assert.Contains(onAram.Recommendations,
            r => r.Reasons.Any(reason => reason.Contains("healing")));
    }

    [Fact]
    public void OwnedUpgrade_HidesItsBaseItem()
    {
        // Tengo la mejora Obra Maestra (7031, construida desde Filo Infinito): el asesor
        // no debe volver a recomendarme el Filo Infinito (3031) aunque su id no coincida.
        var state = TestCatalog.State(9000,
            ("Jinx", "ORDER", 0, new[] { 7031 }),
            ("Soraka", "CHAOS", 0, None));
        var plan = Advisor().Advise(state)!;

        Assert.NotEmpty(plan.Recommendations);
        Assert.DoesNotContain(plan.Recommendations, r => r.Item.Id == 3031);
    }

    [Fact]
    public void OwnedComponentsOfUpgrade_AreAlsoHidden()
    {
        // La exclusión baja por todo el árbol: 7031 → 3031 → B.F. Sword/Cloak; poseer la
        // mejora no debe hacer que el planificador sugiera recomprar el item base.
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, new[] { 3031 }),
            ("Soraka", "CHAOS", 0, None));
        var plan = Advisor().Advise(state)!;

        Assert.DoesNotContain(plan.Recommendations, r => r.Item.Id == 3031);
    }

    [Fact]
    public void DetectedTankBuild_ShiftsRecommendationsAndSummary()
    {
        // Aatrox (fighter por defecto) llevando Thornmail + Sunfire: el plan debe seguir
        // la build de tanque detectada y anunciarla en el banner.
        var state = TestCatalog.State(9000,
            ("Aatrox", "ORDER", 0, new[] { 3075, 3068 }),
            ("Zed", "CHAOS", 2, None),
            ("Ahri", "CHAOS", 2, None));
        var plan = Advisor().Advise(state)!;

        Assert.Equal(BuildArchetype.Tank, plan.MyProfile.Archetype);
        Assert.Contains("Build detected from your items: tank", plan.ThreatSummary);
        // Las recomendaciones son de tanque: nada de crítico/AD puro.
        Assert.All(plan.Recommendations, r => Assert.True(
            r.Item.HasTag("Health") || r.Item.HasTag("Armor") || r.Item.HasTag("SpellBlock"),
            $"{r.Item.Name} no es un item de tanque"));
    }

    [Fact]
    public void DuplicateNameItems_NeverRecommendedTwice()
    {
        // El catálogo tiene "Sunfire Aegis" con dos ids (3068 y 9068): jamás las dos juntas.
        var state = TestCatalog.State(9000,
            ("Aatrox", "ORDER", 0, new[] { 3075 }), // build tanque detectada (Thornmail)
            ("Zed", "CHAOS", 3, None),
            ("Vayne", "CHAOS", 3, None));
        var plan = Advisor().Advise(state)!;

        var names = plan.Recommendations.Select(r => r.Item.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void OwnedItem_ExcludesItsDuplicateIdVariant()
    {
        // Tengo la Sunfire 3068: la variante 9068 (mismo nombre, otro id) tampoco se recomienda.
        var state = TestCatalog.State(9000,
            ("Aatrox", "ORDER", 0, new[] { 3068, 3075 }),
            ("Zed", "CHAOS", 3, None),
            ("Vayne", "CHAOS", 3, None));
        var plan = Advisor().Advise(state)!;

        Assert.DoesNotContain(plan.Recommendations,
            r => r.Item.Name.Equals("Sunfire Aegis", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DefaultBuild_HasNoDetectionNote()
    {
        var state = TestCatalog.State(5000, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 2, None));
        var plan = Advisor().Advise(state)!;

        Assert.DoesNotContain("Build detected", plan.ThreatSummary);
        Assert.False(plan.MyProfile.InferredFromItems);
    }

    [Fact]
    public void EveryRecommendation_HasAtLeastOneReason()
    {
        var state = TestCatalog.State(3000,
            ("Aatrox", "ORDER", 0, None),
            ("Jinx", "CHAOS", 4, None),
            ("Ahri", "CHAOS", 4, None));
        var plan = Advisor().Advise(state)!;

        Assert.NotEmpty(plan.Recommendations);
        Assert.All(plan.Recommendations, r => Assert.NotEmpty(r.Reasons));
        Assert.True(plan.Recommendations.Count <= 3);
    }

    // --- Scoring difuso v3: prioridad coherente y counters de pasivas/build enemiga ---

    [Fact]
    public void Priority_IsScoreRelativeToTop_AndNonIncreasing()
    {
        var state = TestCatalog.State(6000,
            ("Ahri", "ORDER", 0, None),
            ("Jinx", "CHAOS", 6, None),
            ("Leona", "CHAOS", 0, new[] { 3075, 1029 }));
        var plan = Advisor().Advise(state)!;

        Assert.True(plan.Recommendations.Count > 1);
        Assert.Equal(1.0, plan.Top!.Priority, precision: 6);
        for (var i = 0; i < plan.Recommendations.Count; i++)
        {
            var r = plan.Recommendations[i];
            Assert.Equal(r.Score / plan.Top.Score, r.Priority, precision: 6); // prioridad = fracción del top
            Assert.InRange(r.Priority, 0.0, 1.0);
            if (i > 0)
                Assert.True(r.Priority <= plan.Recommendations[i - 1].Priority, "prioridad no decreciente");
        }
    }

    [Fact]
    public void AntiCrit_TankPrefersCritReductionArmor_VsCritMarksman()
    {
        // Tanque vs un tirador de crítico: entre items de armadura+vida, gana el anti-crit
        // (Presagio de Randuin) por reducir el daño crítico entrante.
        var state = TestCatalog.State(9000,
            ("Leona", "ORDER", 0, None),
            ("Jinx", "CHAOS", 8, new[] { 3031 }));   // Filo Infinito = crítico
        var plan = Advisor().Advise(state, BuildArchetype.Tank)!;

        var randuins = plan.Recommendations.FirstOrDefault(r => r.Item.Id == 3143);
        Assert.NotNull(randuins);
        Assert.Contains(randuins!.Reasons, r => r.Contains("crit"));
        // Ningún item de armadura+vida "plano" recomendado le gana en puntaje.
        foreach (var r in plan.Recommendations.Where(r =>
                     r.Item.HasTag("Armor") && r.Item.HasTag("Health") && !r.Item.ReducesCritDamage))
            Assert.True(randuins.Score >= r.Score, $"{r.Item.Name} superó al anti-crit");
    }

    [Fact]
    public void EnemyAntiHeal_DevaluesLifestealItem()
    {
        // Mismo enemigo físico; en un caso ya compró anti-curación (Ejecutor). El robo de
        // vida (Filo de Rey) debe puntuar MENOS cuando su curación ya está cortada.
        StaticItemScore Bork(params (string, string, int, int[])[] players) =>
            new(new ItemAdvisor(TestCatalog.Catalog()).Advise(TestCatalog.State(6000, players))!);

        // Morello (3165): Heridas Graves puras — sin lifesteal (no da "sustain" al enemigo)
        // ni armadura, así el ÚNICO cambio es EnemyAntiHeal. Zed sigue siendo físico.
        var without = Bork(("Aatrox", "ORDER", 0, None), ("Zed", "CHAOS", 3, None));
        var with = Bork(("Aatrox", "ORDER", 0, None), ("Zed", "CHAOS", 3, new[] { 3165 }));

        Assert.True(without.Of(3153) > 0, "Filo de Rey debería recomendarse sin anti-heal enemigo");
        Assert.True(with.Of(3153) < without.Of(3153),
            $"con anti-heal enemigo el lifesteal no bajó: {with.Of(3153)} vs {without.Of(3153)}");
    }

    [Fact]
    public void PenetrationScore_IsMonotonic_InEnemyArmor()
    {
        // Sin acantilados: a más armadura enemiga, la penetración nunca puntúa menos.
        double Pen(int[] enemyArmorItems) => new StaticItemScore(
            new ItemAdvisor(TestCatalog.Catalog()).Advise(TestCatalog.State(9000,
                ("Zed", "ORDER", 0, None),
                ("Leona", "CHAOS", 0, enemyArmorItems)))!).Of(3036); // Lord Dominik's

        var low = Pen(new[] { 1029, 1029 });         // 80 armadura (bajo el umbral duro de 150)
        var mid = Pen(new[] { 1029, 1029, 1029 });   // 120 (aún bajo el umbral)
        var high = Pen(new[] { 1029, 1029, 1029, 1029, 1029, 1029 }); // 240

        // La clave anti-acantilado: por DEBAJO del viejo umbral (150) la penetración ya
        // sube con la armadura, no salta de golpe. Con umbral duro low==mid (ambos 0).
        Assert.True(mid > low, $"respuesta plana bajo el umbral: {mid} ≤ {low}");
        Assert.True(high > mid, $"{high} ≤ {mid}");
    }

    [Fact]
    public void TankyEnemies_BoostPenetrationAndOnHit_ForMarksman()
    {
        // Mismo item on-hit (Filo de Rey) vale MÁS contra un equipo gordo (aunque sea HP
        // puro, sin armadura) que contra uno frágil: tu daño quiere penetrar, no rebotar.
        double Bork(int[] enemyItems) => new StaticItemScore(
            new ItemAdvisor(TestCatalog.Catalog()).Advise(TestCatalog.State(9000,
                ("Jinx", "ORDER", 0, None),
                ("Leona", "CHAOS", 0, enemyItems)))!).Of(3153); // BotRK (OnHit)

        var vsTanky = Bork(new[] { 3083, 3083 });  // Warmog ×2 = 2000 HP, 0 armadura
        var vsSquishy = Bork(new int[0]);

        Assert.True(vsTanky > 0 && vsSquishy > 0, "BotRK debería recomendarse en ambos casos");
        Assert.True(vsTanky > vsSquishy,
            $"el on-hit no reaccionó a la tankiness enemiga: {vsTanky} vs {vsSquishy}");
    }

    [Fact]
    public void HardEngage_BoostsSurvivalItem_ForSquishy()
    {
        // Mismo estado; solo cambia si el rival cuenta como enganche duro (lista de config).
        // Una maga frágil vs enganche valora más un híbrido de supervivencia (Zhonya).
        var state = TestCatalog.State(9000,
            ("Ahri", "ORDER", 0, None),
            ("Leona", "CHAOS", 3, None));
        var cat = TestCatalog.Catalog();

        var withEngage = new StaticItemScore(new ItemAdvisor(cat).Advise(state)!).Of(3157);
        var noEngage = new StaticItemScore(new ItemAdvisor(cat,
            new Core.Config.ItemsConfig { HardEngageChampions = new() }).Advise(state)!).Of(3157);

        Assert.True(withEngage > noEngage,
            $"la supervivencia vs enganche no subió Zhonya: {withEngage} vs {noEngage}");
    }

    [Fact]
    public void PercentHpTrueDamage_DampensSingleResistStacking()
    {
        // Mismo tanque, mismo carry físico de crítico; solo cambia si su daño ignora
        // resistencias. Contra daño %HP/verdadero (Vayne) apilar un muro rinde menos.
        double ArmorScore(string carry) => new StaticItemScore(
            new ItemAdvisor(TestCatalog.Catalog()).Advise(TestCatalog.State(9000,
                ("Leona", "ORDER", 0, None),
                (carry, "CHAOS", 5, None)), BuildArchetype.Tank)!).Of(3143); // Randuin's

        var vsPercentHp = ArmorScore("Vayne");  // % de vida / daño verdadero
        var vsRegular = ArmorScore("Jinx");     // crítico físico "normal"

        Assert.True(vsPercentHp > 0 && vsRegular > 0, "Randuin debería recomendarse en ambos casos");
        Assert.True(vsPercentHp < vsRegular,
            $"apilar armadura no se atenuó vs daño %HP/verdadero: {vsPercentHp} vs {vsRegular}");
    }

    [Fact]
    public void Category_ExplainsWhyItemIsRecommended()
    {
        // Penetración dominante vs. armadura apilada → Counter (contrapartida ofensiva).
        var counter = TestCatalog.State(9000,
            ("Zed", "ORDER", 0, None),
            ("Leona", "CHAOS", 0, new[] { 3075, 1029, 3068 }));
        var pen = Advisor().Advise(counter)!.Recommendations
            .First(r => r.Item.HasTag("ArmorPenetration"));
        Assert.Equal(RecommendationCategory.Counter, pen.Category);

        // Daño enemigo parejo y sin amenaza situacional → el top se recomienda por fit puro.
        var coreState = TestCatalog.State(1500,
            ("Ahri", "ORDER", 0, None),
            ("Zed", "CHAOS", 2, None),
            ("Ahri", "CHAOS", 2, None));
        var top = Advisor().Advise(coreState)!.Top!;
        Assert.True(top.Category is RecommendationCategory.Core or RecommendationCategory.Spike,
            $"esperaba Core/Spike, fue {top.Category}");
    }

    [Fact]
    public void HighLiveArmor_DampsArmorWallBonus()
    {
        // Con mucha armadura ya en los stats vivos, el bono del muro de armadura baja: un
        // tercer item de armadura no debe apilarse a ciegas. Sin datos vivos → sin cambio.
        GameState Vs(double liveArmor)
        {
            var s = TestCatalog.State(20000,
                ("Jinx", "ORDER", 0, None),
                ("Zed", "CHAOS", 0, None),
                ("Vayne", "CHAOS", 0, None));
            s.ActivePlayer!.Level = 11;
            s.ActivePlayer.ChampionStats.MaxHealth = 1800;
            s.ActivePlayer.ChampionStats.Armor = liveArmor;
            return s;
        }
        var advisor = new ItemAdvisor(TestCatalog.Catalog(),
            new ItemsConfig { MaxRecommendations = 20 });

        var low = new StaticItemScore(advisor.Advise(Vs(15))!).Of(3026);    // Guardian Angel (Armor)
        var high = new StaticItemScore(advisor.Advise(Vs(260))!).Of(3026);

        Assert.True(low > high, $"low={low} high={high}");
    }

    [Fact]
    public void CcCounter_RecommendedVsHeavyCc_WithoutSuppressor()
    {
        // Comp de CC pesado sin supresor (Leona+Amumu): la regla de supresión NO dispara,
        // pero el grado CcThreat sí — un item de limpieza sube por "crowd control".
        var state = TestCatalog.State(20000,
            ("Jinx", "ORDER", 0, None),
            ("Leona", "CHAOS", 0, None),
            ("Amumu", "CHAOS", 0, None),
            ("Zed", "CHAOS", 0, None));
        var advisor = new ItemAdvisor(TestCatalog.Catalog(),
            new ItemsConfig { MaxRecommendations = 10 });

        var plan = advisor.Advise(state)!;

        Assert.Contains(plan.Recommendations,
            r => r.Reasons.Any(reason => reason.Contains("crowd control")));
    }

    [Fact]
    public void ExclusiveGroup_NotPoisoned_ByGwSkippedItem()
    {
        // Tirador vs comp que cura (Warwick+Aatrox): Grievous Edge (GW, mayor fit de
        // tirador) supera por fit a Recordatorio Mortal y toma el cupo de Heridas Graves;
        // Recordatorio Mortal (GW + grupo Last Whisper) se saltea por gwTaken — y NO debe
        // marcar el grupo y bloquear a Lord Dominik's (mismo grupo, alto fit). Con la
        // corrección, un item GW salteado ya no envenena su grupo y Lord Dominik's entra.
        var state = TestCatalog.AramState(20000,
            ("Jinx", "ORDER", 0, None),
            ("Warwick", "CHAOS", 0, None),
            ("Aatrox", "CHAOS", 0, None),
            ("Zed", "CHAOS", 0, None));
        var advisor = new ItemAdvisor(TestCatalog.Catalog(),
            new ItemsConfig { MaxRecommendations = 10 });

        var plan = advisor.Advise(state)!;

        var names = plan.Recommendations.Select(r => r.Item.Name).ToList();
        Assert.Contains("Grievous Edge", names);             // el GW que sí entró
        Assert.DoesNotContain("Mortal Reminder", names);     // cupo GW ya tomado
        Assert.Contains("Lord Dominik's Regards", names);    // NO bloqueado por el grupo
    }

    [Fact]
    public void ShopAlert_QuotesRemainingCost_WhenComponentsOwned()
    {
        // Jinx muerta con componentes ya comprados: el aviso de tienda abierta debe citar
        // lo que FALTA pagar del top (RemainingCost), no su precio de lista — es la
        // ventana donde el jugador actúa sobre el consejo.
        var state = TestCatalog.AramState(3000,
            ("Jinx", "ORDER", 0, new[] { 1018, 1036, 1053 }),
            ("Malzahar", "CHAOS", 0, None));
        state.AllPlayers[0].IsDead = true;
        var advisor = new ItemAdvisor(TestCatalog.Catalog());

        var plan = advisor.Advise(state)!;
        var top = plan.Recommendations[0];

        Assert.NotNull(plan.ShopAlert);
        Assert.True(top.Purchase.CanFinishNow, "el escenario espera un top ya alcanzable");
        Assert.True(top.Purchase.RemainingCost < top.Item.GoldTotal,
            "el escenario debe descontar componentes; si no, ajustar los items poseídos");
        Assert.Contains(top.Purchase.RemainingCost.ToString("N0",
            System.Globalization.CultureInfo.InvariantCulture), plan.ShopAlert);
    }

    /// <summary>Ayuda de test: busca el puntaje de un item por id en un plan (0 si no está).</summary>
    private sealed class StaticItemScore
    {
        private readonly ItemAdvicePlan _plan;
        public StaticItemScore(ItemAdvicePlan plan) => _plan = plan;
        public double Of(int itemId) =>
            _plan.Recommendations.FirstOrDefault(r => r.Item.Id == itemId)?.Score ?? 0;
    }
}
