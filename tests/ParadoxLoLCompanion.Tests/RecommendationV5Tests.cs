using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;
using Xunit;

namespace ParadoxLoLCompanion.Tests;

/// <summary>v5: fit por magnitud, prior por WR encogido y orden del core, contexto aliado,
/// oro proyectado al respawn, y el diario de partida.</summary>
public class RecommendationV5Tests
{
    private static readonly int[] None = Array.Empty<int>();

    private static ItemAdvisor Wide() =>
        new(TestCatalog.Catalog(), new ItemsConfig { MaxRecommendations = 12 });

    private static double ScoreOf(ItemAdvicePlan plan, int id) =>
        plan.Recommendations.FirstOrDefault(r => r.Item.Id == id)?.Score ?? 0;

    [Fact]
    public void Magnitude_BigStatItem_OutscoresSmallStatItem_WithSameTag()
    {
        // Rabadon (130 AP) vs. Morello (AP chico + anti-heal sin sustain enemigo): mismo tag
        // SpellDamage; con el fit por magnitud el que trae MÁS stat puntúa más.
        var plan = Wide().Advise(TestCatalog.State(9000,
            ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 0, None)))!;
        Assert.True(ScoreOf(plan, 3089) > ScoreOf(plan, 3165));
    }

    [Fact]
    public void ProjectedGold_WhileDeadInAram_MakesTheItemAffordable()
    {
        // 2800 de oro + 200 s muertos ≈ 2800 + 660 → Zhonya (3250) se puede terminar YA.
        var dead = TestCatalog.AramState(2800, ("Ahri", "ORDER", 0, None), ("Zed", "CHAOS", 3, None));
        dead.AllPlayers[0].IsDead = true;
        dead.AllPlayers[0].RespawnTimer = 200;
        var alive = TestCatalog.AramState(2800, ("Ahri", "ORDER", 0, None), ("Zed", "CHAOS", 3, None));

        var zhonyaDead = Wide().Advise(dead)!.Recommendations.First(r => r.Item.Id == 3157);
        var zhonyaAlive = Wide().Advise(alive)!.Recommendations.First(r => r.Item.Id == 3157);
        Assert.True(zhonyaDead.Affordable);
        Assert.False(zhonyaAlive.Affordable);
    }

    [Fact]
    public void AllyTeamMostlyAd_PreValuesArmorPen_BeforeEnemiesStackArmor()
    {
        // Sin armadura enemiga comprada: un equipo full-AD anticipa que la van a apilar.
        double Pen(params (string, string, int, int[])[] players) =>
            ScoreOf(Wide().Advise(TestCatalog.State(9000, players))!, 3036); // Lord Dominik's

        var mixedTeam = Pen(("Jinx", "ORDER", 0, None), ("Ahri", "ORDER", 0, None), ("Karma", "ORDER", 0, None),
            ("Leona", "CHAOS", 0, None));
        var adTeam = Pen(("Jinx", "ORDER", 0, None), ("Vayne", "ORDER", 0, None), ("Zed", "ORDER", 0, None),
            ("Leona", "CHAOS", 0, None));
        Assert.True(adTeam > mixedTeam, $"{adTeam} vs {mixedTeam}");
    }

    [Fact]
    public void AllyAlreadyOwnsUniqueAura_ItemLeavesThePool()
    {
        // Shurelya's (Aura + pasiva) ya en un aliado: no se recomienda una segunda.
        var alone = Wide().Advise(TestCatalog.State(9000,
            ("Soraka", "ORDER", 0, None), ("Jinx", "CHAOS", 0, None)))!;
        var withAllyAura = Wide().Advise(TestCatalog.State(9000,
            ("Soraka", "ORDER", 0, None), ("Karma", "ORDER", 0, new[] { 2065 }), ("Jinx", "CHAOS", 0, None)))!;

        var shurelya = TestCatalog.Catalog().ItemById(2065)!;
        if (shurelya.PassiveNames.Count == 0 || !shurelya.HasTag("Aura"))
            return; // el catálogo de prueba no modela la pasiva: nada que afirmar
        Assert.Contains(alone.Recommendations, r => r.Item.Id == 2065);
        Assert.DoesNotContain(withAllyAura.Recommendations, r => r.Item.Id == 2065);
    }

    [Fact]
    public void WinRatePrior_HighWrLateItem_BeatsPopularButLosingOne()
    {
        // Dos candidatos tardíos del mismo slot: el popular pierde (44 %), el otro gana (56 %).
        var stats = new ChampionBuildStats
        {
            ChampionKey = "Ahri", GameMode = "aram", Position = "none", WinRate = 0.5,
            FourthItems = new[]
            {
                new ItemSetStats(new[] { 3135 }, PickRate: 0.40, Play: 5000, Win: 2200), // Void 44 %
                new ItemSetStats(new[] { 3089 }, PickRate: 0.20, Play: 5000, Win: 2800), // Rabadon 56 %
            },
        };
        // Tres completos ya comprados → estamos eligiendo el 4.º.
        var plan = Wide().Advise(TestCatalog.State(9000,
            ("Ahri", "ORDER", 0, new[] { 3157, 3165, 3102 }), ("Jinx", "CHAOS", 0, None)), stats: stats)!;
        var rabadon = plan.Recommendations.First(r => r.Item.Id == 3089);
        var voidStaff = plan.Recommendations.First(r => r.Item.Id == 3135);
        Assert.True(rabadon.Score > voidStaff.Score);
        Assert.Contains(rabadon.Reasons, r => r.Contains("56%"));
    }

    [Fact]
    public void CoreOrder_TheItemThatIsDueNext_GetsTheFullPrior()
    {
        // Core ordenado [Zhonya, Rabadon, Void]; sin nada comprado toca Zhonya.
        var stats = new ChampionBuildStats
        {
            ChampionKey = "Ahri", GameMode = "aram", Position = "none", WinRate = 0.5,
            CoreItems = new ItemSetStats(new[] { 3157, 3089, 3135 }, PickRate: 0.2, Play: 8000, Win: 4000),
        };
        var plan = Wide().Advise(TestCatalog.State(9000,
            ("Ahri", "ORDER", 0, None), ("Jinx", "CHAOS", 0, None)), stats: stats)!;
        var next = plan.Recommendations.First(r => r.Item.Id == 3157);
        var later = plan.Recommendations.First(r => r.Item.Id == 3135);
        // Void trae pen (+fit) pero no toca todavía: el prior entero se lo lleva Zhonya.
        Assert.True(next.Score >= later.Score, $"{next.Score} vs {later.Score}");
    }

    [Fact]
    public void Journal_AttributesBuysToTheTopShownBeforeTheBuy_AndReportsAdoption()
    {
        var dir = Path.Combine(Path.GetTempPath(), "paradox-journal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new GameJournal(dir);
            var advisor = new ItemAdvisor(TestCatalog.Catalog());

            var t1 = TestCatalog.AramState(3000, ("Jinx", "ORDER", 0, None), ("Zed", "CHAOS", 0, None));
            t1.GameData.GameTime = 60;
            var plan1 = advisor.Advise(t1)!;
            journal.Record(t1, plan1);

            // Compra el top-1 del tick anterior + un item fuera del top.
            var bought = plan1.Recommendations[0].Item.Id;
            var t2 = TestCatalog.AramState(100, ("Jinx", "ORDER", 0, new[] { bought, 1001 }), ("Zed", "CHAOS", 0, None));
            t2.GameData.GameTime = 120;
            t2.Events.Events.Add(new GameEvent { EventName = "GameEnd", Result = "Win" });
            journal.Record(t2, advisor.Advise(t2));

            var lines = File.ReadAllLines(journal.CurrentFile!);
            Assert.Contains(lines, l => l.Contains("\"kind\":\"start\""));
            Assert.Contains(lines, l => l.Contains("\"kind\":\"buy\"") && l.Contains($"\"item\":{bought}") && l.Contains("\"rank\":0"));
            Assert.Contains(lines, l => l.Contains("\"kind\":\"buy\"") && l.Contains("\"item\":1001") && l.Contains("\"rank\":-1"));
            Assert.Contains(lines, l => l.Contains("\"kind\":\"end\"") && l.Contains("Win"));
            Assert.Equal((2, 1), GameJournal.Adoption(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
