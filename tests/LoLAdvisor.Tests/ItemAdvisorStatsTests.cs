using LoLAdvisor.Core.DataDragon;
using LoLAdvisor.Core.Items;
using LoLAdvisor.Core.Models;

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
}
