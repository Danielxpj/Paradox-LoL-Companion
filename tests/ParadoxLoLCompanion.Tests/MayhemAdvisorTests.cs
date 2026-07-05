using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Mayhem;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Tests;

public class MayhemAdvisorTests
{
    private static readonly int[] None = new int[0];

    private static MayhemAdvisor Advisor() => new(TestCatalog.Catalog());

    private static GameState State(int level, bool dead = false, double respawn = 0,
        params (string Champ, string Team, int Kills, int[] ItemIds)[] enemies)
    {
        var players = new[] { ("Jinx", "ORDER", 0, None) }.Concat(enemies).ToArray();
        var state = TestCatalog.AramState(1000, players);
        state.ActivePlayer!.Level = level;
        state.AllPlayers[0].Level = level;
        state.AllPlayers[0].IsDead = dead;
        state.AllPlayers[0].RespawnTimer = respawn;
        return state;
    }

    [Theory]
    [InlineData(1, 1, 7)]
    [InlineData(6, 1, 7)]
    [InlineData(7, 2, 11)]
    [InlineData(11, 3, 15)]
    [InlineData(14, 3, 15)]
    [InlineData(15, 4, null)]
    [InlineData(18, 4, null)]
    public void UnlockedPicks_FollowLevelMilestones(int level, int expectedPicks, int? expectedNext)
    {
        var advice = Advisor().Advise(State(level, enemies: ("Ahri", "CHAOS", 0, None)))!;

        Assert.Equal(expectedPicks, advice.UnlockedPicks);
        Assert.Equal(4, advice.TotalPicks);
        Assert.Equal(expectedNext, advice.NextPickLevel);
    }

    [Fact]
    public void Dead_OpensThePickWindow()
    {
        var advice = Advisor().Advise(State(11, dead: true, respawn: 12,
            ("Ahri", "CHAOS", 0, None)))!;

        Assert.True(advice.PickWindowNow);
        Assert.NotNull(advice.PickNowLine);
        Assert.Contains("dead", advice.PickNowLine);
        Assert.Contains("12s", advice.PickNowLine);
    }

    [Fact]
    public void Alive_NoPickWindow()
    {
        var advice = Advisor().Advise(State(11, enemies: ("Ahri", "CHAOS", 0, None)))!;

        Assert.False(advice.PickWindowNow);
        Assert.Null(advice.PickNowLine);
    }

    [Fact]
    public void Guidance_MatchesMyArchetype()
    {
        // Yo Jinx (tiradora): la guía debe hablar de daño sostenido.
        var advice = Advisor().Advise(State(7, enemies: ("Ahri", "CHAOS", 0, None)))!;

        Assert.Contains(advice.Guidance, g => g.Contains("marksman"));
    }

    [Fact]
    public void Guidance_FollowsForcedArchetype()
    {
        // Jinx (tiradora) forzada a maga: la guía de augments habla de AP, no de daño sostenido.
        var advice = Advisor().Advise(State(7, enemies: ("Ahri", "CHAOS", 0, None)),
            BuildArchetype.Mage)!;

        Assert.Contains(advice.Guidance, g => g.Contains("mage"));
        Assert.DoesNotContain(advice.Guidance, g => g.Contains("marksman"));
    }

    [Fact]
    public void Guidance_WarnsAboutSkewedEnemyDamage()
    {
        var advice = Advisor().Advise(State(7,
            enemies: new[] { ("Zed", "CHAOS", 3, None), ("Vayne", "CHAOS", 3, None) }))!;

        Assert.Contains(advice.Guidance, g => g.Contains("physical"));
    }

    [Fact]
    public void Guidance_SuggestsAntiHeal_AgainstHealers()
    {
        var advice = Advisor().Advise(State(7, enemies: ("Warwick", "CHAOS", 5, None)))!;

        Assert.Contains(advice.Guidance, g => g.Contains("anti-heal"));
    }

    [Fact]
    public void Guidance_SuggestsSurvival_AgainstFedBurst()
    {
        var advice = Advisor().Advise(State(7, enemies: new[]
        {
            ("Zed", "CHAOS", 10, None),
            ("Soraka", "CHAOS", 0, None),
            ("Leona", "CHAOS", 0, None),
        }))!;

        Assert.Contains(advice.Guidance, g => g.Contains("survival"));
    }

    [Fact]
    public void NoActivePlayer_ReturnsNull()
    {
        var state = new GameState();
        Assert.Null(Advisor().Advise(state));
    }
}
