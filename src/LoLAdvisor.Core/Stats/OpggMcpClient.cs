using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LoLAdvisor.Core.Stats;

/// <summary>Abstracción del cliente OP.GG para poder inyectar fakes en tests.</summary>
public interface IOpggClient
{
    /// <summary>Texto crudo del tool, o <c>null</c> ante cualquier fallo.</summary>
    Task<string?> GetChampionAnalysisTextAsync(
        string champion, string gameMode, string position, CancellationToken ct = default);

    /// <summary>
    /// Rol principal del campeón en ranked ("top"/"jungle"/"mid"/"adc"/"support"),
    /// o <c>null</c> si no se pudo resolver. Necesario porque el tool EXIGE una
    /// posición concreta (rechaza "all"/"none") y la Live Client API muchas veces
    /// no informa la posición del jugador.
    /// </summary>
    Task<string?> GetChampionMainPositionAsync(string champion, CancellationToken ct = default);
}

/// <summary>
/// Cliente mínimo del MCP de OP.GG (JSON-RPC sobre HTTP). El servidor es
/// stateless: no requiere handshake initialize ni sesión — un POST de
/// tools/call por consulta basta (verificado 2026-07-04).
/// </summary>
public sealed class OpggMcpClient : IOpggClient
{
    private const string Endpoint = "https://mcp-api.op.gg/mcp";

    // Campos pedidos al tool: cubren lo que consume OpggResponseParser (los nombres extra ayudan a depurar fixtures).
    private static readonly string[] DesiredFields =
    {
        "data.core_items.{ids[],ids_names[],pick_rate,play,win}",
        "data.boots.{ids[],ids_names[],pick_rate,play,win}",
        "data.starter_items.{ids[],ids_names[],pick_rate,play,win}",
        "data.fourth_items[].{ids[],ids_names[],pick_rate,play,win}",
        "data.fifth_items[].{ids[],ids_names[],pick_rate,play,win}",
        "data.runes.{id,pick_rate,play,primary_page_id,primary_page_name,primary_rune_ids[],primary_rune_names[],secondary_page_id,secondary_page_name,secondary_rune_ids[],secondary_rune_names[],stat_mod_ids[],stat_mod_names[],win}",
        "data.skills.{order[],pick_rate,play,win}",
        "data.summary.average_stats.{win_rate,pick_rate,play}",
    };

    // Compartido: HttpClient está pensado para reutilizarse; instanciar uno por
    // cliente agota sockets si alguien crea clientes por consulta.
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly HttpClient _http;
    private readonly Action<string>? _log;

    public OpggMcpClient(HttpClient? http = null, Action<string>? log = null)
    {
        _http = http ?? SharedHttp;
        _log = log;
    }

    public Task<string?> GetChampionAnalysisTextAsync(
        string champion, string gameMode, string position, CancellationToken ct = default) =>
        CallToolAsync(BuildToolCallJson(champion, gameMode, position),
            $"{champion} {gameMode}/{position}", ct);

    public async Task<string?> GetChampionMainPositionAsync(string champion, CancellationToken ct = default)
    {
        // El tool exige una posición aunque solo pidamos el resumen; "mid" es un
        // token válido cualquiera — la lista de posiciones jugadas no depende de él.
        var text = await CallToolAsync(
            BuildPositionsCallJson(champion), $"{champion} positions", ct).ConfigureAwait(false);
        return text is null ? null : ParseMainPosition(text);
    }

    private async Task<string?> CallToolAsync(string payload, string queryLabel, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            // El transporte MCP exige aceptar ambos tipos aunque responda JSON plano.
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _log?.Invoke($"OP.GG HTTP {(int)resp.StatusCode} ({queryLabel})");
                return null;
            }
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var text = ExtractToolText(body, out var error);
            if (text is null)
                _log?.Invoke($"OP.GG rejected the query ({queryLabel}): {error ?? "unexpected response format"}");
            return text;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Las stats son opcionales: cualquier fallo degrada a "sin prior".
            _log?.Invoke($"OP.GG request failed ({queryLabel}): {ex.Message}");
            return null;
        }
    }

    internal static string BuildToolCallJson(string champion, string gameMode, string position) =>
        JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "lol_get_champion_analysis",
                arguments = new
                {
                    champion,
                    game_mode = gameMode,
                    position,
                    desired_output_fields = DesiredFields,
                },
            },
        });

    /// <summary>Payload liviano para resolver el rol principal: solo el resumen de posiciones.</summary>
    internal static string BuildPositionsCallJson(string champion) =>
        JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "lol_get_champion_analysis",
                arguments = new
                {
                    champion,
                    game_mode = "ranked",
                    position = "mid",
                    desired_output_fields = new[]
                    {
                        "data.summary.positions[].name",
                        "data.summary.positions[].stats.play",
                    },
                },
            },
        });

    /// <summary>
    /// Rol con más partidas del texto compacto del tool:
    /// <c>Position("ADC",Stats(233174,…))</c> → "adc". <c>null</c> si no hay posiciones.
    /// </summary>
    internal static string? ParseMainPosition(string text)
    {
        var best = Regex.Matches(text, """Position\("([A-Z]+)",Stats\((\d+)""")
            .Select(m => (Name: m.Groups[1].Value, Play: long.Parse(m.Groups[2].Value)))
            .OrderByDescending(p => p.Play)
            .Select(p => p.Name)
            .FirstOrDefault();
        return best switch
        {
            "TOP" => "top",
            "JUNGLE" => "jungle",
            "MID" or "MIDDLE" => "mid",
            "ADC" or "BOTTOM" => "adc",
            "SUPPORT" or "UTILITY" => "support",
            _ => null,
        };
    }

    internal static string? ExtractToolText(string body) => ExtractToolText(body, out _);

    /// <summary>
    /// Extrae <c>result.content[0].text</c> del sobre JSON-RPC. Acepta tanto JSON
    /// plano como frames SSE ("data: {json}"). <c>null</c> ante error del tool o
    /// formato inesperado (nunca lanza); <paramref name="error"/> trae el motivo
    /// cuando el servidor lo dice (p.ej. "The selected position is invalid").
    /// </summary>
    internal static string? ExtractToolText(string body, out string? error)
    {
        error = null;
        try
        {
            var json = body.TrimStart();
            if (json.StartsWith("event:", StringComparison.Ordinal)
                || json.StartsWith("data:", StringComparison.Ordinal))
            {
                // Solo se toma la primera línea "data:"; las respuestas de este servidor son JSON plano en un único frame.
                var dataLine = json.Split('\n')
                    .FirstOrDefault(l => l.StartsWith("data:", StringComparison.Ordinal));
                if (dataLine is null)
                    return null;
                json = dataLine["data:".Length..].Trim();
            }

            using var doc = JsonDocument.Parse(json);
            // Error JSON-RPC (p.ej. posición inválida): el motivo viene en error.message.
            if (doc.RootElement.TryGetProperty("error", out var rpcError))
            {
                error = rpcError.TryGetProperty("message", out var msg) ? msg.GetString() : "rpc error";
                return null;
            }
            var result = doc.RootElement.GetProperty("result");
            if (result.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True)
            {
                error = "tool error";
                return null;
            }
            return result.GetProperty("content")[0].GetProperty("text").GetString();
        }
        catch
        {
            return null;
        }
    }
}
