using System.Text.Json;

namespace LoLAdvisor.Core.Stats;

/// <summary>
/// Cliente mínimo del MCP de OP.GG (JSON-RPC sobre HTTP). El servidor es
/// stateless: no requiere handshake initialize ni sesión — un POST de
/// tools/call por consulta basta (verificado 2026-07-04).
/// </summary>
public sealed class OpggMcpClient
{
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
