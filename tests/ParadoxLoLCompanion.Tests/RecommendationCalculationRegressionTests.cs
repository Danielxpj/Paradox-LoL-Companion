using ParadoxLoLCompanion.Core.Advice.Rules;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

public class RecommendationCalculationRegressionTests
{
    [Fact]
    public void Alternate_build_ignores_standard_meta_but_standard_override_preserves_it()
    {
        var advisor = new ItemAdvisor(TestCatalog.Catalog());
        var state = TestCatalog.State(3000,
            ("Ahri", "ORDER", 0, Array.Empty<int>()),
            ("Jinx", "CHAOS", 0, Array.Empty<int>()));
        var stats = new ChampionBuildStats
        {
            ChampionKey = "Ahri",
            CoreItems = new ItemSetStats(new[] { 3089 }, 0.4, 10000, 5500),
            Boots = new ItemSetStats(new[] { 3020 }, 0.4, 10000, 5500),
        };

        var tank = advisor.Advise(state, BuildArchetype.Tank, stats)!;
        var withoutMeta = advisor.Advise(state, BuildArchetype.Tank)!;
        Assert.Equal(withoutMeta.Recommendations.Select(r => (r.Item.Id, r.Score)),
            tank.Recommendations.Select(r => (r.Item.Id, r.Score)));
        Assert.Equal(withoutMeta.Boots, tank.Boots);
        Assert.DoesNotContain(tank.Recommendations.SelectMany(r => r.Reasons),
            r => r.Contains("core build for"));

        var standard = advisor.Advise(state, BuildArchetype.Mage, stats)!;
        Assert.Equal(3089, standard.Top!.Item.Id);
        Assert.Contains(standard.Top.Reasons, r => r.Contains("core build for"));
    }

    private static DataDragonCatalog Catalog(double crit = 0) => DataDragonCatalog.FromJson(
        "16.13.1",
        """
        {"data":{"TestChamp":{"id":"TestChamp","key":"999","name":"Test Champ",
          "partype":"Mana","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9}},
          "Enemy":{"id":"Enemy","key":"998","name":"Enemy",
          "tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9}}}}
        """,
        $$$$$"""
        {"data":{
          "100":{"name":"Owned component","gold":{"total":100,"purchasable":true},"into":["4101","4102"]},
          "200":{"name":"New component","gold":{"total":200,"purchasable":true},"into":["4101","4102"]},
          "4101":{"name":"Crit item","gold":{"total":3000,"purchasable":true},
            "tags":["Damage","CriticalStrike"],"maps":{"11":true},"from":["100","200"],
            "stats":{"FlatPhysicalDamageMod":55,"FlatCritChanceMod":{{{{{crit.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}}},
          "4102":{"name":"Plain item","gold":{"total":3000,"purchasable":true},
            "tags":["Damage"],"maps":{"11":true},"from":["100","200"],
            "stats":{"FlatPhysicalDamageMod":55}}}}
        """);

    [Theory]
    [InlineData(0.1, 0)]
    [InlineData(0.25, 2)]
    [InlineData(0.5, 2)]
    public void Capped_crit_has_no_value_regardless_of_magnitude_or_passive_bonus(double crit, double passive)
    {
        var config = new ItemsConfig
        {
            ItemPassiveValue = new() { [4101] = passive, [4102] = passive },
        };
        var state = ItemAdvisorStatsTests.State();
        state.ActivePlayer!.ChampionStats.CritChance = 100;
        var advisor = new ItemAdvisor(Catalog(crit), config);
        var capped = advisor.Advise(state, BuildArchetype.Marksman)!;
        Assert.Equal(capped.Recommendations.Single(r => r.Item.Id == 4102).Score,
            capped.Recommendations.Single(r => r.Item.Id == 4101).Score, 10);

        state.ActivePlayer.ChampionStats.CritChance = 0;
        var uncapped = advisor.Advise(state, BuildArchetype.Marksman)!;
        Assert.True(uncapped.Recommendations.Single(r => r.Item.Id == 4101).Score
            > uncapped.Recommendations.Single(r => r.Item.Id == 4102).Score);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(2900, false)]
    public void Full_inventory_checks_the_immediate_purchase(double gold, bool blocked)
    {
        var state = ItemAdvisorStatsTests.State();
        state.ActivePlayer!.CurrentGold = gold;
        state.AllPlayers[0].Items.AddRange(Enumerable.Range(0, 6)
            .Select(slot => new Item { Slot = slot, ItemID = slot == 0 ? 100 : 9000 + slot, Count = 1 }));
        var advisor = new ItemAdvisor(Catalog());
        var plan = advisor.Advise(state, BuildArchetype.Marksman)!;
        Assert.NotEmpty(plan.Recommendations);
        Assert.All(plan.Recommendations, r =>
        {
            Assert.Equal(blocked, r.BlockedByFullInventory);
            Assert.Equal(blocked ? 200 : r.Item.Id, r.Purchase.NextComponent!.Id);
        });
        var feed = new ItemRecommendationRule(advisor, () => BuildArchetype.Marksman)
            .Evaluate(state).ToList();
        if (blocked)
        {
            Assert.DoesNotContain(feed, a => a.Message.Contains("Buy now:"));
            Assert.Contains(feed, a => a.Key == "item-next" && a.Message.Contains("Inventory full"));
        }
    }
}
