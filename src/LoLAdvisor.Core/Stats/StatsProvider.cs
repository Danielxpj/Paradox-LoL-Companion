using System.Text.RegularExpressions;

namespace LoLAdvisor.Core.Stats;

/// <summary>
/// Punto de entrada de la capa estadística: cache-first, fetch al MCP de OP.GG
/// en miss, y <c>null</c> ante cualquier fallo — la app funciona igual sin stats.
/// </summary>
public sealed class StatsProvider
{
    private readonly IOpggClient _client;
    private readonly StatsCache _cache;

    public StatsProvider(IOpggClient? client = null, StatsCache? cache = null)
    {
        _client = client ?? new OpggMcpClient();
        _cache = cache ?? new StatsCache();
    }

    /// <param name="livePosition">Posición cruda de la Live Client API (TOP/MIDDLE/… o vacía).</param>
    public async Task<ChampionBuildStats?> GetAsync(
        string championKey, string? livePosition, int mapNumber, string patch,
        CancellationToken ct = default)
    {
        var (mode, position) = MapModeAndPosition(livePosition, mapNumber);
        if (_cache.TryRead(patch, championKey, mode, position, out var cached))
            return cached;

        var text = await _client.GetChampionAnalysisTextAsync(
            ToOpggName(championKey), mode, position, ct).ConfigureAwait(false);
        if (text is null)
            return null;

        var stats = OpggResponseParser.Parse(text, championKey, mode, position);
        if (stats is not null)
            _cache.Write(patch, championKey, mode, position, stats);
        return stats;
    }

    /// <summary>ARAM no tiene posiciones; en la Grieta, la posición viva mapea al vocabulario de OP.GG.</summary>
    internal static (string Mode, string Position) MapModeAndPosition(string? livePosition, int mapNumber) =>
        mapNumber == 12
            ? ("aram", "none")
            : ("ranked", livePosition?.ToUpperInvariant() switch
            {
                "TOP" => "top",
                "JUNGLE" => "jungle",
                "MIDDLE" or "MID" => "mid",
                "BOTTOM" => "adc",
                "UTILITY" or "SUPPORT" => "support",
                _ => "all",
            });

    /// <summary>
    /// Key de ddragon → nombre OP.GG: frontera minúscula→mayúscula se vuelve "_"
    /// y todo a mayúsculas (MonkeyKing → MONKEY_KING). El servidor acepta también
    /// la forma sin guiones, así que los casos raros (KSante) no fallan.
    /// </summary>
    internal static string ToOpggName(string ddragonKey) =>
        Regex.Replace(ddragonKey, "(?<=[a-z])(?=[A-Z])", "_").ToUpperInvariant();
}
