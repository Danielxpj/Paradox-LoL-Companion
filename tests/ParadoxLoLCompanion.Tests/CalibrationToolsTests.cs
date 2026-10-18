using System.Text.Json.Nodes;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

/// <summary>Herramientas offline sobre el diario: lectura, barrido de calibración y verificación de amenazas.</summary>
public class CalibrationToolsTests
{
    private static readonly int[] None = Array.Empty<int>();

    private static RecommendationReplayContext Context(ItemsConfig? config = null,
        Dictionary<string, ChampionBuildStats>? enemyStats = null) =>
        new("16.13.1", config ?? new ItemsConfig(), BuildArchetype.Tank, null, null) { EnemyStats = enemyStats };

    private static GameState Leona(double t, int[] mine, int[] enemy)
    {
        var s = TestCatalog.AramState(3000, ("Leona", "ORDER", 0, mine), ("Jinx", "CHAOS", 3, enemy), ("Vayne", "CHAOS", 2, enemy));
        s.GameData.GameTime = t;
        return s;
    }

    /// <summary>Escribe un diario REAL con GameJournal (mismo formato que la app) y lo relee.</summary>
    private static string WriteJournal(string dir, Dictionary<string, ChampionBuildStats>? enemyStats = null)
    {
        var journal = new GameJournal(dir);
        var advisor = new ItemAdvisor(TestCatalog.Catalog());
        void Tick(double t, int[] mine, int[] enemy)
        {
            var state = Leona(t, mine, enemy);
            var plan = advisor.Advise(state, BuildArchetype.Tank, enemyStats: enemyStats);
            journal.Record(state, plan, Context(enemyStats: enemyStats));
        }
        Tick(10, None, None);
        Tick(60, new[] { 1029 }, None);                 // compra un componente (Chain Vest)
        Tick(200, new[] { 3075 }, new[] { 3031 });      // Thornmail completo; el enemigo compró IE
        Tick(600, new[] { 3075, 3065 }, new[] { 3031, 3036 });
        return journal.CurrentFile!;
    }

    [Fact]
    public void JournalGame_reads_snapshots_buys_and_champion_from_a_real_journal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "plc-cal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = WriteJournal(dir);
            var game = JournalGame.Read(file);
            Assert.Equal("Leona", game.Champion);
            Assert.True(game.Replayable);
            Assert.Equal(4, game.Snapshots.Count);
            Assert.Equal(new[] { 1029, 3075, 3065 }, game.Buys.Select(b => b.ItemId));
            Assert.Equal(new[] { 60.0, 200, 600 }, game.Buys.Select(b => b.Time));
            Assert.Single(JournalGame.ReadAll(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Sweep_measures_agreement_with_the_next_completed_purchase()
    {
        var dir = Path.Combine(Path.GetTempPath(), "plc-cal-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteJournal(dir);
            var games = JournalGame.ReadAll(dir).ToList();
            var baseline = new ItemsConfig();
            // Decisiones: ticks a t=10 y t=60 → próxima compra completa = Thornmail (t=200);
            // t=200 → Spirit Visage (t=600); t=600 → ninguna (no cuenta).
            var catalog = TestCatalog.Catalog();
            Assert.Equal(3075, CalibrationSweep.NextCompletedPurchase(games[0], 10, catalog, baseline));
            Assert.Equal(3065, CalibrationSweep.NextCompletedPurchase(games[0], 200, catalog, baseline));
            Assert.Null(CalibrationSweep.NextCompletedPurchase(games[0], 600, catalog, baseline));

            var variants = CalibrationSweep.Variants(baseline, new JsonObject
            {
                ["maxRecommendations"] = new JsonArray(0, 30),
            });
            Assert.Equal(new[] { "baseline", "maxRecommendations=0", "maxRecommendations=30" }, variants.Select(v => v.Name));

            var results = variants.Select(v => CalibrationSweep.Evaluate(v, games, _ => TestCatalog.Catalog())).ToList();
            Assert.All(results, r => Assert.Equal(3, r.Decisions));
            Assert.Equal(0, results[1].Agreed);                       // sin top no hay acuerdo
            Assert.True(results[2].Agreed >= results[0].Agreed);      // top-30 ⊇ top-3… pero agreement@3 mira solo 3
            Assert.True(results[0].Agreement is >= 0 and <= 1);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Overlay_sets_properties_case_insensitively_and_rejects_unknown_ones()
    {
        var cfg = CalibrationSweep.Overlay(new ItemsConfig(), new JsonObject { ["MAXRECOMMENDATIONS"] = 7, ["spineSituationalWeight"] = 0.25 });
        Assert.Equal(7, cfg.MaxRecommendations);
        Assert.Equal(0.25, cfg.SpineSituationalWeight);
        Assert.Equal(new ItemsConfig().SustainTags, cfg.SustainTags);   // lo demás intacto
        Assert.Throws<InvalidDataException>(() => CalibrationSweep.Overlay(new ItemsConfig(), new JsonObject { ["noSuchField"] = 1 }));
    }

    [Fact]
    public void Threat_calibration_flags_a_miss_that_the_projection_catches()
    {
        var stats = new Dictionary<string, ChampionBuildStats>
        {
            ["Jinx"] = new() { ChampionKey = "Jinx", GameMode = "aram", Position = "none",
                CoreItems = new ItemSetStats(new[] { 3031, 3036 }, 0.3, 4000, 2100) },
        };
        var game = new JournalGame();
        // Minuto 1: Jinx/Vayne sin items. Final: ambas con tres items de crítico.
        var early = TestCatalog.AramState(0, ("Leona", "ORDER", 0, None), ("Jinx", "CHAOS", 3, None), ("Vayne", "CHAOS", 2, None));
        early.GameData.GameTime = 60;
        var final = TestCatalog.AramState(0, ("Leona", "ORDER", 0, None),
            ("Jinx", "CHAOS", 8, new[] { 3031, 3036, 3033 }), ("Vayne", "CHAOS", 5, new[] { 3031, 3036, 3033 }));
        final.GameData.GameTime = 1100;
        game.Snapshots.Add(new RecommendationSnapshot(early, Context(enemyStats: stats), Array.Empty<RecordedRecommendation>()));
        game.Snapshots.Add(new RecommendationSnapshot(final, Context(), Array.Empty<RecordedRecommendation>()));

        var check = ThreatCalibration.Evaluate(game, TestCatalog.Catalog(), new ItemsConfig())!;
        var crit = check.Signals.Single(s => s.Signal == "CritThreat");
        Assert.True(crit.Miss, $"crit temprano {crit.Early} / final {crit.Final}");
        var gwBlind = check.Signals.Single(s => s.Signal == "EnemyAntiHeal");
        var gwProj = check.Signals.Single(s => s.Signal == "EnemyAntiHeal" + ThreatCalibration.ProjectedSuffix);
        Assert.Equal(gwBlind.Final, gwProj.Final);
        Assert.True(gwProj.Early >= gwBlind.Early);

        var summary = ThreatCalibration.Summarize(new[] { check });
        Assert.Equal(1, summary.Single(s => s.Signal == "CritThreat").Misses);
        Assert.Equal(0, summary.Single(s => s.Signal == "CritThreat").FalseAlarms);
        Assert.Null(ThreatCalibration.Evaluate(new JournalGame(), TestCatalog.Catalog(), new ItemsConfig()));
    }
}
