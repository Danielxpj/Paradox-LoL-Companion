using System.Net.Http;
using System.Text;
using System.Text.Json;
using ParadoxLoLCompanion.Core.Net;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Core.Connectors.Lcu;

/// <summary>
/// Escribe la página de runas recomendada en el cliente de LoL vía LCU:
/// borra la página previa de la app (prefijo fijo), crea la nueva y el cliente
/// la deja seleccionada. Todos los fallos devuelven un mensaje legible, nunca
/// lanzan — aplicar runas es un extra, no puede tumbar el flujo de consejo.
/// </summary>
public sealed class LcuRuneWriter
{
    public const string PagePrefix = "Paradox: ";
    /// <summary>Prefijo del nombre anterior de la app: sus páginas también se limpian.</summary>
    private const string LegacyPagePrefix = "ParadoxLoLCompanion: ";
    private const string PagesPath = "/lol-perks/v1/pages";

    private readonly LockfileLocator _locator;
    private readonly HttpClient _http;

    public LcuRuneWriter(LockfileLocator? locator = null, HttpClient? http = null)
    {
        _locator = locator ?? new LockfileLocator();
        _http = http ?? LocalHttpClientFactory.Create();
    }

    /// <summary><c>null</c> = éxito; si no, el mensaje de error para la UI.</summary>
    public async Task<string?> ApplyAsync(string championName, RunePageStats runes,
        CancellationToken ct = default)
    {
        var creds = _locator.Locate();
        if (creds is null)
            return "LoL client is not running.";
        try
        {
            // 1) Borrar cualquier página previa creada por la app (límite de páginas).
            using (var listResp = await SendAsync(creds, HttpMethod.Get, PagesPath, null, ct).ConfigureAwait(false))
            {
                if (listResp.IsSuccessStatusCode)
                {
                    var json = await listResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    foreach (var page in doc.RootElement.EnumerateArray())
                    {
                        var name = page.GetProperty("name").GetString() ?? "";
                        if (name.StartsWith(PagePrefix, StringComparison.Ordinal)
                            || name.StartsWith(LegacyPagePrefix, StringComparison.Ordinal))
                            (await SendAsync(creds, HttpMethod.Delete,
                                $"{PagesPath}/{page.GetProperty("id").GetInt64()}", null, ct).ConfigureAwait(false)).Dispose();
                    }
                }
            }

            // 2) Crear la nueva (el cliente la marca como actual al crearla con current=true).
            var payload = BuildPagePayload(PagePrefix + championName, runes);
            using var createResp = await SendAsync(creds, HttpMethod.Post, PagesPath, payload, ct).ConfigureAwait(false);
            if (createResp.IsSuccessStatusCode)
                return null;
            // Caso típico: todas las páginas ocupadas y ninguna borrable.
            return $"LoL client rejected the rune page (HTTP {(int)createResp.StatusCode}). Free a rune page slot and retry.";
        }
        catch (Exception ex)
        {
            return $"Could not apply runes: {ex.Message}";
        }
    }

    internal static string BuildPagePayload(string name, RunePageStats runes) =>
        JsonSerializer.Serialize(new
        {
            name,
            primaryStyleId = runes.PrimaryPageId,
            subStyleId = runes.SecondaryPageId,
            selectedPerkIds = runes.PrimaryRuneIds
                .Concat(runes.SecondaryRuneIds)
                .Concat(runes.StatModIds)
                .ToArray(),
            current = true,
        });

    private async Task<HttpResponseMessage> SendAsync(LcuCredentials creds, HttpMethod method,
        string path, string? jsonBody, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, creds.BaseUrl + path);
        req.Headers.TryAddWithoutValidation("Authorization", creds.BasicAuthHeader);
        if (jsonBody is not null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await _http.SendAsync(req, ct).ConfigureAwait(false);
    }
}
