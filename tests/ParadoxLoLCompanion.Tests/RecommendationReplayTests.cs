using ParadoxLoLCompanion.Core.Advice.Rules;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

public class RecommendationReplayTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Crit_upgrade_removes_only_the_consumed_cloaks_contribution(int copies)
    {
        var config = new ItemsConfig { MaxRecommendations = 30, AffordabilityBonus = 0 };
        var advisor = new ItemAdvisor(TestCatalog.Catalog(), config);
        var upgrading = TestCatalog.State(0,
            ("Jinx", "ORDER", 0, Enumerable.Repeat(1018, copies).ToArray()),
            ("Jinx", "CHAOS", 0, Array.Empty<int>()));
        upgrading.ActivePlayer!.ChampionStats.CritChance = 0.9;
        var reference = TestCatalog.State(0, ("Jinx", "ORDER", 0, Array.Empty<int>()),
            ("Jinx", "CHAOS", 0, Array.Empty<int>()));
        reference.ActivePlayer!.ChampionStats.CritChance = 0.75;
        var upgrade = advisor.Advise(upgrading, BuildArchetype.Marksman)!.Recommendations.Single(r => r.Item.Id == 3031);
        var direct = advisor.Advise(reference, BuildArchetype.Marksman)!.Recommendations.Single(r => r.Item.Id == 3031);
        Assert.Equal(direct.ScoreBreakdown!.Core, upgrade.ScoreBreakdown!.Core, 10);
        Assert.Single(BuildPathPlanner.ConsumedComponents(TestCatalog.Catalog(), upgrade.Item,
            Enumerable.Repeat(1018, copies)));
    }

    [Theory]
    [InlineData(0, false, "save 800g more")]
    [InlineData(799.5, false, "save 1g more")]
    [InlineData(800, false, "buy the 800g piece now")]
    [InlineData(800, true, "inventory full")]
    public void Urgent_antiheal_describes_an_action_the_inventory_and_gold_allow(
        double gold, bool full, string expected)
    {
        var state = TestCatalog.State(gold,
            ("Soraka", "ORDER", 0, full ? new[] { 9001, 9002, 9003, 9004, 9005, 9006 } : Array.Empty<int>()),
            ("Warwick", "CHAOS", 0, Array.Empty<int>()), ("Ahri", "CHAOS", 0, Array.Empty<int>()),
            ("Ekko", "CHAOS", 0, Array.Empty<int>()), ("Elise", "CHAOS", 0, Array.Empty<int>()));
        for (var i = 0; i < state.AllPlayers[0].Items.Count; i++)
            state.AllPlayers[0].Items[i].Slot = i;
        var advisor = new ItemAdvisor(TestCatalog.Catalog(), new ItemsConfig { MaxRecommendations = 1 });
        var plan = advisor.Advise(state, BuildArchetype.Enchanter)!;
        Assert.NotNull(plan.UrgentPickup);
        Assert.Contains(expected, plan.UrgentPickup.Reason);
        var feed = new ItemRecommendationRule(advisor, () => BuildArchetype.Enchanter).Evaluate(state);
        Assert.Contains(feed, a => a.Key == "item-antiheal" && a.Message.Contains(expected));
        if (full || gold < 800)
            Assert.DoesNotContain("now", plan.UrgentPickup.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replay_evaluates_severe_healing_gate_without_changing_low_healing_rankings(bool severe)
    {
        var catalog = TestCatalog.Catalog();
        var state = TestCatalog.State(0, ("Ahri", "ORDER", 0, Array.Empty<int>()),
            (severe ? "Soraka" : "Jinx", "CHAOS", 0, Array.Empty<int>()));
        var stats = new ChampionBuildStats
        {
            CoreItems = new ItemSetStats(new[] { 3089, 3157, 3115 }, 0.4, 10000, 5500),
        };
        var oldConfig = new ItemsConfig { SevereAntiHealCounterGate = 4 };
        var baseline = new ItemAdvisor(catalog, oldConfig).Advise(state, stats: stats)!;
        var snapshot = new RecommendationSnapshot(state,
            new RecommendationReplayContext(catalog.Version, oldConfig, null, stats, null),
            baseline.Recommendations.Select(r => new RecordedRecommendation(r.Item.Id, r.Score, r.ScoreBreakdown)).ToList());
        var result = RecommendationReplay.Evaluate(snapshot, catalog, new ItemsConfig());
        if (severe)
        {
            Assert.DoesNotContain(baseline.Recommendations, r => r.Item.Id == 3165);
            Assert.Contains(result.Plan!.Recommendations, r => r.Item.Id == 3165);
            Assert.True(result.RankingChanged);
            Assert.Equal(baseline.Top!.Item.Id, result.Plan.Top!.Item.Id);
        }
        else
        {
            Assert.False(result.RankingChanged);
            Assert.Equal(0, result.MaxSharedItemScoreDelta);
        }
    }

    [Fact]
    public void Journal_roundtrip_preserves_decision_inputs_and_reproduces_scores()
    {
        var dir = Path.Combine(Path.GetTempPath(), "paradox-replay-" + Guid.NewGuid());
        try
        {
            var data = TestCatalog.Catalog();
            var config = new ItemsConfig { HysteresisBonus = 0.8 };
            var state = TestCatalog.State(1700, ("Ahri", "ORDER", 2, new[] { 1058 }),
                ("Jinx", "CHAOS", 5, new[] { 3031, 3065 }));
            state.ActivePlayer!.ChampionStats.AbilityPower = 200;
            state.ActivePlayer.ChampionStats.MaxHealth = 2000;
            var stats = new ChampionBuildStats { CoreItems = new ItemSetStats(new[] { 3157, 3089 }, 0.3, 8000, 4500) };
            var context = new RecommendationReplayContext(data.Version, config, BuildArchetype.Mage, stats, new[] { 3157 });
            var plan = new ItemAdvisor(data, config).Advise(state, context.ForcedArchetype, stats, context.PreviousTopIds)!;
            var journal = new GameJournal(dir);
            journal.Record(state, plan, context);
            // State is mutable between ticks; saved snapshots must remain unchanged.
            state.AllPlayers[1].Items.Clear();
            state.ActivePlayer.ChampionStats.AbilityPower = 0;
            var snapshot = Assert.Single(RecommendationReplay.Read(journal.CurrentFile!));
            Assert.Equal(2, snapshot.State.AllPlayers[1].Items.Count);
            Assert.Equal(200, snapshot.State.ActivePlayer!.ChampionStats.AbilityPower);
            var replay = RecommendationReplay.Evaluate(snapshot, data);
            Assert.False(replay.RankingChanged);
            Assert.Equal(0, replay.MaxSharedItemScoreDelta, 10);
            Assert.All(snapshot.Recommendations, r => Assert.Equal(r.Score, r.Breakdown!.Total, 10));
            Assert.Throws<InvalidOperationException>(() => RecommendationReplay.Evaluate(
                snapshot with { Context = context with { CatalogVersion = "wrong-patch" } }, data));
            File.AppendAllText(journal.CurrentFile!, "{\"kind\":\"tick\",\"top\":[1]}\n");
            Assert.Single(RecommendationReplay.Read(journal.CurrentFile!));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
