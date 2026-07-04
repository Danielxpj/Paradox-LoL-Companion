using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LoLAdvisor.Core.Stats;

/// <summary>Abstracción del cliente OP.GG para poder inyectar fakes en tests.</summary>
public interface IOpggClient
{
    /// <summary>Texto crudo del tool, o <c>null</c> ante cualquier fallo.</summary>
    Task<string?> GetChampionAnalysisTextAsync(
        string champion, string gameMode, string position, CancellationToken ct = default);
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

    public OpggMcpClient(HttpClient? http = null) =>
        _http = http ?? SharedHttp;

    public async Task<string?> GetChampionAnalysisTextAsync(
        string champion, string gameMode, string position, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(
                    BuildToolCallJson(champion, gameMode, position), Encoding.UTF8, "application/json"),
            };
            // El transporte MCP exige aceptar ambos tipos aunque responda JSON plano.
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ExtractToolText(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Las stats son opcionales: cualquier fallo degrada a "sin prior".
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

    /// <summary>
    /// Extrae <c>result.content[0].text</c> del sobre JSON-RPC. Acepta tanto JSON
    /// plano como frames SSE ("data: {json}"). <c>null</c> ante error del tool o
    /// formato inesperado (nunca lanza).
    /// </summary>
    internal static string? ExtractToolText(string body)
    {
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
            var result = doc.RootElement.GetProperty("result");
            if (result.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True)
                return null;
            return result.GetProperty("content")[0].GetProperty("text").GetString();
        }
        catch
        {
            return null;
        }
    }
}
