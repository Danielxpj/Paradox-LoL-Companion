using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Tests;

/// <summary>Coherencia del asesor: inventario lleno y botas accionables.</summary>
public class ItemAdvisorCoherenceTests
{
    // Catálogo mínimo: campeón mago, un componente (1026) que arma dos items AP,
    // y botas básicas (1001) → tier 2 con pen mágica (3020).
    private static DataDragonCatalog Catalog() => DataDragonCatalog.FromJson(
        "16.13.1",
        """
        {"data":{"TestChamp":{"id":"TestChamp","key":"999","name":"Test Champ",
          "partype":"Mana","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9}},
          "Enemy":{"id":"Enemy","key":"998","name":"Enemy",
          "partype":"Mana","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9}}}}
        """,
        """
        {"data":{
          "1001":{"name":"Boots","gold":{"total":300,"sell":210,"purchasable":true},
                  "tags":["Boots"],"maps":{"11":true,"12":true},"into":["3020"]},
          "3020":{"name":"Sorcerer's Shoes","gold":{"total":1100,"sell":770,"purchasable":true},
                  "tags":["Boots","MagicPenetration"],"maps":{"11":true,"12":true},
                  "from":["1001"],"depth":2},
          "1026":{"name":"Blasting Wand","gold":{"total":850,"sell":595,"purchasable":true},
                  "tags":["SpellDamage"],"maps":{"11":true,"12":true},"into":["4002","4005"]},
          "4002":{"name":"Pure AP Item","gold":{"total":2900,"sell":2030,"purchasable":true},
                  "tags":["SpellDamage"],"maps":{"11":true,"12":true},
                  "from":["1026"],"depth":2,
                  "stats":{"FlatMagicDamageMod":60}},
          "4005":{"name":"Other AP Item","gold":{"total":2900,"sell":2030,"purchasable":true},
                  "tags":["SpellDamage"],"maps":{"11":true,"12":true},
                  "from":["1026"],"depth":2,
                  "stats":{"FlatMagicDamageMod":60}},
          "2139":{"name":"Elixir of Sorcery","gold":{"total":500,"sell":200,"purchasable":true},
                  "tags":["Consumable"],"maps":{"11":true,"12":true},"consumed":true},
          "2055":{"name":"Control Ward","gold":{"total":75,"sell":30,"purchasable":true},
                  "tags":["Consumable","Vision"],"maps":{"11":true},"consumed":true}}}
        """);

    private static GameState State(double gold, params Item[] items)
    {
        var state = new GameState
        {
            ActivePlayer = new ActivePlayer { SummonerName = "Me", CurrentGold = gold },
            GameData = new GameData { GameTime = 900, GameMode = "CLASSIC", MapNumber = 11 },
        };
        var me = new Player
        {
            SummonerName = "Me", ChampionName = "Test Champ",
            RawChampionName = "game_character_displayname_TestChamp", Team = "ORDER",
        };
        me.Items.AddRange(items);
        state.AllPlayers.Add(me);
        state.AllPlayers.Add(new Player
        {
            SummonerName = "Foe", ChampionName = "Enemy",
            RawChampionName = "game_character_displayname_Enemy", Team = "CHAOS",
        });
        return state;
    }

    private static Item Slot(int slot, int itemId) => new() { ItemID = itemId, Slot = slot, Count = 1 };

    [Fact]
    public void Full_inventory_marks_plan_and_blocks_unmergeable_recos()
    {
        var advisor = new ItemAdvisor(Catalog());
        // 6 slots ocupados por items ajenos al catálogo: nada fusiona componentes.
        var state = State(gold: 5000,
            Slot(0, 9001), Slot(1, 9002), Slot(2, 9003),
            Slot(3, 9004), Slot(4, 9005), Slot(5, 9006));

        var plan = advisor.Advise(state, BuildArchetype.Mage)!;
        Assert.True(plan.InventoryFull);
        Assert.NotEmpty(plan.Recommendations);
        Assert.All(plan.Recommendations, r => Assert.True(r.BlockedByFullInventory));
    }

    [Fact]
    public void Full_inventory_reco_that_merges_owned_component_is_not_blocked()
    {
        var advisor = new ItemAdvisor(Catalog());
        // Slot 5 tiene el componente 1026: comprar 4002/4005 lo consume y libera slot.
        var state = State(gold: 5000,
            Slot(0, 9001), Slot(1, 9002), Slot(2, 9003),
            Slot(3, 9004), Slot(4, 9005), Slot(5, 1026));

        var plan = advisor.Advise(state, BuildArchetype.Mage)!;
        Assert.True(plan.InventoryFull);
        Assert.All(plan.Recommendations, r => Assert.False(r.BlockedByFullInventory));
    }

    [Fact]
    public void Free_slot_means_nothing_is_blocked()
    {
        var advisor = new ItemAdvisor(Catalog());
        var state = State(gold: 5000, Slot(0, 9001), Slot(1, 9002));
        var plan = advisor.Advise(state, BuildArchetype.Mage)!;
        Assert.False(plan.InventoryFull);
        Assert.All(plan.Recommendations, r => Assert.False(r.BlockedByFullInventory));
    }

    [Fact]
    public void Full_build_suggests_the_archetype_elixir()
    {
        var advisor = new ItemAdvisor(Catalog());
        var state = State(gold: 1000,
            Slot(0, 9001), Slot(1, 9002), Slot(2, 9003),
            Slot(3, 9004), Slot(4, 9005), Slot(5, 9006));

        var plan = advisor.Advise(state, BuildArchetype.Mage)!;
        Assert.Contains(plan.LateTips, t => t.Contains("Elixir of Sorcery"));
    }

    [Fact]
    public void No_elixir_tip_without_gold_or_with_free_slots()
    {
        var advisor = new ItemAdvisor(Catalog());
        // Lleno pero sin oro para el elixir.
        var poor = advisor.Advise(State(gold: 300,
            Slot(0, 9001), Slot(1, 9002), Slot(2, 9003),
            Slot(3, 9004), Slot(4, 9005), Slot(5, 9006)), BuildArchetype.Mage)!;
        Assert.DoesNotContain(poor.LateTips, t => t.Contains("Elixir"));
        // Con slots libres tampoco (la build no está completa).
        var open = advisor.Advise(State(gold: 1000, Slot(0, 9001)), BuildArchetype.Mage)!;
        Assert.DoesNotContain(open.LateTips, t => t.Contains("Elixir"));
    }

    [Fact]
    public void Mid_game_without_control_ward_suggests_one_on_rift()
    {
        var advisor = new ItemAdvisor(Catalog());
        // t=900s, sin Control Ward en el inventario, oro de sobra.
        var plan = advisor.Advise(State(gold: 1000, Slot(0, 9001)), BuildArchetype.Mage)!;
        Assert.Contains(plan.LateTips, t => t.Contains("Control Ward"));

        // Con uno encima, no insiste.
        var carrying = advisor.Advise(State(gold: 1000, Slot(0, 2055)), BuildArchetype.Mage)!;
        Assert.DoesNotContain(carrying.LateTips, t => t.Contains("Control Ward"));
    }

    [Fact]
    public void Boots_advice_pushes_basic_boots_when_gold_is_short()
    {
        var advisor = new ItemAdvisor(Catalog());
        // Sin botas y con 400 de oro: el plan debe decir "comprá las Botas básicas ya".
        var plan = advisor.Advise(state: State(gold: 400), BuildArchetype.Mage)!;
        var boots = plan.Boots!;
        Assert.Equal("Sorcerer's Shoes", boots.Boots.Name);
        Assert.Equal(1001, boots.Purchase.NextComponent!.Id);
        Assert.False(boots.Purchase.CanFinishNow);
        Assert.Equal(700, boots.MissingGold);
    }

    [Fact]
    public void Boots_advice_can_finish_now_with_enough_gold()
    {
        var advisor = new ItemAdvisor(Catalog());
        var plan = advisor.Advise(state: State(gold: 2000), BuildArchetype.Mage)!;
        Assert.True(plan.Boots!.Purchase.CanFinishNow);
        Assert.Equal(0, plan.Boots.MissingGold);
    }
}
