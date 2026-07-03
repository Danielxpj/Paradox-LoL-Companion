using LoLAdvisor.Core.Config;
using LoLAdvisor.Core.DataDragon;
using LoLAdvisor.Core.Items;
using LoLAdvisor.Core.Models;

namespace LoLAdvisor.Core.Draft;

/// <summary>Un intercambio sugerido desde la banca, con cuánto mejora y por qué.</summary>
public sealed record BenchSuggestion(
    StaticChampion Champion,
    double Improvement,
    IReadOnlyList<string> Reasons);

/// <summary>
/// El consejo de banca para champ select: resumen del equipo, sugerencias ordenadas
/// (vacío = quedarse con el pick actual) y un veredicto listo para mostrar.
/// </summary>
public sealed record BenchAdvice(
    StaticChampion MyChampion,
    string TeamSummary,
    IReadOnlyList<BenchSuggestion> Suggestions,
    string Verdict)
{
    public BenchSuggestion? Top => Suggestions.Count > 0 ? Suggestions[0] : null;
}

/// <summary>
/// Evalúa la banca de ARAM: puntúa la composición del equipo (mezcla de daño físico/
/// mágico, frontline, tirador, soporte y CC) con cada campeón descartado en lugar del
/// tuyo, y sugiere el intercambio que más equilibra al equipo — o quedarte donde estás.
/// </summary>
public sealed class TeamBalanceAdvisor
{
    /// <summary>Mejora mínima de composición para sugerir un cambio (evita ruido).</summary>
    private const double MinImprovement = 0.25;
    private const int MaxSuggestions = 2;

    private readonly IStaticData _data;
    private readonly ItemsConfig _config;
    private readonly ChampionProfiler _profiler;

    public TeamBalanceAdvisor(IStaticData data, ItemsConfig? config = null)
    {
        _data = data;
        _config = config ?? new ItemsConfig();
        _profiler = new ChampionProfiler(data, _config);
    }

    /// <summary><c>null</c> si el catálogo no cargó, no hay banca o no se resuelve tu campeón.</summary>
    public BenchAdvice? Advise(ChampSelectSession session)
    {
        if (!_data.IsLoaded || session is not { BenchEnabled: true })
            return null;

        var myChamp = _data.ChampionById(session.LocalPlayer?.DisplayChampionId ?? 0);
        if (myChamp is null)
            return null;

        var teammates = session.MyTeam
            .Where(c => c.CellId != session.LocalPlayerCellId)
            .Select(c => _data.ChampionById(c.DisplayChampionId))
            .Where(c => c is not null)
            .Cast<StaticChampion>()
            .ToList();

        var current = Evaluate(teammates.Append(myChamp));

        var suggestions = new List<BenchSuggestion>();
        foreach (var candidate in session.BenchChampions
                     .Select(b => _data.ChampionById(b.ChampionId))
                     .Where(c => c is not null)
                     .Cast<StaticChampion>())
        {
            var alt = Evaluate(teammates.Append(candidate));
            var improvement = alt.Score - current.Score;
            if (improvement >= MinImprovement)
                suggestions.Add(new BenchSuggestion(candidate, improvement, Reasons(current, alt, candidate)));
        }

        suggestions = suggestions
            .OrderByDescending(s => s.Improvement)
            .Take(MaxSuggestions)
            .ToList();

        var summary = $"Team damage: {Pct(current.PhysShare)} physical · {Pct(current.MagShare)} magic";
        var verdict = suggestions.Count > 0
            ? $"Swap to {suggestions[0].Champion.Name} — {string.Join("; ", suggestions[0].Reasons)}."
            : $"Keep {myChamp.Name} — it's the best balance for your team.";

        return new BenchAdvice(myChamp, summary, suggestions, verdict);
    }

    // --- Puntaje de composición ---

    private sealed record TeamEval(
        double Score, double PhysShare, double MagShare,
        double Frontline, bool HasMarksman, bool HasSupport, int CcCount);

    /// <summary>
    /// Puntúa una composición: mezcla de daño 50/50 (difícil de itemizar en contra),
    /// frontline que inicie/tanquee, un tirador de daño sostenido, un soporte y CC.
    /// </summary>
    private TeamEval Evaluate(IEnumerable<StaticChampion> team)
    {
        double phys = 0, frontline = 0;
        var n = 0;
        var cc = 0;
        bool marksman = false, support = false;

        foreach (var champ in team)
        {
            var profile = _profiler.Profile(champ);
            n++;

            phys += profile.Damage switch
            {
                DamageProfile.Physical => 1.0,
                DamageProfile.Magical => 0.0,
                _ => 0.5,
            };

            frontline += profile.Archetype switch
            {
                BuildArchetype.Tank => 1.0,
                BuildArchetype.AdFighter or BuildArchetype.ApFighter => 0.5,
                _ => 0,
            };

            marksman |= profile.Archetype == BuildArchetype.Marksman;
            support |= profile.Archetype == BuildArchetype.Enchanter;
            if (_config.HeavyCcChampions.Contains(champ.Key))
                cc++;
        }

        if (n == 0)
            return new TeamEval(0, 0, 0, 0, false, false, 0);

        var physShare = phys / n;
        var magShare = 1 - physShare;
        var score =
              3.0 * (1 - Math.Abs(physShare - magShare))
            + Math.Min(frontline, 1.5)
            + (marksman ? 1.0 : 0)
            + (support ? 0.75 : 0)
            + Math.Min(cc * 0.5, 1.5);

        return new TeamEval(score, physShare, magShare, Math.Min(frontline, 1.5), marksman, support, cc);
    }

    private IReadOnlyList<string> Reasons(TeamEval current, TeamEval alt, StaticChampion candidate)
    {
        var reasons = new List<string>();

        var balanceGain = (1 - Math.Abs(alt.PhysShare - alt.MagShare))
                        - (1 - Math.Abs(current.PhysShare - current.MagShare));
        if (balanceGain > 0.05)
        {
            var majority = current.PhysShare >= current.MagShare ? "physical" : "magic";
            var majorityShare = Math.Max(current.PhysShare, current.MagShare);
            var missing = current.PhysShare >= current.MagShare ? "magic" : "physical";
            reasons.Add($"adds {missing} damage (your team is {Pct(majorityShare)} {majority})");
        }

        if (alt.Frontline > current.Frontline)
        {
            var isTank = _profiler.Profile(candidate).Archetype == BuildArchetype.Tank;
            reasons.Add(isTank ? "adds a tank/frontline" : "adds frontline");
        }

        if (alt.HasMarksman && !current.HasMarksman)
            reasons.Add("adds a marksman for sustained damage");

        if (alt.HasSupport && !current.HasSupport)
            reasons.Add("adds a support");

        if (alt.CcCount > current.CcCount && current.CcCount < 3)
            reasons.Add("adds crowd control");

        if (reasons.Count == 0)
            reasons.Add("improves the overall team balance");
        return reasons;
    }

    private static string Pct(double share) =>
        (int)Math.Round(share * 100, MidpointRounding.AwayFromZero) + "%";
}
