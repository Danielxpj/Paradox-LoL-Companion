using System.Text.Json;

namespace ParadoxLoLCompanion.Core.Items;

/// <summary>Historial de un campeón en el diario: partidas y en cuántas apareció cada item.</summary>
public sealed class PersonalChampionHistory
{
    public int Games { get; set; }
    public Dictionary<int, int> ItemGames { get; set; } = new();
}

/// <summary>
/// Lo que VOS comprás con cada campeón, leído de los diarios de partida. Entra al ranking
/// como un prior más (al lado del meta de op.gg): un item que llevás en casi todas tus
/// partidas con el campeón no debería ser peleado por el asesor tick a tick. Serializable
/// (va en el contexto de replay) y sin estado de red.
/// </summary>
public sealed class PersonalBuildPrior
{
    /// <summary>Clave: nombre del campeón en minúsculas (el diario guarda el nombre visible).</summary>
    public Dictionary<string, PersonalChampionHistory> Champions { get; set; } = new();

    public static PersonalBuildPrior Empty { get; } = new();

    public int Games(string champion) =>
        Champions.TryGetValue(Key(champion), out var h) ? h.Games : 0;

    /// <summary>Fracción [0,1] de tus partidas con el campeón en que compraste el item.</summary>
    public double Frequency(string champion, int itemId) =>
        Champions.TryGetValue(Key(champion), out var h) && h.Games > 0
        && h.ItemGames.TryGetValue(itemId, out var n)
            ? (double)n / h.Games
            : 0;

    /// <summary>Construye el prior desde partidas ya resumidas (campeón + items comprados).</summary>
    public static PersonalBuildPrior FromGames(IEnumerable<(string Champion, IEnumerable<int> Items)> games)
    {
        var prior = new PersonalBuildPrior();
        foreach (var (champion, items) in games)
        {
            if (string.IsNullOrWhiteSpace(champion))
                continue;
            var key = Key(champion);
            if (!prior.Champions.TryGetValue(key, out var h))
                prior.Champions[key] = h = new PersonalChampionHistory();
            h.Games++;
            foreach (var id in items.Distinct())
                h.ItemGames[id] = h.ItemGames.GetValueOrDefault(id) + 1;
        }
        return prior;
    }

    /// <summary>
    /// Lee todos los diarios <c>*.jsonl</c> del directorio. Un diario roto se ignora: el
    /// prior es una comodidad, nunca puede tirar el asesor. Solo cuentan partidas con
    /// línea <c>start</c> y al menos una compra.
    /// </summary>
    public static PersonalBuildPrior Load(string dir)
    {
        if (!Directory.Exists(dir))
            return Empty;
        var games = new List<(string, IEnumerable<int>)>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
        {
            try
            {
                if (ReadGame(file) is { } game)
                    games.Add(game);
            }
            catch
            {
                // diario ilegible: se salta
            }
        }
        return FromGames(games);
    }

    private static (string, IEnumerable<int>)? ReadGame(string file)
    {
        string? champion = null;
        var items = new List<int>();
        foreach (var line in File.ReadLines(file))
        {
            // Las líneas "tick" llevan el snapshot completo (grandes): no se parsean.
            if (line.Contains("\"kind\":\"start\""))
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("me", out var me))
                    champion = me.GetString();
            }
            else if (line.Contains("\"kind\":\"buy\""))
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("item", out var item))
                    items.Add(item.GetInt32());
            }
        }
        return champion is null || items.Count == 0 ? null : (champion, items);
    }

    private static string Key(string champion) => champion.Trim().ToLowerInvariant();
}
