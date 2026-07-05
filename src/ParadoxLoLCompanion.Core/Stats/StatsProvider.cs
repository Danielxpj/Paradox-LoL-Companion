using System.Text.RegularExpressions;

namespace ParadoxLoLCompanion.Core.Stats;

/// <summary>
/// Punto de entrada de la capa estadística: cache-first, fetch al MCP de OP.GG
/// en miss, y <c>null</c> ante cualquier fallo — la app funciona igual sin stats.
/// </summary>
public sealed class StatsProvider
{
    private readonly IOpggClient _client;
    private readonly StatsCache _cache;
    private readonly Action<string>? _log;

    public StatsProvider(IOpggClient? client = null, StatsCache? cache = null, Action<string>? log = null)
    {
        _client = client ?? new OpggMcpClient(log: log);
        _cache = cache ?? new StatsCache();
        _log = log;
    }

    /// <param name="livePosition">Posición cruda de la Live Client API (TOP/MIDDLE/… o vacía).</param>
    public async Task<ChampionBuildStats?> GetAsync(
        string championKey, string? livePosition, int mapNumber, string patch,
        CancellationToken ct = default)
    {
        var (mode, position) = MapModeAndPosition(livePosition, mapNumber);
        if (_cache.TryRead(patch, championKey, mode, position, out var cached))
            return cached;

        // La Grieta sin posición conocida: OP.GG rechaza "all", así que primero se
        // resuelve el rol principal del campeón y se consulta ese. El resultado se
        // guarda también bajo "all" (alias) para que la próxima sesión no re-resuelva.
        var aliasPosition = (string?)null;
        if (position == "all")
        {
            var main = await _client.GetChampionMainPositionAsync(ToOpggName(championKey), ct)
                .ConfigureAwait(false);
            if (main is null)
            {
                _log?.Invoke($"could not resolve {championKey}'s main role — no stats this game.");
                return null;
            }
            _log?.Invoke($"{championKey}: live position unknown, using main role '{main}' (OP.GG).");
            aliasPosition = position;
            position = main;
            if (_cache.TryRead(patch, championKey, mode, position, out var cachedByRole))
            {
                _cache.Write(patch, championKey, mode, aliasPosition, cachedByRole!);
                return cachedByRole;
            }
        }

        // OP.GG exige un rol concreto incluso en ARAM (rechaza "none"); el dato en
        // modo aram no depende del rol, así que se envía un token válido cualquiera.
        var apiPosition = position == "none" ? "mid" : position;
        var text = await _client.GetChampionAnalysisTextAsync(
            ToOpggName(championKey), mode, apiPosition, ct).ConfigureAwait(false);
        if (text is null)
            return null;

        var stats = OpggResponseParser.Parse(text, championKey, mode, position);
        if (stats is not null)
        {
            _cache.Write(patch, championKey, mode, position, stats);
            if (aliasPosition is not null)
                _cache.Write(patch, championKey, mode, aliasPosition, stats);
        }
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
