using ParadoxLoLCompanion.Core.Augments;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Mayhem;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Tests;

public class MayhemAdvisorTests
{
    private static readonly int[] None = new int[0];

    private static MayhemAdvisor Advisor() => new(TestCatalog.Catalog());

    private static GameState State(int level, bool dead = false, double respawn = 0,
        double gameTime = 600,
        params (string Champ, string Team, int Kills, int[] ItemIds)[] enemies)
    {
        var players = new[] { ("Jinx", "ORDER", 0, None) }.Concat(enemies).ToArray();
        var state = TestCatalog.AramState(1000, players);
        state.GameData.GameTime = gameTime;
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
            enemies: ("Ahri", "CHAOS", 0, None)))!;

        Assert.True(advice.PickWindowNow);
        Assert.NotNull(advice.PickNowLine);
        Assert.Contains("dead", advice.PickNowLine);
        Assert.Contains("12s", advice.PickNowLine);
    }

    [Fact]
    public void Alive_MidGame_NoPickWindow()
    {
        var advice = Advisor().Advise(State(11, gameTime: 600,
            enemies: ("Ahri", "CHAOS", 0, None)))!;

        Assert.False(advice.PickWindowNow);
        Assert.Null(advice.PickNowLine);
    }

    [Fact]
    public void GameStart_AliveAtSpawn_OpensThePickWindow()
    {
        // El PRIMER pick de augment se hace en el spawn inicial, VIVO: el bug
        // real (2026-07-18) era que la ventana solo abría al morir y en el
        // arranque no se mostraba ninguna recomendación.
        var advice = Advisor().Advise(State(1, gameTime: 20,
            enemies: ("Ahri", "CHAOS", 0, None)))!;

        Assert.True(advice.PickWindowNow);
        Assert.NotNull(advice.PickNowLine);
        Assert.Contains("first augment", advice.PickNowLine);
    }

    [Fact]
    public void GameStart_ButDead_KeepsDeathMessage()
    {
        var advice = Advisor().Advise(State(1, dead: true, respawn: 5, gameTime: 60,
            enemies: ("Ahri", "CHAOS", 0, None)))!;

        Assert.True(advice.PickWindowNow);
        Assert.Contains("dead", advice.PickNowLine);
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

    private static AugmentTierList TierList() => new()
    {
        Augments = new[]
        {
            new AugmentInfo { Id = 1, Name = "Eureka", Rarity = AugmentRarity.Prismatic, Tier = 1 },
            new AugmentInfo { Id = 2, Name = "Goliath", Rarity = AugmentRarity.Prismatic, Tier = 2 },
            new AugmentInfo { Id = 3, Name = "Dual Wield", Rarity = AugmentRarity.Prismatic, Tier = 2,
                TopChampions = new[] { "Jinx" } },
            new AugmentInfo { Id = 4, Name = "Bad One", Rarity = AugmentRarity.Prismatic, Tier = 5 },
            new AugmentInfo { Id = 5, Name = "Deft", Rarity = AugmentRarity.Gold, Tier = 1 },
            new AugmentInfo { Id = 6, Name = "Unranked", Rarity = AugmentRarity.Gold, Tier = null },
            new AugmentInfo { Id = 7, Name = "Witchful", Rarity = AugmentRarity.Silver, Tier = 1 },
        },
    };

    [Fact]
    public void TopAugments_RankChampionFitFirst_ThenTier()
    {
        // Jugamos Jinx: Dual Wield (tier 2 pero favorito para Jinx) va por
        // delante de Eureka (tier 1 global) dentro de los prismáticos.
        var advice = Advisor().Advise(State(7, enemies: ("Ahri", "CHAOS", 0, None)),
            augments: TierList())!;

        var prismatic = advice.TopAugments
            .Where(a => a.Rarity == AugmentRarity.Prismatic).ToList();
        Assert.Equal("Dual Wield", prismatic[0].Name);
        Assert.True(prismatic[0].FitsMyChampion);
        Assert.Equal("Eureka", prismatic[1].Name);
        Assert.DoesNotContain(advice.TopAugments, a => a.Name == "Bad One");   // tier 5 fuera
        Assert.DoesNotContain(advice.TopAugments, a => a.Name == "Unranked");  // sin tier fuera
        Assert.Contains(advice.TopAugments, a => a.Name == "Deft");
        Assert.Contains(advice.TopAugments, a => a.Name == "Witchful");
    }

    [Fact]
    public void TopAugments_PrismaticListedFirst()
    {
        var advice = Advisor().Advise(State(7, enemies: ("Ahri", "CHAOS", 0, None)),
            augments: TierList())!;

        Assert.Equal(AugmentRarity.Prismatic, advice.TopAugments[0].Rarity);
        Assert.Equal(AugmentRarity.Silver, advice.TopAugments[^1].Rarity);
    }

    [Fact]
    public void NoTierList_TopAugmentsEmpty()
    {
        var advice = Advisor().Advise(State(7, enemies: ("Ahri", "CHAOS", 0, None)))!;
        Assert.Empty(advice.TopAugments);
    }
}
