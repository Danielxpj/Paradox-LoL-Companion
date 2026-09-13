using ParadoxLoLCompanion.Core.Advice.Rules;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

public class RecommendationCalculationRegressionTests
{
    private static ItemSetStats MetaSet(params int[] ids) => new(ids, 0.4, 10000, 5500);

    [Fact]
    public void Detected_alternative_and_manual_alternative_receive_the_same_recommendations()
    {
        var advisor = new ItemAdvisor(TestCatalog.Catalog());
        var state = TestCatalog.State(3000,
            ("Aatrox", "ORDER", 0, new[] { 3075, 3068 }),
            ("Jinx", "CHAOS", 0, Array.Empty<int>()));
        var stats = new ChampionBuildStats { CoreItems = MetaSet(3031, 3036, 3156) };
        var detected = advisor.Advise(state, stats: stats)!;
        var manual = advisor.Advise(state, BuildArchetype.Tank, stats)!;
        Assert.True(detected.MyProfile.InferredFromItems);
        Assert.Equal(BuildArchetype.Tank, detected.MyProfile.Archetype);
        Assert.Equal(manual.Recommendations.Select(r => (r.Item.Id, r.Score)),
            detected.Recommendations.Select(r => (r.Item.Id, r.Score)));
        Assert.DoesNotContain(detected.Recommendations.SelectMany(r => r.Reasons),
            r => r.Contains("core build for"));
    }

    [Theory]
    [InlineData(3, 3102, "4th")]
    [InlineData(4, 3135, "5th")]
    [InlineData(5, 3137, "6th")]
    public void Meta_ranking_leads_with_the_next_unfilled_slot(int completed, int expected, string label)
    {
        var data = TestCatalog.Catalog();
        var stats = new ChampionBuildStats
        {
            CoreItems = MetaSet(3089, 3157, 3115),
            FourthItems = new[] { MetaSet(3102), MetaSet(3165), MetaSet(6653) },
            FifthItems = new[] { MetaSet(3135), MetaSet(3165) },
            SixthItems = new[] { MetaSet(3137) },
        };
        var owned = new[] { 3089, 3157, 3115, 3102, 3135 }.Take(completed).ToArray();
        var slots = MetaSpine.Build(data, new ItemsConfig(), stats, 11, owned.ToHashSet(), completed);
        Assert.Equal(expected, slots[0].Item.Id);
        Assert.Equal(label, slots[0].SlotLabel);
        if (completed == 4)
        {
            Assert.Equal("5th", slots.Single(s => s.Item.Id == 3165).SlotLabel);
            var state = TestCatalog.State(0, ("Ahri", "ORDER", 0, owned),
                ("Jinx", "CHAOS", 0, Array.Empty<int>()));
            var plan = new ItemAdvisor(data, new ItemsConfig { DetectBuildFromItems = false })
                .Advise(state, stats: stats)!;
            Assert.Equal(expected, plan.Top!.Item.Id);
        }
    }

    [Theory]
    [InlineData("Aatrox", BuildArchetype.Mage, "magic resist", 3065, 3102)]
    [InlineData("Ahri", BuildArchetype.Marksman, "armor", 3075, 3068)]
    public void Alternate_damage_builds_receive_resistance_counter_bonuses(
        string champion, BuildArchetype build, string resistance, int enemyItem1, int enemyItem2)
    {
        var advisor = new ItemAdvisor(TestCatalog.Catalog(), new ItemsConfig { MaxRecommendations = 30 });
        var state = TestCatalog.State(9000, (champion, "ORDER", 0, Array.Empty<int>()),
            ("Jinx", "CHAOS", 0, new[] { enemyItem1, enemyItem2 }));
        var plan = advisor.Advise(state, build)!;
        Assert.Contains(plan.Recommendations.SelectMany(r => r.Reasons),
            r => r.Contains("enemies already bought") && r.Contains(resistance));
    }

    [Fact]
    public void Respawn_income_does_not_fund_immediate_purchases_or_shop_messages()
    {
        var advisor = new ItemAdvisor(TestCatalog.Catalog());
        var state = TestCatalog.AramState(3100,
            ("Ahri", "ORDER", 0, Array.Empty<int>()), ("Zed", "CHAOS", 0, Array.Empty<int>()));
        state.AllPlayers[0].IsDead = true;
        state.AllPlayers[0].RespawnTimer = 50;
        var stats = new ChampionBuildStats { CoreItems = MetaSet(3157) };
        var plan = advisor.Advise(state, stats: stats)!;
        Assert.Equal(3157, plan.Top!.Item.Id);
        Assert.False(plan.Top.Affordable);
        Assert.Equal(150, plan.Top.MissingGold);
        Assert.NotNull(plan.Top.Purchase.NextComponent);
        Assert.True(plan.Top.Purchase.NextComponentCost <= state.ActivePlayer!.CurrentGold);
        Assert.DoesNotContain("finish", plan.ShopAlert!);

        state.ActivePlayer!.CurrentGold = 3250;
        var funded = advisor.Advise(state, stats: stats)!;
        Assert.True(funded.Top!.Affordable);
        Assert.Contains("finish", funded.ShopAlert!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shop_alert_respects_full_inventory_and_allows_component_merges(bool merges)
    {
        var state = TestCatalog.AramState(9000,
            ("Ahri", "ORDER", 0, new[] { 9001, 9002, 9003, 9004, 9005, merges ? 1058 : 9006 }),
            ("Zed", "CHAOS", 0, Array.Empty<int>()));
        for (var i = 0; i < 6; i++)
            state.AllPlayers[0].Items[i].Slot = i;
        state.AllPlayers[0].IsDead = true;
        var plan = new ItemAdvisor(TestCatalog.Catalog()).Advise(state,
            stats: new ChampionBuildStats { CoreItems = MetaSet(3157) })!;
        Assert.Equal(!merges, plan.Top!.BlockedByFullInventory);
        if (merges)
            Assert.Contains("finish", plan.ShopAlert!);
        else
            Assert.Null(plan.ShopAlert);
    }

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
