using System.Text.Json;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Models;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Core.Items;

/// <summary>Inputs not contained in the live game payload, captured before Advise updates its history.</summary>
public sealed record RecommendationReplayContext(string CatalogVersion, ItemsConfig Config,
    BuildArchetype? ForcedArchetype, ChampionBuildStats? Stats, IReadOnlyList<int>? PreviousTopIds)
{
    public string EngineVersion { get; init; } = "recommendations-v6";
}

public sealed record RecordedRecommendation(int ItemId, double Score, ItemScoreBreakdown? Breakdown);

/// <summary>The full local game state includes enemy inventories, events and live player stats.</summary>
public sealed record RecommendationSnapshot(GameState State, RecommendationReplayContext Context,
    IReadOnlyList<RecordedRecommendation> Recommendations)
{
    public int SchemaVersion { get; init; } = 1;
}

public sealed record RecommendationReplayResult(double GameTime, IReadOnlyList<int> RecordedIds,
    IReadOnlyList<int> ReplayedIds, bool RankingChanged, double MaxSharedItemScoreDelta,
    ItemAdvicePlan? Plan);

/// <summary>Offline comparison against recorded decisions; this does not estimate causal win-rate changes.</summary>
public static class RecommendationReplay
{
    public static IEnumerable<RecommendationSnapshot> Read(string journalPath)
    {
        foreach (var line in File.ReadLines(journalPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var doc = JsonDocument.Parse(line);
            // Older journals and buy/end lines remain readable, but cannot be replayed.
            if (!doc.RootElement.TryGetProperty("snapshot", out var snapshot) || snapshot.ValueKind == JsonValueKind.Null)
                continue;
            var parsed = snapshot.Deserialize<RecommendationSnapshot>()
                ?? throw new InvalidDataException("Empty recommendation snapshot.");
            if (parsed.SchemaVersion != 1)
                throw new InvalidDataException($"Unsupported replay schema: {parsed.SchemaVersion}.");
            yield return parsed;
        }
    }

    public static RecommendationReplayResult Evaluate(RecommendationSnapshot snapshot, IStaticData catalog,
        ItemsConfig? candidateConfig = null)
    {
        if (snapshot.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported replay schema: {snapshot.SchemaVersion}.");
        if (!catalog.IsLoaded || catalog.Version != snapshot.Context.CatalogVersion)
            throw new InvalidOperationException("Replay requires the recorded Data Dragon catalog version.");
        var context = snapshot.Context;
        var plan = new ItemAdvisor(catalog, candidateConfig ?? context.Config).Advise(snapshot.State,
            context.ForcedArchetype, context.Stats, context.PreviousTopIds);
        var recorded = snapshot.Recommendations.Select(r => r.ItemId).ToList();
        var replayed = plan?.Recommendations.Select(r => r.Item.Id).ToList() ?? new List<int>();
        var previousScores = snapshot.Recommendations.ToDictionary(r => r.ItemId, r => r.Score);
        var delta = plan?.Recommendations.Where(r => previousScores.ContainsKey(r.Item.Id))
            .Select(r => Math.Abs(r.Score - previousScores[r.Item.Id])).DefaultIfEmpty(0).Max() ?? 0;
        return new RecommendationReplayResult(snapshot.State.GameData.GameTime, recorded, replayed,
            !recorded.SequenceEqual(replayed), delta, plan);
    }
}
