using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Tests;

/// <summary>
/// Vara de medición de la calibración (H2), métrica de <b>counter-responsiveness</b>:
/// barridos sintéticos de cada grado de amenaza que afirman que el puntaje del counter
/// correspondiente es MONÓTONO NO-DECRECIENTE al subir la amenaza. No necesita corpus:
/// detecta regresiones de calibración (un cambio que rompe la respuesta de un counter)
/// sin depender de partidas grabadas. Las métricas agreement@3 y churn quedan pendientes
/// del corpus de partidas reales (H1).
/// </summary>
public class CalibrationHarnessTests
{
    private static double ScoreOf(GameState state, int itemId, BuildArchetype? forced = null)
    {
        var plan = new ItemAdvisor(TestCatalog.Catalog(), new ItemsConfig { MaxRecommendations = 30 })
            .Advise(state, forced)!;
        return plan.Recommendations.FirstOrDefault(r => r.Item.Id == itemId)?.Score ?? 0;
    }

    [Fact]
    public void CounterResponsiveness_MoreEnemyArmor_NeverLowersPen()
    {
        // Barrido de armadura enemiga: el puntaje de la penetración (Lord Dominik's) nunca baja.
        double prev = -1;
        foreach (var vests in new[] { 0, 1, 2, 3, 4 })
        {
            var items = Enumerable.Repeat(1029, vests).ToArray();   // Chain Vest = 40 armadura
            var state = TestCatalog.State(20000,
                ("Zed", "ORDER", 0, Array.Empty<int>()),
                ("Leona", "CHAOS", 0, items));
            var score = ScoreOf(state, 3036);   // Lord Dominik's Regards (ArmorPenetration)
            Assert.True(score >= prev - 1e-6, $"pen bajó a {vests} vests: {score} < {prev}");
            prev = score;
        }
    }

    [Fact]
    public void CounterResponsiveness_MoreEnemyMagicResist_NeverLowersMagicPen()
    {
        // Barrido de RM enemiga: la pen mágica (Void Staff) para un mago nunca baja.
        double prev = -1;
        foreach (var mantles in new[] { 0, 1, 2, 3, 4 })
        {
            var items = Enumerable.Repeat(1033, mantles).ToArray();   // Null-Magic Mantle = 25 RM
            var state = TestCatalog.State(20000,
                ("Ahri", "ORDER", 0, Array.Empty<int>()),
                ("Leona", "CHAOS", 0, items));
            var score = ScoreOf(state, 3135);   // Void Staff (MagicPenetration)
            Assert.True(score >= prev - 1e-6, $"pen mágica bajó a {mantles} mantles: {score} < {prev}");
            prev = score;
        }
    }

    [Fact]
    public void CounterResponsiveness_MoreEnemySustain_NeverLowersAntiHeal()
    {
        // Barrido de curación enemiga (0→2 healers de tres, todos físicos): el anti-heal
        // (Mortal Reminder) para un tirador nunca baja.
        var rosters = new[]
        {
            new[] { ("Zed", "CHAOS", 0, Array.Empty<int>()), ("Zed", "CHAOS", 0, Array.Empty<int>()) },
            new[] { ("Warwick", "CHAOS", 0, Array.Empty<int>()), ("Zed", "CHAOS", 0, Array.Empty<int>()) },
            new[] { ("Warwick", "CHAOS", 0, Array.Empty<int>()), ("Aatrox", "CHAOS", 0, Array.Empty<int>()) },
        };
        double prev = -1;
        foreach (var enemies in rosters)
        {
            var players = new (string, string, int, int[])[] { ("Jinx", "ORDER", 0, Array.Empty<int>()) }
                .Concat(enemies).ToArray();
            var score = ScoreOf(TestCatalog.State(20000, players), 3033);   // Mortal Reminder (GW)
            Assert.True(score >= prev - 1e-6, $"anti-heal bajó con más curación: {score} < {prev}");
            prev = score;
        }
    }

    [Fact]
    public void CounterResponsiveness_MoreEnemyTankiness_NeverLowersAntiTank()
    {
        // Barrido de durabilidad enemiga (más HP+armadura): el on-hit anti-tanque (BORK)
        // para un tirador nunca baja de puntaje.
        double prev = -1;
        foreach (var belts in new[] { 0, 1, 2, 3 })
        {
            var items = Enumerable.Repeat(1011, belts).ToArray();   // Giant's Belt = 350 HP
            var state = TestCatalog.State(20000,
                ("Jinx", "ORDER", 0, Array.Empty<int>()),
                ("Leona", "CHAOS", 0, items));
            var score = ScoreOf(state, 3153);   // Blade of the Ruined King (OnHit)
            Assert.True(score >= prev - 1e-6, $"anti-tanque bajó a {belts} belts: {score} < {prev}");
            prev = score;
        }
    }
}
