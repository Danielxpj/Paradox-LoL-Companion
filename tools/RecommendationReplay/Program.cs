using System.Text.Json;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;

if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine("Usage: RecommendationReplay <journal.jsonl> <catalog-version> <catalog-locale-directory> [candidate-items-config.json]");
    return 2;
}

try
{
    var champions = File.ReadAllText(Path.Combine(args[2], "champion.json"));
    var items = File.ReadAllText(Path.Combine(args[2], "item.json"));
    var fullPath = Path.Combine(args[2], "championFull.json");
    var full = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    var candidate = args.Length == 4
        ? JsonSerializer.Deserialize<ItemsConfig>(File.ReadAllText(args[3]),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty candidate items configuration.")
        : null;
    int frames = 0, changed = 0;
    foreach (var snapshot in RecommendationReplay.Read(args[0]))
    {
        var config = candidate ?? snapshot.Context.Config;
        var catalog = DataDragonCatalog.FromJson(args[1], champions, items, config, full);
        var result = RecommendationReplay.Evaluate(snapshot, catalog, candidate);
        frames++;
        if (result.RankingChanged)
            changed++;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            result.GameTime, result.RecordedIds, result.ReplayedIds,
            result.RankingChanged, result.MaxSharedItemScoreDelta,
            recommendations = result.Plan?.Recommendations.Select(r => new
            {
                id = r.Item.Id, r.Score, r.ScoreBreakdown, r.Reasons,
                r.Affordable, r.BlockedByFullInventory, r.MissingGold,
            }),
        }));
    }
    Console.Error.WriteLine($"Replayed {frames} snapshots; {changed} rankings changed.");
    if (frames == 0)
    {
        Console.Error.WriteLine("This journal has no replay snapshots. Record a match with the updated app first.");
        return 2;
    }
    return 0;
}
catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
