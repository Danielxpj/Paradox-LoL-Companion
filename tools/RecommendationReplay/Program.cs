using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;

const string Usage = """
Usage:
  RecommendationReplay replay  <journal.jsonl>      <catalog-version> <catalog-locale-dir> [candidate-items-config.json]
  RecommendationReplay sweep   <journal-dir|file>   <catalog-version> <catalog-locale-dir> <sweep.json>
  RecommendationReplay threats <journal-dir|file>   <catalog-version> <catalog-locale-dir> [early-seconds=300]

  replay  : re-evaluates every snapshot of one journal (optionally with a candidate config) and prints one JSON per frame.
  sweep   : agreement@3 of each config variant against the item the player bought next, over all journals.
            sweep.json = {"antiHealMag":[1.5,2,3], "spineSituationalWeight":[0.5,1,1.5]} (one variant per value).
  threats : did the threat announced early (minute 5) materialise in the enemy's final inventory? Per-signal MAE,
            false alarms and misses, with and without the enemy build projection.
(The legacy form `RecommendationReplay <journal.jsonl> <version> <dir> [config]` still works as `replay`.)
""";

var argv = args;
if (argv.Length > 0 && File.Exists(argv[0]) && argv[0].EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
    argv = new[] { "replay" }.Concat(argv).ToArray();
if (argv.Length < 4)
{
    Console.Error.WriteLine(Usage);
    return 2;
}
var mode = argv[0].ToLowerInvariant();
var source = argv[1];
var version = argv[2];
var localeDir = argv[3];

try
{
    var champions = File.ReadAllText(Path.Combine(localeDir, "champion.json"));
    var items = File.ReadAllText(Path.Combine(localeDir, "item.json"));
    var fullPath = Path.Combine(localeDir, "championFull.json");
    var full = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    IStaticData CatalogFor(ItemsConfig config) => DataDragonCatalog.FromJson(version, champions, items, config, full);
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    switch (mode)
    {
        case "replay":
        {
            var candidate = argv.Length >= 5
                ? JsonSerializer.Deserialize<ItemsConfig>(File.ReadAllText(argv[4]), jsonOptions)
                    ?? throw new InvalidDataException("Empty candidate items configuration.")
                : null;
            int frames = 0, changed = 0;
            IStaticData? catalog = null;
            foreach (var snapshot in RecommendationReplay.Read(source))
            {
                catalog ??= CatalogFor(candidate ?? snapshot.Context.Config);
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
        case "sweep":
        {
            if (argv.Length < 5)
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
            var sweep = JsonNode.Parse(File.ReadAllText(argv[4]))?.AsObject()
                ?? throw new InvalidDataException("sweep.json must be a JSON object.");
            var games = JournalGame.ReadAll(source, Console.Error.WriteLine).Where(g => g.Replayable).ToList();
            if (games.Count == 0)
            {
                Console.Error.WriteLine("No replayable journals found.");
                return 2;
            }
            // La base es la config con la que se grabó la primera partida (la de la app).
            var baseline = games[0].Snapshots[0].Context.Config;
            Console.WriteLine($"{"variant",-40} {"games",5} {"decisions",9} {"agreed",6} {"agreement@3",11}");
            foreach (var variant in CalibrationSweep.Variants(baseline, sweep))
            {
                var r = CalibrationSweep.Evaluate(variant, games, CatalogFor);
                Console.WriteLine($"{r.Variant,-40} {r.Games,5} {r.Decisions,9} {r.Agreed,6} {r.Agreement.ToString("P1", CultureInfo.InvariantCulture),11}");
            }
            return 0;
        }
        case "threats":
        {
            var early = argv.Length >= 5 ? double.Parse(argv[4], CultureInfo.InvariantCulture) : 300;
            var games = JournalGame.ReadAll(source, Console.Error.WriteLine).Where(g => g.Replayable).ToList();
            if (games.Count == 0)
            {
                Console.Error.WriteLine("No replayable journals found.");
                return 2;
            }
            var config = games[0].Snapshots[0].Context.Config;
            var catalog = CatalogFor(config);
            var checks = games.Select(g => ThreatCalibration.Evaluate(g, catalog, config, early))
                .OfType<ThreatCalibrationGame>().ToList();
            foreach (var g in checks)
                Console.Error.WriteLine($"{Path.GetFileName(g.File)}: early t={g.EarlyTime:0} final t={g.FinalTime:0} "
                    + string.Join(" ", g.Signals.Select(s => $"{s.Signal}={s.Early:0.00}->{s.Final:0.00}")));
            Console.WriteLine($"{"signal",-24} {"games",5} {"MAE",6} {"false+",6} {"misses",6}");
            foreach (var s in ThreatCalibration.Summarize(checks))
                Console.WriteLine($"{s.Signal,-24} {s.Games,5} {s.MeanAbsError.ToString("0.000", CultureInfo.InvariantCulture),6} {s.FalseAlarms,6} {s.Misses,6}");
            return 0;
        }
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
