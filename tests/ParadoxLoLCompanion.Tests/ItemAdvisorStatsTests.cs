using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

public class ItemAdvisorStatsTests
{
    // Catálogo mínimo: un campeón (partype configurable), un item de maná y uno de AP.
    // Los items cumplen los filtros de "completado": purchasable, from, oro >= 1100, mapa 11.
    internal static DataDragonCatalog Catalog(string partype = "Mana") => DataDragonCatalog.FromJson(
        "16.13.1",
        $$"""
        {"data":{"TestChamp":{"id":"TestChamp","key":"999","name":"Test Champ",
          "partype":"{{partype}}","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9} },
          "Enemy":{"id":"Enemy","key":"998","name":"Enemy",
          "partype":"Mana","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9} } } }
        """,
        """
        {"data":{
          "1026":{"name":"Blasting Wand","gold":{"total":850,"sell":595,"purchasable":true},
                  "tags":["SpellDamage"],"maps":{"11":true,"12":true},"into":["4001","4002"]},
          "4001":{"name":"Mana Tome Item","gold":{"total":2900,"sell":2030,"purchasable":true},
                  "tags":["SpellDamage","Mana","ManaRegen"],"maps":{"11":true,"12":true},
                  "from":["1026"],"depth":2,
                  "stats":{"FlatMagicDamageMod":60}},
          "4002":{"name":"Pure AP Item","gold":{"total":2900,"sell":2030,"purchasable":true},
                  "tags":["SpellDamage"],"maps":{"11":true,"12":true},
                  "from":["1026"],"depth":2,
                  "stats":{"FlatMagicDamageMod":60}},
          "4005":{"name":"Other AP Item","gold":{"total":2900,"sell":2030,"purchasable":true},
                  "tags":["SpellDamage"],"maps":{"11":true,"12":true},
                  "from":["1026"],"depth":2,
                  "stats":{"FlatMagicDamageMod":60}}}}
        """);

    internal static GameState State()
    {
        var state = new GameState
        {
            ActivePlayer = new ActivePlayer { SummonerName = "Me", CurrentGold = 3000 },
            GameData = new GameData { GameTime = 900, GameMode = "CLASSIC", MapNumber = 11 },
        };
        state.AllPlayers.Add(new Player
        {
            SummonerName = "Me", ChampionName = "Test Champ",
            RawChampionName = "game_character_displayname_TestChamp", Team = "ORDER",
        });
        state.AllPlayers.Add(new Player
        {
            SummonerName = "Foe", ChampionName = "Enemy",
            RawChampionName = "game_character_displayname_Enemy", Team = "CHAOS",
        });
        return state;
    }

    [Fact]
    public void Manaless_champion_never_gets_mana_items()
    {
        var advisor = new ItemAdvisor(Catalog(partype: "Energy"));
        var plan = advisor.Advise(State(), BuildArchetype.Mage)!;
        Assert.DoesNotContain(plan.Recommendations, r => r.Item.Name == "Mana Tome Item");
        Assert.Contains(plan.Recommendations, r => r.Item.Name == "Pure AP Item");
    }

    [Fact]
    public void Mana_champion_still_gets_mana_items()
    {
        var advisor = new ItemAdvisor(Catalog(partype: "Mana"));
        var plan = advisor.Advise(State(), BuildArchetype.Mage)!;
        Assert.Contains(plan.Recommendations, r => r.Item.Name == "Mana Tome Item");
    }

    private static ChampionBuildStats StatsWith(params int[] coreIds) => new()
    {
        ChampionKey = "TestChamp",
        GameMode = "ranked",
        Position = "mid",
        CoreItems = new ItemSetStats(coreIds, PickRate: 0.25, Play: 10000, Win: 5600),
    };

    [Fact]
    public void Prior_boosts_the_statistically_core_item()
    {
        var advisor = new ItemAdvisor(Catalog());
        var state = State();

        // Sin stats, ambos items AP puros empatan; con prior, 4002 debe ir primero.
        var withStats = advisor.Advise(state, BuildArchetype.Mage, StatsWith(4002))!;
        Assert.Equal("Pure AP Item", withStats.Recommendations[0].Item.Name);
        Assert.Contains(withStats.Recommendations[0].Reasons,
            r => r.Contains("of Test Champ builds"));
    }

    [Fact]
    public void Null_stats_change_nothing()
    {
        var advisor = new ItemAdvisor(Catalog());
        var state = State();
        var without = advisor.Advise(state, BuildArchetype.Mage)!;
        var withNull = advisor.Advise(state, BuildArchetype.Mage, stats: null)!;
        Assert.Equal(
            without.Recommendations.Select(r => (r.Item.Id, r.Score)),
            withNull.Recommendations.Select(r => (r.Item.Id, r.Score)));
    }

    [Fact]
    public void Prior_bridges_item_evolutions_via_config()
    {
        // OP.GG lista la forma evolucionada (4003, no comprable y SIN from — igual
        // que Muramana en ddragon real); el catálogo recomienda la comprable (4002).
        // El puente es el mapa ItemEvolutions de la config.
        var config = new ParadoxLoLCompanion.Core.Config.ItemsConfig();
        config.ItemEvolutions[4003] = 4002;
        var catalog = DataDragonCatalog.FromJson(
            "16.13.1",
            """
            {"data":{"TestChamp":{"id":"TestChamp","key":"999","name":"Test Champ",
              "partype":"Mana","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9}},
              "Enemy":{"id":"Enemy","key":"998","name":"Enemy",
              "partype":"Mana","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9}}}}
            """,
            """
            {"data":{
              "1026":{"name":"Blasting Wand","gold":{"total":850,"sell":595,"purchasable":true},
                      "tags":["SpellDamage"],"maps":{"11":true},"into":["4002","4004"]},
              "4002":{"name":"Buyable Form","gold":{"total":2900,"sell":2030,"purchasable":true},
                      "tags":["SpellDamage"],"maps":{"11":true},"from":["1026"],"depth":2,
                      "stats":{"FlatMagicDamageMod":60}},
              "4003":{"name":"Evolved Form","gold":{"total":2900,"sell":2030,"purchasable":false},
                      "tags":["SpellDamage"],"maps":{"11":true},
                      "stats":{"FlatMagicDamageMod":80}},
              "4004":{"name":"Other AP","gold":{"total":2900,"sell":2030,"purchasable":true},
                      "tags":["SpellDamage"],"maps":{"11":true},"from":["1026"],"depth":2,
                      "stats":{"FlatMagicDamageMod":60}}}}
            """, config);
        var advisor = new ItemAdvisor(catalog, config);
        var plan = advisor.Advise(State(), BuildArchetype.Mage, StatsWith(4003))!;
        Assert.Equal("Buyable Form", plan.Recommendations[0].Item.Name);
    }

    [Fact]
    public void Core_item_prior_outweighs_late_item_prior()
    {
        // Escenario real (Ahri, parche 16.13.1): el pick del SET núcleo es la
        // probabilidad de un combo de 3 items (~0.11) mientras que un candidato
        // tardío es un item suelto (~0.31). Sin corregir la escala, Rabadon (4.º
        // opcional) recibía 5× el bono de los items del core real.
        var stats = new ChampionBuildStats
        {
            ChampionKey = "TestChamp",
            GameMode = "ranked",
            Position = "mid",
            CoreItems = new ItemSetStats(new[] { 4005 }, PickRate: 0.11, Play: 10750, Win: 5609),
            LateItems = new[] { new ItemSetStats(new[] { 4002 }, PickRate: 0.31, Play: 12759, Win: 7525) },
        };
        var advisor = new ItemAdvisor(Catalog());
        var plan = advisor.Advise(State(), BuildArchetype.Mage, stats)!;
        // Ambos items son AP puro idéntico: el prior decide el orden y el core manda.
        Assert.Equal("Other AP Item", plan.Recommendations[0].Item.Name);
    }

    [Fact]
    public void Tiny_sample_prior_is_ignored()
    {
        // Un candidato con 150 partidas es ruido: no debe sumar bono ni razón.
        var stats = new ChampionBuildStats
        {
            ChampionKey = "TestChamp",
            GameMode = "ranked",
            Position = "mid",
            LateItems = new[] { new ItemSetStats(new[] { 4002 }, PickRate: 0.31, Play: 150, Win: 95) },
        };
        var advisor = new ItemAdvisor(Catalog());
        var plan = advisor.Advise(State(), BuildArchetype.Mage, stats)!;
        Assert.DoesNotContain(plan.Recommendations,
            r => r.Reasons.Any(reason => reason.Contains("builds")));
    }

    [Fact]
    public void Stat_prior_keeps_core_category()
    {
        var advisor = new ItemAdvisor(Catalog());
        var plan = advisor.Advise(State(), BuildArchetype.Mage, StatsWith(4002))!;
        var top = plan.Recommendations[0];
        // Prior estadístico refuerza el fit: la categoría sigue siendo Core/Spike,
        // no Counter (no hay contrapartida situacional aquí).
        Assert.NotEqual(RecommendationCategory.Counter, top.Category);
        Assert.NotEqual(RecommendationCategory.Defense, top.Category);
    }

    // --- Botas: la meta (op.gg) manda; la amenaza queda como nota ---

    private static readonly int[] NoItems = Array.Empty<int>();

    private static ChampionBuildStats BootsStats(params int[] bootIds) => new()
    {
        ChampionKey = "Jinx",
        GameMode = "ranked",
        Position = "adc",
        Boots = new ItemSetStats(bootIds, PickRate: 0.62, Play: 9000, Win: 4700),
    };

    [Fact]
    public void StarterSet_FromOpgg_RecommendsMultipleWithinBudget()
    {
        // op.gg trae un set de apertura de 2 items: con presupuesto (1400) se recomiendan
        // ambos; con presupuesto ajustado (500) solo el primero (D3).
        var stats = new ChampionBuildStats
        {
            ChampionKey = "Jinx",
            GameMode = "aram",
            Position = "mid",
            Starter = new ItemSetStats(new[] { 1055, 2051 }, 0.5, 5000, 2700),   // Doran's + Guardian's Horn
        };
        var advisor = new ItemAdvisor(TestCatalog.Catalog());

        var full = advisor.Advise(TestCatalog.AramState(1400,
            ("Jinx", "ORDER", 0, NoItems), ("Zed", "CHAOS", 0, NoItems)), stats: stats)!;
        Assert.Equal(new[] { 1055, 2051 }, full.Starter!.Items.Select(i => i.Id));

        var tight = advisor.Advise(TestCatalog.AramState(500,
            ("Jinx", "ORDER", 0, NoItems), ("Zed", "CHAOS", 0, NoItems)), stats: stats)!;
        Assert.Equal(new[] { 1055 }, tight.Starter!.Items.Select(i => i.Id));
    }

    private static ChampionBuildStats BootsStatsLowSample(params int[] bootIds) => new()
    {
        ChampionKey = "Jinx",
        GameMode = "ranked",
        Position = "adc",
        Boots = new ItemSetStats(bootIds, PickRate: 0.62, Play: 40, Win: 25),   // muestra trivial
    };

    [Fact]
    public void ItemPriorFor_IsSlotAware_ByCompletedCount()
    {
        // Un item presente en el 4.º y en el 6.º slot hereda las stats del slot que
        // corresponde al progreso de build, no siempre las del 4.º (D2).
        var stats = new ChampionBuildStats
        {
            CoreItems = new ItemSetStats(new[] { 1001 }, 0.3, 5000, 2600),
            FourthItems = new[] { new ItemSetStats(new[] { 3153 }, 0.30, 4000, 2100) },
            SixthItems = new[] { new ItemSetStats(new[] { 3153 }, 0.10, 900, 480) },
        };

        Assert.Equal(0.30, stats.ItemPriorFor(3153, completedCount: 3)!.Value.PickRate, precision: 3);
        Assert.Equal(0.10, stats.ItemPriorFor(3153, completedCount: 5)!.Value.PickRate, precision: 3);
        Assert.Null(stats.ItemPriorFor(3153));   // sin contexto: no está en core ni en el aplanado
    }

    [Fact]
    public void Boots_TrivialSample_FallsBackToThreat()
    {
        // Muestra de op.gg trivial (día 1 de parche, 40 partidas): NO debe pisar la amenaza
        // (Malzahar+Leona+Amumu → Mercs). La regla "la meta manda" solo vale con datos reales.
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, NoItems),
            ("Malzahar", "CHAOS", 0, NoItems),
            ("Leona", "CHAOS", 0, NoItems),
            ("Amumu", "CHAOS", 0, NoItems));
        var advisor = new ItemAdvisor(TestCatalog.Catalog());

        var plan = advisor.Advise(state, stats: BootsStatsLowSample(3020))!;

        Assert.NotNull(plan.Boots);
        Assert.Equal(3111, plan.Boots!.Boots.Id);   // Mercury's Treads (amenaza), no Sorcerer's
        Assert.DoesNotContain("pick rate", plan.Boots.Reason);
    }

    [Fact]
    public void Boots_OpggMetaWins_ThreatBecomesNote()
    {
        // Misma amenaza que Boots_MercsAgainstCcAndMagic (CC pesado → Mercs),
        // pero op.gg dice Sorcerer's: la meta manda y la amenaza queda como nota.
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, NoItems),
            ("Malzahar", "CHAOS", 0, NoItems),
            ("Leona", "CHAOS", 0, NoItems),
            ("Amumu", "CHAOS", 0, NoItems));
        var advisor = new ItemAdvisor(TestCatalog.Catalog());

        var plan = advisor.Advise(state, stats: BootsStats(3020))!;

        Assert.NotNull(plan.Boots);
        Assert.Equal(3020, plan.Boots!.Boots.Id);
        Assert.Contains("pick rate", plan.Boots.Reason);
        Assert.Contains("consider Mercury's Treads", plan.Boots.Reason);
    }

    [Fact]
    public void Boots_OpggMeta_NoNote_WhenThreatAgrees_OrNoSkew()
    {
        // Sin sesgo de amenaza: op.gg decide y la razón no lleva nota.
        // Además op.gg trae dos botas y gana la primera de SU orden (3158),
        // no la primera del orden del catálogo.
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, NoItems),
            ("Zed", "CHAOS", 2, NoItems),
            ("Ahri", "CHAOS", 2, NoItems));
        var advisor = new ItemAdvisor(TestCatalog.Catalog());

        var plan = advisor.Advise(state, stats: BootsStats(3158, 3006))!;

        Assert.NotNull(plan.Boots);
        Assert.Equal(3158, plan.Boots!.Boots.Id);
        Assert.Contains("pick rate", plan.Boots.Reason);
        Assert.DoesNotContain("consider", plan.Boots.Reason);
    }

    [Fact]
    public void Boots_ThreatFallback_WhenOpggBootsNotInCatalog()
    {
        // op.gg trae un id que no existe en el mapa: fallback a la amenaza (Mercs).
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, NoItems),
            ("Malzahar", "CHAOS", 0, NoItems),
            ("Leona", "CHAOS", 0, NoItems),
            ("Amumu", "CHAOS", 0, NoItems));
        var advisor = new ItemAdvisor(TestCatalog.Catalog());

        var plan = advisor.Advise(state, stats: BootsStats(99999))!;

        Assert.NotNull(plan.Boots);
        Assert.Equal(3111, plan.Boots!.Boots.Id);
        Assert.Contains("CC", plan.Boots.Reason);
    }
}
