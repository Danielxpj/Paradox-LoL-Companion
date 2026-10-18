using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

/// <summary>
/// v6 del asesor: proyección de la build enemiga, espinazo reactivo, selección por
/// cobertura, reloj de partida y prior personal.
/// </summary>
public class RecommendationV6Tests
{
    private static readonly int[] None = Array.Empty<int>();

    private static ChampionBuildStats Stats(string key, params int[] core) => new()
    {
        ChampionKey = key, GameMode = "aram", Position = "none",
        CoreItems = new ItemSetStats(core, PickRate: 0.25, Play: 5000, Win: 2600),
    };

    // ---------- 1. Proyección de la build enemiga ----------

    [Fact]
    public void Projection_reads_the_enemy_meta_build_before_they_buy_it()
    {
        var analyzer = new ThreatAnalyzer(TestCatalog.Catalog());
        var state = TestCatalog.AramState(1400,
            ("Zed", "ORDER", 0, None),
            ("Leona", "CHAOS", 0, None));
        var enemyStats = new Dictionary<string, ChampionBuildStats>
        {
            ["Leona"] = Stats("Leona", 3075, 3068, 3143), // Thornmail, Sunfire, Randuin's
        };

        var blind = analyzer.Analyze(state);
        var projected = analyzer.Analyze(state, enemyStats);

        Assert.Equal(0, blind.ArmorStack);
        Assert.Equal(0, blind.ProjectedArmorStack);
        Assert.Equal(0, projected.ArmorStack);                // todavía no compró nada
        // 70 + 50·0.7 + 50·0.49 ≈ 130 de armadura proyectada sobre la rampa [75, 225].
        Assert.True(projected.ProjectedBonusArmor > 100, $"armadura proyectada = {projected.ProjectedBonusArmor}");
        Assert.True(projected.ProjectedArmorStack > 0.3, $"proyección = {projected.ProjectedArmorStack}");
        Assert.Equal("Leona", projected.ProjectedArmorNames);
    }

    [Fact]
    public void Projection_empties_as_the_enemy_buys_the_build()
    {
        var analyzer = new ThreatAnalyzer(TestCatalog.Catalog());
        var enemyStats = new Dictionary<string, ChampionBuildStats> { ["Leona"] = Stats("Leona", 3075, 3068) };
        var before = analyzer.Analyze(TestCatalog.AramState(0,
            ("Zed", "ORDER", 0, None), ("Leona", "CHAOS", 0, None)), enemyStats);
        var after = analyzer.Analyze(TestCatalog.AramState(0,
            ("Zed", "ORDER", 0, None), ("Leona", "CHAOS", 0, new[] { 3075, 3068 })), enemyStats);

        Assert.True(before.ProjectedBonusArmor > 0);
        Assert.Equal(0, after.ProjectedBonusArmor);
        Assert.True(after.ArmorStack > 0);   // ahora manda lo comprado
    }

    [Fact]
    public void Projected_armor_pre_values_penetration_with_a_readable_reason()
    {
        var advisor = new ItemAdvisor(TestCatalog.Catalog(),
            new ItemsConfig { MaxRecommendations = 30, AffordabilityBonus = 0, AramAffordabilityBonus = 0 });
        var state = TestCatalog.AramState(9000,
            ("Zed", "ORDER", 0, None),
            ("Leona", "CHAOS", 0, None),
            ("Amumu", "CHAOS", 0, None));
        var enemyStats = new Dictionary<string, ChampionBuildStats>
        {
            ["Leona"] = Stats("Leona", 3075, 3068, 3143),
            ["Amumu"] = Stats("Amumu", 3068, 3075),
        };

        var blind = advisor.Advise(state)!.Recommendations.Single(r => r.Item.Id == 3036); // Lord Dominik's
        var projected = advisor.Advise(state, enemyStats: enemyStats)!.Recommendations.Single(r => r.Item.Id == 3036);

        Assert.True(projected.ScoreBreakdown!.Offense > blind.ScoreBreakdown!.Offense);
        Assert.Contains(projected.Reasons, r => r.Contains("will stack armor"));
        Assert.DoesNotContain(blind.Reasons, r => r.Contains("will stack armor"));
    }

    [Fact]
    public void Projected_enemy_antiheal_devalues_lifesteal_at_reduced_weight()
    {
        var advisor = new ItemAdvisor(TestCatalog.Catalog(),
            new ItemsConfig { MaxRecommendations = 30, AffordabilityBonus = 0, AramAffordabilityBonus = 0 });
        var state = TestCatalog.AramState(9000,
            ("Jinx", "ORDER", 0, None),
            ("Jinx", "CHAOS", 3, None));
        var willBuyGw = new Dictionary<string, ChampionBuildStats> { ["Jinx"] = Stats("Jinx", 3033) }; // Mortal Reminder
        var bought = TestCatalog.AramState(9000, ("Jinx", "ORDER", 0, None), ("Jinx", "CHAOS", 3, new[] { 3033 }));

        double Penalty(ItemAdvicePlan plan) => plan.Recommendations.Single(r => r.Item.Id == 3153).ScoreBreakdown!.Penalty; // BotRK
        var none = Penalty(advisor.Advise(state)!);
        var projected = Penalty(advisor.Advise(state, enemyStats: willBuyGw)!);
        var actual = Penalty(advisor.Advise(bought)!);

        Assert.True(projected > none, $"proyectado {projected} vs nada {none}");
        Assert.True(projected < actual, $"proyectado {projected} debe pesar menos que comprado {actual}");
    }

    // ---------- 2. Espinazo reactivo ----------

    [Theory]
    [InlineData("Ahri", "Malzahar", "Karma", 3065)]   // triple AP → Spirit Visage antes que Thornmail
    [InlineData("Jinx", "Vayne", "Zed", 3075)]        // triple AD → Thornmail antes que Spirit Visage
    public void Spine_reorders_its_defensive_items_by_the_enemy_damage_type(
        string e1, string e2, string e3, int expectedFirst)
    {
        var advisor = new ItemAdvisor(TestCatalog.Catalog());
        var state = TestCatalog.AramState(9000,
            ("Leona", "ORDER", 0, None),
            (e1, "CHAOS", 3, None), (e2, "CHAOS", 3, None), (e3, "CHAOS", 2, None));
        // op.gg lista Thornmail ANTES que Spirit Visage, siempre.
        var stats = Stats("Leona", 3075, 3065);

        var top = advisor.Advise(state, BuildArchetype.Tank, stats)!.Recommendations;
        Assert.Equal(expectedFirst, top[0].Item.Id);
        Assert.Contains(top, r => r.Item.Id is 3065 or 3075 && r.Item.Id != expectedFirst);
    }

    [Fact]
    public void Spine_keeps_the_meta_order_when_the_threat_is_lukewarm()
    {
        var advisor = new ItemAdvisor(TestCatalog.Catalog());
        // Daño parejo: ningún muro tiene una amenaza clara → manda el orden de op.gg.
        var state = TestCatalog.AramState(9000,
            ("Leona", "ORDER", 0, None),
            ("Jinx", "CHAOS", 2, None), ("Ahri", "CHAOS", 2, None));
        var top = advisor.Advise(state, BuildArchetype.Tank, Stats("Leona", 3075, 3065))!.Recommendations;
        Assert.Equal(3075, top[0].Item.Id);
    }

    // ---------- 3. Selección por cobertura ----------

    [Fact]
    public void Second_slot_covers_the_other_damage_type_instead_of_repeating_armor()
    {
        var state = TestCatalog.AramState(9000,
            ("Leona", "ORDER", 0, None),
            ("Jinx", "CHAOS", 4, None), ("Vayne", "CHAOS", 3, None),
            ("Ahri", "CHAOS", 4, None), ("Malzahar", "CHAOS", 3, None));

        var covered = new ItemAdvisor(TestCatalog.Catalog(), new ItemsConfig { CoverageOverlapPenalty = 0.6 })
            .Advise(state, BuildArchetype.Tank)!.Recommendations;
        Assert.Contains(covered, r => r.Item.HasTag("Armor"));
        Assert.Contains(covered, r => r.Item.HasTag("SpellBlock"));
        // El slot que repite dimensión lo dice.
        var naive = new ItemAdvisor(TestCatalog.Catalog(), new ItemsConfig { CoverageOverlapPenalty = 0 })
            .Advise(state, BuildArchetype.Tank)!.Recommendations;
        Assert.True(naive[0].Item.Id == covered[0].Item.Id, "el primer slot no depende de la cobertura");
    }

    // ---------- 4. Reloj de partida ----------

    private static GameState Aram(double t, params GameEvent[] events)
    {
        var state = TestCatalog.AramState(0, ("Ahri", "ORDER", 0, None), ("Zed", "CHAOS", 0, None));
        state.GameData.GameTime = t;
        state.Events.Events.AddRange(events);
        return state;
    }

    [Fact]
    public void Clock_estimates_remaining_time_from_mode_lead_and_inhibitors()
    {
        var cfg = new ItemsConfig();
        Assert.Equal(1200, GameClock.RemainingSeconds(Aram(0), cfg, isAram: true));
        Assert.Equal(cfg.MinRemainingSeconds, GameClock.RemainingSeconds(Aram(1500), cfg, isAram: true));
        Assert.Equal(1200 * 0.7, GameClock.RemainingSeconds(Aram(0), cfg, isAram: true, ahead: 1), 6);
        var inhib = new GameEvent { EventName = "InhibKilled", EventTime = 500 };
        Assert.Equal(cfg.InhibitorRemainingSeconds, GameClock.RemainingSeconds(Aram(600, inhib), cfg, isAram: true));
        Assert.Equal(1200 - 900, GameClock.RemainingSeconds(Aram(900, inhib), cfg, isAram: true)); // hace >300 s: respawneó
        Assert.Equal(1800, GameClock.RemainingSeconds(Aram(0), cfg, isAram: false));
    }

    [Fact]
    public void Stacking_items_are_discounted_when_no_game_is_left()
    {
        // Rabadon's construye desde 1058: se lo declara "componente que se apila" para el test.
        var cfg = new ItemsConfig { StackingComponentIds = new() { 1058 }, MaxRecommendations = 30,
            AffordabilityBonus = 0, AramAffordabilityBonus = 0 };
        var advisor = new ItemAdvisor(TestCatalog.Catalog(), cfg);
        ItemRecommendation Rabadon(double t)
        {
            var s = Aram(t); s.ActivePlayer!.CurrentGold = 9000;
            return advisor.Advise(s, BuildArchetype.Mage)!.Recommendations.Single(r => r.Item.Id == 3089);
        }
        var early = Rabadon(0);
        var late = Rabadon(1100);
        Assert.Equal(0, early.ScoreBreakdown!.Timing);
        Assert.Equal(cfg.StackingLateMag, late.ScoreBreakdown!.Timing, 6);
        Assert.Contains(late.Reasons, r => r.Contains("little game left"));
        Assert.True(late.Score < early.Score);
    }

    [Fact]
    public void Expensive_items_are_discounted_when_they_cannot_be_afforded_in_time()
    {
        var cfg = new ItemsConfig { MaxRecommendations = 30, AffordabilityBonus = 0, AramAffordabilityBonus = 0 };
        var advisor = new ItemAdvisor(TestCatalog.Catalog(), cfg);
        var s = Aram(1000); // quedan 200 s; 3.600 g a 5 g/s = 720 s
        var rabadon = advisor.Advise(s, BuildArchetype.Mage)!.Recommendations.Single(r => r.Item.Id == 3089);
        Assert.Equal(cfg.UnreachableMag, rabadon.ScoreBreakdown!.Timing, 6);
        Assert.Contains(rabadon.Reasons, r => r.Contains("before the game ends"));

        var early = Aram(0);
        var reachable = advisor.Advise(early, BuildArchetype.Mage)!.Recommendations.Single(r => r.Item.Id == 3089);
        Assert.Equal(0, reachable.ScoreBreakdown!.Timing);
    }

    // ---------- 5. Prior personal ----------

    [Fact]
    public void Personal_prior_counts_games_and_item_frequency()
    {
        var prior = PersonalBuildPrior.FromGames(new (string, IEnumerable<int>)[]
        {
            ("Ahri", new[] { 3089, 3102, 3102 }),
            ("Ahri", new[] { 3089 }),
            ("ahri", new[] { 3157 }),
            ("Zed", new[] { 3036 }),
        });
        Assert.Equal(3, prior.Games("AHRI"));
        Assert.Equal(2.0 / 3, prior.Frequency("Ahri", 3089), 6);
        Assert.Equal(1.0 / 3, prior.Frequency("Ahri", 3102), 6);   // dos compras en una partida cuentan una vez
        Assert.Equal(0, prior.Frequency("Ahri", 9999));
        Assert.Equal(0, prior.Games("Jinx"));
    }

    [Fact]
    public void Personal_prior_loads_from_journal_files_and_skips_broken_ones()
    {
        var dir = Path.Combine(Path.GetTempPath(), "plc-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.jsonl"),
                """{"kind":"start","mode":"ARAM","map":12,"me":"Ahri","allies":[],"enemies":[]}""" + "\n" +
                """{"kind":"tick","t":30,"snapshot":{"huge":"ignored"}}""" + "\n" +
                """{"kind":"buy","t":40,"item":3089,"rank":0,"top":[3089],"gold":100}""" + "\n" +
                """{"kind":"end","t":900,"result":"Win"}""" + "\n");
            File.WriteAllText(Path.Combine(dir, "b.jsonl"),
                """{"kind":"start","me":"Ahri"}""" + "\n" + """{"kind":"buy","item":3102}""" + "\n");
            File.WriteAllText(Path.Combine(dir, "broken.jsonl"), "{\"kind\":\"start\",\"me\":\"Ahri\"}\n{not json\n");
            File.WriteAllText(Path.Combine(dir, "nobuys.jsonl"), """{"kind":"start","me":"Zed"}""" + "\n");

            var prior = PersonalBuildPrior.Load(dir);
            Assert.Equal(2, prior.Games("Ahri"));
            Assert.Equal(0.5, prior.Frequency("Ahri", 3089), 6);
            Assert.Equal(0, prior.Games("Zed"));
            Assert.Same(PersonalBuildPrior.Empty, PersonalBuildPrior.Load(Path.Combine(dir, "missing")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Personal_prior_lifts_the_item_you_always_build_and_says_so()
    {
        var advisor = new ItemAdvisor(TestCatalog.Catalog(),
            new ItemsConfig { MaxRecommendations = 30, AffordabilityBonus = 0, AramAffordabilityBonus = 0 });
        var state = TestCatalog.AramState(9000, ("Ahri", "ORDER", 0, None), ("Zed", "CHAOS", 0, None));
        var always = PersonalBuildPrior.FromGames(Enumerable.Repeat(("Ahri", (IEnumerable<int>)new[] { 3102 }), 4));
        var tooFew = PersonalBuildPrior.FromGames(Enumerable.Repeat(("Ahri", (IEnumerable<int>)new[] { 3102 }), 2));

        var without = advisor.Advise(state)!.Recommendations.Single(r => r.Item.Id == 3102);
        var with = advisor.Advise(state, personal: always)!.Recommendations.Single(r => r.Item.Id == 3102);
        var few = advisor.Advise(state, personal: tooFew)!.Recommendations.Single(r => r.Item.Id == 3102);

        Assert.Equal(0, without.ScoreBreakdown!.Personal);
        Assert.Equal(6.0, with.ScoreBreakdown!.Personal, 6);
        Assert.Contains(with.Reasons, r => r.Contains("of your Ahri games"));
        Assert.Equal(0, few.ScoreBreakdown!.Personal);   // bajo el mínimo de partidas no pesa
    }
}
