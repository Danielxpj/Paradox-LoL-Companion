using System.Text.Json.Nodes;
using ParadoxLoLCompanion.Core.Advice.Rules;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

public class ItemReplacementTests
{
    private static DataDragonCatalog Catalog(Action<JsonObject>? edit = null)
    {
        var items = JsonNode.Parse("""
        {"1001":{"name":"Boots","gold":{"total":300,"sell":210,"purchasable":true},
          "tags":["Boots"],"maps":{"11":true,"12":true},"into":["3020"]},
         "3020":{"name":"Finished Boots","gold":{"total":1100,"sell":770,"purchasable":true},
          "tags":["Boots"],"maps":{"11":true,"12":true},"from":["1001"]},
         "1052":{"name":"Tome","gold":{"total":400,"sell":280,"purchasable":true},
          "tags":["SpellDamage"],"maps":{"11":true,"12":true},"into":["9101","9102"],
          "stats":{"FlatMagicDamageMod":20}},
         "9101":{"name":"Magic Ward","gold":{"total":3000,"sell":2100,"purchasable":true},
          "tags":["SpellDamage","SpellBlock"],"maps":{"11":true,"12":true},"from":["1052"],
          "stats":{"FlatMagicDamageMod":90,"FlatSpellBlockMod":50}},
         "9102":{"name":"Armor Ward","gold":{"total":3000,"sell":2100,"purchasable":true},
          "tags":["SpellDamage","Armor"],"maps":{"11":true,"12":true},"from":["1052"],
          "stats":{"FlatMagicDamageMod":90,"FlatArmorMod":45}}}
        """)!.AsObject();
        for (var id = 9103; id <= 9106; id++)
            items[id.ToString()] = JsonNode.Parse($$$"""
            {"name":"Core {{{id}}}","gold":{"total":3400,"sell":2380,"purchasable":true},
             "tags":["SpellDamage","Mana","AbilityHaste"],"maps":{"11":true,"12":true},
             "from":["1052"],"stats":{"FlatMagicDamageMod":180,"FlatMPPoolMod":800}}
            """);
        edit?.Invoke(items);
        return DataDragonCatalog.FromJson("test", """
        {"data":{
          "Ahri":{"id":"Ahri","key":"103","name":"Ahri","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9}},
          "Jinx":{"id":"Jinx","key":"222","name":"Jinx","tags":["Marksman"],"info":{"attack":9,"defense":2,"magic":1}},
          "Lux":{"id":"Lux","key":"99","name":"Lux","tags":["Mage"],"info":{"attack":1,"defense":2,"magic":9}},
          "Soraka":{"id":"Soraka","key":"16","name":"Soraka","tags":["Support"],"info":{"attack":1,"defense":2,"magic":9}},
          "Malzahar":{"id":"Malzahar","key":"90","name":"Malzahar","tags":["Mage"],"info":{"attack":1,"defense":2,"magic":9}}}}
        """, new JsonObject { ["data"] = items }.ToJsonString());
    }

    private static GameState State(double gold = 1000, int oldId = 9101, string enemy = "Jinx")
    {
        var state = TestCatalog.State(gold,
            ("Ahri", "ORDER", 0, new[] { oldId, 9103, 9104, 9105, 9106, 3020 }),
            (enemy, "CHAOS", 0, Array.Empty<int>()));
        for (var i = 0; i < 6; i++)
            state.AllPlayers[0].Items[i].Slot = i;
        state.GameData.GameTime = 2400;
        state.AllPlayers[0].Level = 18;
        return state;
    }

    [Fact]
    public void FullBuild_ReplacesObsoleteResistance_WithExactNetCostAndReasons()
    {
        var plan = new ItemAdvisor(Catalog()).Advise(State())!;
        Assert.True(plan.FullBuild);
        var recommendation = Assert.Single(plan.Recommendations);
        var swap = Assert.IsType<ItemReplacement>(recommendation.Replacement);
        Assert.Equal(9101, swap.SellItem.Id);
        Assert.Equal(0, swap.SellSlot);
        Assert.Equal(9102, recommendation.Item.Id);
        Assert.Equal(2100, swap.SellGold);
        Assert.Equal(900, swap.NetCost);
        Assert.True(recommendation.Affordable);
        Assert.False(recommendation.BlockedByFullInventory);
        Assert.Contains(recommendation.Reasons, r => r.Contains("Jinx"));
        Assert.Equal(recommendation.Score, recommendation.ScoreBreakdown!.Total, 8);
        Assert.Empty(plan.Sells); // no unrelated standalone sell instructions
    }

    [Theory]
    [InlineData(899.5, 1, false)]
    [InlineData(900, 0, true)]
    [InlineData(0, 900, false)]
    public void SaleProceeds_OnlyFundTheWholeReplacement(double gold, int missing, bool affordable)
    {
        var recommendation = new ItemAdvisor(Catalog()).Advise(State(gold))!.Top!;
        Assert.Equal(missing, recommendation.MissingGold);
        Assert.Equal(affordable, recommendation.Affordable);
        if (!affordable)
        {
            Assert.Null(recommendation.Purchase.NextComponent);
            Assert.Contains(recommendation.Reasons, r => r.Contains("keep Magic Ward until"));
        }
    }

    [Fact]
    public void ChangingThreat_ReevaluatesTheBuild_AndDoesNotReverseWithoutAChange()
    {
        var advisor = new ItemAdvisor(Catalog());
        Assert.Equal(9102, advisor.Advise(State())!.Top!.Item.Id);
        var afterPurchase = State(oldId: 9102);
        var hold = advisor.Advise(afterPurchase)!;
        Assert.True(hold.FullBuild);
        Assert.Empty(hold.Recommendations);
        Assert.Contains(hold.LateTips, t => t.Contains("keep your current items"));
        Assert.Equal(9101, advisor.Advise(State(oldId: 9102, enemy: "Lux"))!.Top!.Item.Id);
    }

    [Fact]
    public void MetaPurchaseOrder_DoesNotCauseSwaps_AndCoreItemsNeedAnExtraMargin()
    {
        var stats = new ChampionBuildStats
        {
            ChampionKey = "Ahri", Position = "mid", WinRate = 0.5,
            CoreItems = new ItemSetStats(new[] { 9101, 9103, 9104 }, 0.3, 8000, 4000),
            SixthItems = new[] { new ItemSetStats(new[] { 9102 }, 0.5, 8000, 4500) },
        };
        var advisor = new ItemAdvisor(Catalog());
        Assert.Null(advisor.Advise(State(enemy: "Lux"), stats: stats)!.Top);
        Assert.NotNull(advisor.Advise(State())!.Top);
        Assert.Null(advisor.Advise(State(), stats: stats)!.Top);
        // Core membership is a margin, not an unconditional veto.
        var stronger = Catalog(i => i["9102"]!["stats"]!["FlatMagicDamageMod"] = 130);
        Assert.Equal(9102, new ItemAdvisor(stronger).Advise(State(), stats: stats)!.Top!.Item.Id);
    }

    [Fact]
    public void SameExclusiveGroup_IsLegalOnlyWhenSellingItsExistingMember()
    {
        var config = new ItemsConfig { ExclusiveItemGroups = new() { new() { 9101, 9102 } } };
        Assert.Equal(9102, new ItemAdvisor(Catalog(), config).Advise(State())!.Top!.Item.Id);
        config.ExclusiveItemGroups[0].Add(9103);
        Assert.Null(new ItemAdvisor(Catalog(), config).Advise(State())!.Top);
    }

    [Fact]
    public void SharedPassive_IsLegalAfterSale_ButCannotConflictWithRetainedItems()
    {
        var catalog = Catalog(i =>
        {
            i["9101"]!["description"] = "<passive>Barrier</passive>";
            i["9102"]!["description"] = "<passive>Barrier</passive>";
        });
        Assert.NotNull(new ItemAdvisor(catalog).Advise(State())!.Top);
        catalog = Catalog(i =>
        {
            i["9103"]!["description"] = "<passive>Barrier</passive>";
            i["9102"]!["description"] = "<passive>Barrier</passive>";
        });
        Assert.Null(new ItemAdvisor(catalog).Advise(State())!.Top);
    }

    [Theory]
    [InlineData(1052)] // component
    [InlineData(1001)] // unfinished boots
    [InlineData(9999)] // unknown item
    public void SixOccupiedSlots_AreNotNecessarilyFullBuild(int id)
    {
        var state = State();
        state.AllPlayers[0].Items[5].ItemID = id;
        var plan = new ItemAdvisor(Catalog()).Advise(state)!;
        Assert.True(plan.InventoryFull);
        Assert.False(plan.FullBuild);
        Assert.All(plan.Recommendations, r => Assert.Null(r.Replacement));
    }

    [Fact]
    public void FiveItemsAndATrinket_AreNotFullBuild()
    {
        var state = State();
        state.AllPlayers[0].Items[5].Slot = 6;
        Assert.False(new ItemAdvisor(Catalog()).Advise(state)!.FullBuild);
    }

    [Theory]
    [InlineData("unsellable")]
    [InlineData("evolved")]
    [InlineData("stacking")]
    [InlineData("amplifier")]
    public void PreservesInvestmentsTheScorerCannotFullyValue(string protection)
    {
        var config = new ItemsConfig();
        var catalog = Catalog(i =>
        {
            if (protection == "unsellable") i["9101"]!["gold"]!["sell"] = 0;
            if (protection == "evolved") i["9101"]!["gold"]!["purchasable"] = false;
        });
        if (protection == "stacking") config.StackingComponentIds.Add(9101);
        if (protection == "amplifier") config.ReplacementProtectedItemIds.Add(9101);
        Assert.Null(new ItemAdvisor(catalog, config).Advise(State())!.Top);
    }

    [Fact]
    public void AHighSaleLoss_CanMakeAnOtherwiseUsefulSwapNotWorthwhile()
    {
        Assert.NotNull(new ItemAdvisor(Catalog()).Advise(State())!.Top);
        var expensiveSale = Catalog(i => i["9101"]!["gold"]!["sell"] = 1000);
        Assert.Null(new ItemAdvisor(expensiveSale).Advise(State())!.Top);
    }

    [Fact]
    public void LiveDefensesAreReevaluatedWithoutTheItemBeingSold_WithoutMutatingState()
    {
        var state = State(oldId: 9102, enemy: "Lux");
        var live = new ChampionStats { MaxHealth = 2000, Armor = 150, MagicResist = 50,
            AbilityPower = 650, CritChance = 0.25 };
        state.ActivePlayer!.ChampionStats = live;
        var plan = new ItemAdvisor(Catalog()).Advise(state)!;
        Assert.Equal(9101, plan.Top!.Item.Id);
        Assert.Same(live, state.ActivePlayer.ChampionStats);
        Assert.Equal(150, live.Armor);
        Assert.Equal(650, live.AbilityPower);
        Assert.Equal(9102, state.AllPlayers[0].Items[0].ItemID);
    }

    [Theory]
    [InlineData("Grievous Wounds", "Soraka")]
    [InlineData("Removes all crowd control debuffs", "Malzahar")]
    public void DoesNotSellTheOnlyRequiredCounter(string description, string enemy)
    {
        var state = State();
        state.AllPlayers.Add(new Player { ChampionName = enemy, Team = "CHAOS" });
        var catalog = Catalog(i => i["9101"]!["description"] = description);
        Assert.Null(new ItemAdvisor(catalog).Advise(state)!.Top);
    }

    [Fact]
    public void RespectsMapAndBuildOverride()
    {
        var catalog = Catalog(i => i["9102"]!["maps"]!["12"] = false);
        var state = State();
        state.GameData.MapNumber = 12;
        state.GameData.GameMode = "ARAM";
        Assert.Null(new ItemAdvisor(catalog).Advise(state)!.Top);
        var adOnly = Catalog(i =>
        {
            i["9102"]!["tags"] = new JsonArray("Damage", "Armor");
            i["9102"]!["stats"] = new JsonObject { ["FlatPhysicalDamageMod"] = 90, ["FlatArmorMod"] = 55 };
        });
        Assert.Null(new ItemAdvisor(adOnly).Advise(State(), BuildArchetype.Mage)!.Top);
    }

    [Fact]
    public void AramShopAndAdviceFeed_DescribeTheSameSwap_UsingOnlyCurrentGold()
    {
        var state = State(100);
        state.GameData.GameMode = "ARAM";
        state.GameData.MapNumber = 12;
        state.AllPlayers[0].IsDead = true;
        state.AllPlayers[0].RespawnTimer = 300;
        var catalog = Catalog();
        var advisor = new ItemAdvisor(catalog);
        var plan = advisor.Advise(state)!;
        Assert.False(plan.Top!.Affordable);
        Assert.Contains("Keep Magic Ward", plan.ShopAlert);
        var advice = Assert.Single(new ItemRecommendationRule(advisor).Evaluate(state));
        Assert.Contains("save 800g more", advice.Message);
        Assert.DoesNotContain("Next item:", advice.Message);
        state.ActivePlayer!.CurrentGold = 900;
        Assert.Contains("sell Magic Ward, buy Armor Ward", advisor.Advise(state)!.ShopAlert);
    }
}
