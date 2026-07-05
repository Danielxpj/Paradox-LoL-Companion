using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Draft;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Tests;

public class TeamBalanceAdvisorTests
{
    // Ids del TestCatalog: Zed 238, Jinx 222, Vayne 67, Pyke 555, Warwick 19,
    // Amumu 32, Ahri 103, Leona 89, Soraka 16, Aatrox 266.

    private static TeamBalanceAdvisor Advisor() => new(TestCatalog.Catalog());

    private static ChampSelectSession Session(
        int myChampId, int[] teammateIds, int[] benchIds, bool benchEnabled = true)
    {
        var session = new ChampSelectSession { LocalPlayerCellId = 0, BenchEnabled = benchEnabled };
        session.MyTeam.Add(new ChampSelectCell { CellId = 0, ChampionId = myChampId });
        var cell = 1;
        foreach (var id in teammateIds)
            session.MyTeam.Add(new ChampSelectCell { CellId = cell++, ChampionId = id });
        foreach (var id in benchIds)
            session.BenchChampions.Add(new BenchChampion { ChampionId = id });
        return session;
    }

    [Fact]
    public void NotLoadedCatalog_ReturnsNull()
    {
        var advisor = new TeamBalanceAdvisor(DataDragonCatalog.Empty);
        Assert.Null(advisor.Advise(Session(238, new[] { 222 }, new[] { 32 })));
    }

    [Fact]
    public void BenchDisabled_ReturnsNull()
    {
        // Draft normal (sin banca): el asesor no aplica.
        var advice = Advisor().Advise(Session(238, new[] { 222 }, new int[0], benchEnabled: false));
        Assert.Null(advice);
    }

    [Fact]
    public void AllPhysicalTeam_SuggestsTheTankyMagicPick()
    {
        // Equipo 100 % físico, sin tanque ni CC (yo Zed). Banca: Amumu (tanque mágico
        // con CC) y Ahri (maga). Amumu equilibra más → debe ser la sugerencia top.
        var session = Session(238, new[] { 222, 67, 555, 19 }, new[] { 32, 103 });
        var advice = Advisor().Advise(session)!;

        Assert.Equal(2, advice.Suggestions.Count);
        Assert.Equal("Amumu", advice.Top!.Champion.Name);
        Assert.StartsWith("Swap to Amumu", advice.Verdict);
        Assert.Contains(advice.Top.Reasons, r => r.Contains("magic damage"));
        Assert.Contains(advice.Top.Reasons, r => r.Contains("tank"));
        Assert.Contains(advice.Top.Reasons, r => r.Contains("crowd control"));

        // Ahri también mejora (aporta daño mágico) pero menos que Amumu.
        Assert.Equal("Ahri", advice.Suggestions[1].Champion.Name);
        Assert.True(advice.Top.Improvement > advice.Suggestions[1].Improvement);
    }

    [Fact]
    public void BalancedTeam_KeepsCurrentPick()
    {
        // Equipo equilibrado (daño 50/50, tanque, tirador, soporte, CC): cambiar mi Ahri
        // por el Zed de la banca empeora la mezcla → "Keep".
        var session = Session(103, new[] { 222, 89, 16, 266 }, new[] { 238 });
        var advice = Advisor().Advise(session)!;

        Assert.Empty(advice.Suggestions);
        Assert.Contains("Keep Ahri", advice.Verdict);
    }

    [Fact]
    public void TeamSummary_ShowsDamageSplit()
    {
        var session = Session(238, new[] { 222, 67, 555, 19 }, new[] { 32 });
        var advice = Advisor().Advise(session)!;

        Assert.Contains("100% physical", advice.TeamSummary);
    }

    [Fact]
    public void UnknownBenchChampions_AreSkipped()
    {
        var session = Session(238, new[] { 222 }, new[] { 999999 });
        var advice = Advisor().Advise(session)!;

        Assert.Empty(advice.Suggestions);
        Assert.Contains("Keep", advice.Verdict);
    }

    [Fact]
    public void UsesPickIntent_WhenNotLockedYet()
    {
        // Mi celda sin confirmar (championId 0) pero con intent: se evalúa el intent.
        var session = Session(0, new[] { 222, 67, 555, 19 }, new[] { 32 });
        session.MyTeam[0].ChampionPickIntent = 238; // Zed
        var advice = Advisor().Advise(session);

        Assert.NotNull(advice);
        Assert.Equal("Zed", advice!.MyChampion.Name);
    }
}
