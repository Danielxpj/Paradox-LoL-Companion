using LoLAdvisor.Core.Advice;
using LoLAdvisor.Core.Advice.Rules;
using LoLAdvisor.Core.Live;
using LoLAdvisor.Core.Models;

namespace LoLAdvisor.Tests;

public class AdviceTests
{
    private static GameState Fixture() => LiveGameParser.Parse(Fixtures.AllGameData());

    private static GameState StateWith(
        double gold = 0, int creepScore = 0, double gameTimeSeconds = 0, string gameMode = "CLASSIC")
    {
        var state = new GameState
        {
            ActivePlayer = new ActivePlayer { SummonerName = "Me", CurrentGold = gold },
            GameData = new GameData { GameTime = gameTimeSeconds, GameMode = gameMode },
        };
        state.AllPlayers.Add(new Player
        {
            SummonerName = "Me",
            ChampionName = "Test",
            Scores = new PlayerScores { CreepScore = creepScore },
        });
        return state;
    }

    // --- GoldForItemRule ---

    [Fact]
    public void Gold_BelowComponent_NoAdvice()
    {
        var advice = new GoldForItemRule().Evaluate(StateWith(gold: 500)).ToList();
        Assert.Empty(advice);
    }

    [Fact]
    public void Gold_AtComponentMilestone_InfoAdvice()
    {
        var advice = new GoldForItemRule().Evaluate(StateWith(gold: 1450)).ToList();
        var item = Assert.Single(advice);
        Assert.Equal(AdviceCategory.Gold, item.Category);
        Assert.Equal(AdviceSeverity.Info, item.Severity);
        Assert.Equal("gold-milestone", item.Key);
    }

    [Fact]
    public void Gold_AtLegendaryMilestone_ImportantAdvice()
    {
        var advice = new GoldForItemRule().Evaluate(StateWith(gold: 3200)).ToList();
        var item = Assert.Single(advice);
        Assert.Equal(AdviceSeverity.Important, item.Severity);
    }

    // --- CsPerMinuteRule ---

    [Fact]
    public void Cs_BelowTarget_WarnsAfterMinTime()
    {
        // 30 CS a los 10 min = 3.0 CS/min (bajo)
        var advice = new CsPerMinuteRule().Evaluate(StateWith(creepScore: 30, gameTimeSeconds: 600)).ToList();
        var item = Assert.Single(advice);
        Assert.Equal(AdviceCategory.Farming, item.Category);
        Assert.Equal(AdviceSeverity.Warning, item.Severity);
    }

    [Fact]
    public void Cs_AboveTarget_NoAdvice()
    {
        // Fixture: 78 CS a los 10 min = 7.8 CS/min (>= 7.0)
        var advice = new CsPerMinuteRule().Evaluate(Fixture()).ToList();
        Assert.Empty(advice);
    }

    [Fact]
    public void Cs_TooEarly_NoAdvice()
    {
        var advice = new CsPerMinuteRule().Evaluate(StateWith(creepScore: 0, gameTimeSeconds: 30)).ToList();
        Assert.Empty(advice);
    }

    [Fact]
    public void Cs_SkippedInAram()
    {
        // En ARAM el farmeo es compartido: la referencia de CS/min no aplica.
        var advice = new CsPerMinuteRule()
            .Evaluate(StateWith(creepScore: 10, gameTimeSeconds: 600, gameMode: "ARAM")).ToList();
        Assert.Empty(advice);
    }

    // --- ObjectiveTimerRule ---

    [Fact]
    public void Objective_Dragon_ImportantWhenSpawningSoon()
    {
        // Fixture: último dragón a 330s, respawn 300s => 630s; ahora 600s => faltan 30s.
        var advice = new ObjectiveTimerRule().Evaluate(Fixture()).ToList();
        var dragon = Assert.Single(advice, a => a.Key == "obj-dragon");
        Assert.Equal(AdviceSeverity.Important, dragon.Severity);
    }

    [Fact]
    public void Objective_Baron_InfoBeforeFirstSpawn()
    {
        var advice = new ObjectiveTimerRule().Evaluate(Fixture()).ToList();
        var baron = Assert.Single(advice, a => a.Key == "obj-baron");
        Assert.Equal(AdviceSeverity.Info, baron.Severity);
    }

    [Fact]
    public void Objectives_SkippedInAram()
    {
        // Dragón/Barón no existen en el Abismo: sin timers fuera de CLASSIC.
        var advice = new ObjectiveTimerRule()
            .Evaluate(StateWith(gameTimeSeconds: 600, gameMode: "ARAM")).ToList();
        Assert.Empty(advice);
    }

    // --- AdviceEngine ---

    [Fact]
    public void Engine_Default_AggregatesAcrossRules()
    {
        var advice = AdviceEngine.CreateDefault().Evaluate(Fixture());
        var keys = advice.Select(a => a.Key).ToList();
        Assert.Contains("gold-milestone", keys);
        Assert.Contains("obj-dragon", keys);
        Assert.Contains("obj-baron", keys);
    }

    [Fact]
    public void Engine_DedupesByKey()
    {
        // Dos reglas que emiten la misma Key: solo debe quedar una.
        var engine = new AdviceEngine(new IAdviceRule[] { new GoldForItemRule(), new GoldForItemRule() });
        var advice = engine.Evaluate(StateWith(gold: 1450));
        Assert.Single(advice);
    }
}
