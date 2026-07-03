using LoLAdvisor.Core.Items;

namespace LoLAdvisor.Tests;

public class ThreatAnalyzerTests
{
    private static ThreatAnalyzer Analyzer() => new(TestCatalog.Catalog());

    [Fact]
    public void NoEnemies_ReturnsNone()
    {
        var state = TestCatalog.State(0, ("Ahri", "ORDER", 0, new int[0]));
        var threat = Analyzer().Analyze(state);
        Assert.False(threat.HasEnemies);
    }

    [Fact]
    public void FedPhysicalCarry_DominatesDamageSplit()
    {
        // Jinx 10/0 vs Malzahar 0/0: el split debe reflejar quién está fed, no el draft.
        var state = TestCatalog.State(0,
            ("Ahri", "ORDER", 0, new int[0]),
            ("Jinx", "CHAOS", 10, new int[0]),
            ("Malzahar", "CHAOS", 0, new int[0]));

        var threat = Analyzer().Analyze(state);
        Assert.True(threat.PhysicalShare > 0.8, $"share físico fue {threat.PhysicalShare}");
        Assert.Contains("Jinx", threat.TopThreatName);
        Assert.Contains("Jinx", threat.TopPhysicalName);
        Assert.Contains("Malzahar", threat.TopMagicalName);
    }

    [Fact]
    public void EvenGame_HasNoBiggestThreat()
    {
        // Arranque de partida: todos 0/0/0 nivel 1 — nombrar a alguien como "mayor
        // amenaza" es ruido inventado. Solo aparece cuando alguien destaca de verdad.
        var state = TestCatalog.State(0,
            ("Ahri", "ORDER", 0, new int[0]),
            ("Jinx", "CHAOS", 0, new int[0]),
            ("Leona", "CHAOS", 0, new int[0]));

        Assert.Null(Analyzer().Analyze(state).TopThreatName);
    }

    [Fact]
    public void SharesSumToOne()
    {
        var state = TestCatalog.State(0,
            ("Ahri", "ORDER", 0, new int[0]),
            ("Jinx", "CHAOS", 2, new int[0]),
            ("Leona", "CHAOS", 1, new int[0]),
            ("Amumu", "CHAOS", 3, new int[0]));

        var threat = Analyzer().Analyze(state);
        Assert.Equal(1.0, threat.PhysicalShare + threat.MagicalShare, precision: 6);
    }

    [Fact]
    public void HealerChampion_TriggersSustain()
    {
        var state = TestCatalog.State(0,
            ("Ahri", "ORDER", 0, new int[0]),
            ("Warwick", "CHAOS", 5, new int[0]));

        var threat = Analyzer().Analyze(state);
        Assert.True(threat.SustainScore > 0.9);
        Assert.Contains("Warwick", threat.TopSustainName);
    }

    [Fact]
    public void SustainItems_TriggerSustain_EvenWithoutHealerChampion()
    {
        // Jinx con Cetro vampírico (LifeSteal).
        var state = TestCatalog.State(0,
            ("Ahri", "ORDER", 0, new int[0]),
            ("Jinx", "CHAOS", 3, new[] { 1053 }));

        var threat = Analyzer().Analyze(state);
        Assert.True(threat.SustainScore > 0.9);
    }

    [Fact]
    public void EnemyItems_SumBonusResists()
    {
        var state = TestCatalog.State(0,
            ("Ahri", "ORDER", 0, new int[0]),
            ("Leona", "CHAOS", 0, new[] { 3075, 1029 }),   // 70 + 40 armadura
            ("Amumu", "CHAOS", 0, new[] { 3068, 3065 }));  // 50 armadura, 50 RM

        var threat = Analyzer().Analyze(state);
        Assert.Equal(160, threat.EnemyBonusArmor, precision: 1);
        Assert.Equal(50, threat.EnemyBonusMr, precision: 1);
    }

    [Fact]
    public void FedAssassin_RaisesBurstScore()
    {
        var state = TestCatalog.State(0,
            ("Ahri", "ORDER", 0, new int[0]),
            ("Zed", "CHAOS", 10, new int[0]),
            ("Leona", "CHAOS", 0, new int[0]),
            ("Soraka", "CHAOS", 0, new int[0]));

        var threat = Analyzer().Analyze(state);
        Assert.True(threat.BurstScore >= 1.6, $"burst fue {threat.BurstScore}");
        Assert.Equal(DamageProfile.Physical, threat.BurstDamage);
        Assert.Contains("Zed", threat.TopBurstName);
    }

    [Fact]
    public void DetectsSuppression_HeavyCc_AndShields()
    {
        var state = TestCatalog.State(0,
            ("Jinx", "ORDER", 0, new int[0]),
            ("Malzahar", "CHAOS", 0, new int[0]),
            ("Leona", "CHAOS", 0, new int[0]),
            ("Amumu", "CHAOS", 0, new int[0]),
            ("Karma", "CHAOS", 0, new int[0]));

        var threat = Analyzer().Analyze(state);
        Assert.True(threat.HasSuppression);
        Assert.Contains("Malzahar", threat.SuppressionName);
        Assert.Equal(3, threat.HeavyCcCount); // Malzahar, Leona, Amumu
        Assert.True(threat.HasShields);       // Karma
    }

    [Fact]
    public void MarksmanWeight_FeedsAutoAttackShare()
    {
        var state = TestCatalog.State(0,
            ("Ahri", "ORDER", 0, new int[0]),
            ("Jinx", "CHAOS", 5, new int[0]),
            ("Vayne", "CHAOS", 5, new int[0]),
            ("Amumu", "CHAOS", 0, new int[0]));

        var threat = Analyzer().Analyze(state);
        Assert.True(threat.AutoAttackShare > 0.8, $"share de autos fue {threat.AutoAttackShare}");
    }

    // --- Grados difusos (v3): perceptos continuos que reemplazan los umbrales duros ---

    [Fact]
    public void PhysicalSkew_RisesWithPhysicalShare()
    {
        var skewed = Analyzer().Analyze(TestCatalog.State(0,
            ("Ahri", "ORDER", 0, new int[0]),
            ("Jinx", "CHAOS", 10, new int[0]),
            ("Malzahar", "CHAOS", 0, new int[0])));
        var even = Analyzer().Analyze(TestCatalog.State(0,
            ("Ahri", "ORDER", 0, new int[0]),
            ("Jinx", "CHAOS", 2, new int[0]),
            ("Ahri", "CHAOS", 2, new int[0])));

        Assert.True(skewed.PhysicalSkew > 0.7, $"skew físico fue {skewed.PhysicalSkew}");
        Assert.True(even.PhysicalSkew < 0.2, $"con daño parejo el skew fue {even.PhysicalSkew}");
    }

    [Fact]
    public void SustainDegree_IsMapAware_HotterInAram()
    {
        // Mismo roster con sustain moderado (~0.23): bajo el umbral de Grieta pero sobre
        // el de ARAM. El grado difuso lo refleja (0 en Grieta, alto en ARAM).
        var players = new (string, string, int, int[])[]
        {
            ("Ahri", "ORDER", 0, new int[0]),
            ("Warwick", "CHAOS", 1, new int[0]),
            ("Zed", "CHAOS", 2, new int[0]),
            ("Jinx", "CHAOS", 2, new int[0]),
        };
        var rift = Analyzer().Analyze(TestCatalog.State(0, players));
        var aram = Analyzer().Analyze(TestCatalog.AramState(0, players));

        Assert.True(aram.Sustain > rift.Sustain,
            $"ARAM {aram.Sustain} no fue mayor que Grieta {rift.Sustain}");
        Assert.Equal(0.0, rift.Sustain, precision: 6);
    }

    [Fact]
    public void CritThreat_HighAgainstMarksman_LowAgainstBruisers()
    {
        var vsMarksman = Analyzer().Analyze(TestCatalog.State(0,
            ("Leona", "ORDER", 0, new int[0]),
            ("Jinx", "CHAOS", 5, new int[0])));
        var vsBruiser = Analyzer().Analyze(TestCatalog.State(0,
            ("Leona", "ORDER", 0, new int[0]),
            ("Aatrox", "CHAOS", 5, new int[0])));

        Assert.True(vsMarksman.CritThreat > 0.6, $"crit vs marksman fue {vsMarksman.CritThreat}");
        Assert.True(vsBruiser.CritThreat < 0.3, $"crit vs bruiser fue {vsBruiser.CritThreat}");
    }

    [Fact]
    public void EnemyAntiHeal_RisesWhenEnemyHoldsGrievousWoundsItem()
    {
        var without = Analyzer().Analyze(TestCatalog.State(0,
            ("Jinx", "ORDER", 0, new int[0]),
            ("Aatrox", "CHAOS", 3, new int[0])));
        var with = Analyzer().Analyze(TestCatalog.State(0,
            ("Jinx", "ORDER", 0, new int[0]),
            ("Aatrox", "CHAOS", 3, new[] { 3075 })));   // Thornmail = Heridas Graves

        Assert.Equal(0.0, without.EnemyAntiHeal, precision: 6);
        Assert.True(with.EnemyAntiHeal > 0.5, $"anti-heal enemigo fue {with.EnemyAntiHeal}");
    }

    [Fact]
    public void EnemyTankiness_RisesWithEnemyResistsAndHealth()
    {
        var squishy = Analyzer().Analyze(TestCatalog.State(0,
            ("Jinx", "ORDER", 0, new int[0]),
            ("Zed", "CHAOS", 3, new int[0])));
        var tanky = Analyzer().Analyze(TestCatalog.State(0,
            ("Jinx", "ORDER", 0, new int[0]),
            ("Aatrox", "CHAOS", 3, new[] { 3075, 3083 }),   // Thornmail + Warmog
            ("Leona", "CHAOS", 0, new[] { 3068, 3065 })));  // Sunfire + Spirit Visage

        Assert.True(tanky.EnemyTankiness > squishy.EnemyTankiness);
        Assert.True(tanky.EnemyTankiness > 0.5, $"tankiness fue {tanky.EnemyTankiness}");
        Assert.Equal(0.0, squishy.EnemyTankiness, precision: 6);
    }

    [Fact]
    public void PercentHpTrue_DetectsPercentHpAndTrueDamageChampions()
    {
        // Vayne (daño verdadero + % de vida) va en la lista de kit.
        var threat = Analyzer().Analyze(TestCatalog.State(0,
            ("Leona", "ORDER", 0, new int[0]),
            ("Vayne", "CHAOS", 6, new int[0])));
        Assert.True(threat.PercentHpTrue > 0.5, $"%HP/true fue {threat.PercentHpTrue}");
    }

    [Fact]
    public void HardEngage_DetectsEngageChampions()
    {
        var threat = Analyzer().Analyze(TestCatalog.State(0,
            ("Jinx", "ORDER", 0, new int[0]),
            ("Leona", "CHAOS", 0, new int[0]),
            ("Amumu", "CHAOS", 0, new int[0])));
        Assert.True(threat.HardEngage > 0.5, $"hard-engage fue {threat.HardEngage}");
    }

    [Fact]
    public void AllFuzzyDegrees_StayInUnitInterval()
    {
        var threat = Analyzer().Analyze(TestCatalog.State(0,
            ("Ahri", "ORDER", 0, new int[0]),
            ("Jinx", "CHAOS", 20, new[] { 3031 }),
            ("Vayne", "CHAOS", 15, new[] { 3075 }),
            ("Leona", "CHAOS", 5, new[] { 3068, 3065, 3083 })));

        foreach (var mu in new[]
        {
            threat.PhysicalSkew, threat.MagicalSkew, threat.ArmorStack, threat.MrStack,
            threat.Sustain, threat.Burst, threat.CritThreat, threat.PercentHpTrue,
            threat.HardEngage, threat.EnemyAntiHeal, threat.EnemyTankiness,
        })
            Assert.InRange(mu, 0.0, 1.0);
    }
}
