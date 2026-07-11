using ParadoxLoLCompanion.Core.Advice.Rules;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

/// <summary>El adaptador que publica el plan del asesor en el feed de consejos.</summary>
public class ItemRecommendationTests
{
    private static readonly int[] None = new int[0];

    [Fact]
    public void NotLoadedCatalog_EmitsNothing()
    {
        var rule = new ItemRecommendationRule(DataDragonCatalog.Empty);
        var state = TestCatalog.State(5000, ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 5, None));
        Assert.Empty(rule.Evaluate(state));
    }

    [Fact]
    public void EmitsAntiHeal_WhenStrongEnemyHeals()
    {
        var rule = new ItemRecommendationRule(TestCatalog.Catalog());
        var state = TestCatalog.State(5000, ("Ahri", "ORDER", 0, None), ("Warwick", "CHAOS", 8, None));

        var advice = rule.Evaluate(state).ToList();
        var antiheal = Assert.Single(advice, a => a.Key == "item-antiheal");
        Assert.Contains("Morellonomicon", antiheal.Message);
    }

    [Fact]
    public void EmitsNextItem_WithComponentStep_WhenGoldIsShort()
    {
        var rule = new ItemRecommendationRule(TestCatalog.Catalog());
        var state = TestCatalog.State(1400, ("Jinx", "ORDER", 0, None), ("Soraka", "CHAOS", 0, None));

        var advice = rule.Evaluate(state).ToList();
        var next = Assert.Single(advice, a => a.Key == "item-next");
        Assert.Contains("Buy now:", next.Message);
    }

    [Fact]
    public void EmitsBoots_WhenNotFinished()
    {
        var rule = new ItemRecommendationRule(TestCatalog.Catalog());
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, None),
            ("Malzahar", "CHAOS", 0, None),
            ("Leona", "CHAOS", 0, None),
            ("Amumu", "CHAOS", 0, None));

        var advice = rule.Evaluate(state).ToList();
        var boots = Assert.Single(advice, a => a.Key == "item-boots");
        Assert.Contains("Mercury's Treads", boots.Message);
    }

    [Fact]
    public void FollowsForcedArchetype_FromProvider()
    {
        // El feed debe decir lo MISMO que el panel cuando el jugador fuerza la build:
        // el próximo item del feed es el top del plan con el override aplicado.
        var state = TestCatalog.State(5000, ("Soraka", "ORDER", 0, None), ("Jinx", "CHAOS", 2, None));
        var expected = new ItemAdvisor(TestCatalog.Catalog())
            .Advise(state, BuildArchetype.Mage)!.Top!.Item.Name;

        var rule = new ItemRecommendationRule(TestCatalog.Catalog(),
            forcedArchetype: () => BuildArchetype.Mage);
        var next = rule.Evaluate(state).First(a => a.Key == "item-next");
        Assert.Contains(expected, next.Message);
    }

    [Fact]
    public void NamesTheFedEnemy_InReasons()
    {
        var rule = new ItemRecommendationRule(TestCatalog.Catalog());
        var state = TestCatalog.State(5000,
            ("Ahri", "ORDER", 0, None),
            ("Warwick", "CHAOS", 10, None),
            ("Aatrox", "CHAOS", 0, None));

        var antiheal = rule.Evaluate(state).First(a => a.Key == "item-antiheal");
        Assert.Contains("Warwick", antiheal.Message);
    }

    [Fact]
    public void Feed_UsesOpggStats_MetaBootsRuleInFeed()
    {
        // Amenaza que pide Mercs (Malzahar+Leona+Amumu) pero op.gg dice Sorcerer's:
        // el feed debe decir lo mismo que el panel — la meta manda. Sin el statsProvider,
        // el feed llamaba a Advise sin stats y la regla de botas meta no regía en el feed.
        var state = TestCatalog.State(2000,
            ("Jinx", "ORDER", 0, None),
            ("Malzahar", "CHAOS", 0, None),
            ("Leona", "CHAOS", 0, None),
            ("Amumu", "CHAOS", 0, None));
        var stats = new ChampionBuildStats
        {
            ChampionKey = "Jinx",
            GameMode = "ranked",
            Position = "adc",
            Boots = new ItemSetStats(new[] { 3020 }, PickRate: 0.62, Play: 9000, Win: 4700),
        };
        var rule = new ItemRecommendationRule(TestCatalog.Catalog(),
            statsProvider: () => stats);

        var advice = rule.Evaluate(state).ToList();

        var boots = advice.Single(a => a.Key == "item-boots");
        Assert.Contains("Sorcerer's Shoes", boots.Message);
        Assert.Contains("pick rate", boots.Message);
    }
}
