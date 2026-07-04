using LoLAdvisor.Core.DataDragon;
using LoLAdvisor.Core.Items;
using LoLAdvisor.Core.Models;
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

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
        var config = new LoLAdvisor.Core.Config.ItemsConfig();
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
}
