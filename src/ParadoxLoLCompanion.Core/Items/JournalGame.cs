using System.Text.Json;

namespace ParadoxLoLCompanion.Core.Items;

/// <summary>Una compra registrada en el diario: segundo de partida e id del item nuevo.</summary>
public sealed record JournalBuy(double Time, int ItemId, int Rank);

/// <summary>
/// Una partida leída del diario (<see cref="GameJournal"/>): el campeón, los snapshots
/// reproducibles en orden, las compras y el resultado. Es la unidad sobre la que trabajan
/// la calibración offline (<see cref="CalibrationSweep"/>) y la verificación de amenazas
/// (<see cref="ThreatCalibration"/>). Los diarios viejos sin snapshot se leen igual (sin
/// snapshots: no se pueden reproducir, pero sus compras sí cuentan para el prior personal).
/// </summary>
public sealed class JournalGame
{
    public string File { get; init; } = "";
    public string Champion { get; init; } = "";
    public List<RecommendationSnapshot> Snapshots { get; } = new();
    public List<JournalBuy> Buys { get; } = new();
    public string? Result { get; set; }

    public bool Replayable => Snapshots.Count > 0;

    public static JournalGame Read(string path)
    {
        var game = new JournalGame { File = path };
        var champion = "";
        foreach (var line in System.IO.File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var kind = root.TryGetProperty("kind", out var k) ? k.GetString() : null;
            switch (kind)
            {
                case "start":
                    champion = root.TryGetProperty("me", out var me) ? me.GetString() ?? "" : "";
                    break;
                case "buy":
                    game.Buys.Add(new JournalBuy(
                        root.TryGetProperty("t", out var bt) ? bt.GetDouble() : 0,
                        root.GetProperty("item").GetInt32(),
                        root.TryGetProperty("rank", out var r) ? r.GetInt32() : -1));
                    break;
                case "tick":
                    if (root.TryGetProperty("snapshot", out var snap) && snap.ValueKind != JsonValueKind.Null
                        && snap.Deserialize<RecommendationSnapshot>() is { SchemaVersion: 1 } parsed)
                        game.Snapshots.Add(parsed);
                    break;
                case "end":
                    game.Result = root.TryGetProperty("result", out var res) ? res.GetString() : null;
                    break;
            }
        }
        return new JournalGame(game, champion);
    }

    public JournalGame() { }

    private JournalGame(JournalGame from, string champion)
    {
        File = from.File;
        Champion = champion;
        Snapshots.AddRange(from.Snapshots);
        Buys.AddRange(from.Buys);
        Result = from.Result;
    }

    /// <summary>Todos los diarios de un directorio (o un único archivo), ignorando los ilegibles.</summary>
    public static IEnumerable<JournalGame> ReadAll(string dirOrFile, Action<string>? onError = null)
    {
        IEnumerable<string> files = Directory.Exists(dirOrFile)
            ? Directory.EnumerateFiles(dirOrFile, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal)
            : new[] { dirOrFile };
        foreach (var file in files)
        {
            JournalGame? game = null;
            try { game = Read(file); }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                onError?.Invoke($"{file}: {ex.Message}");
            }
            if (game is not null)
                yield return game;
        }
    }
}
