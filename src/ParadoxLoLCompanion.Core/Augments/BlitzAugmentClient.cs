namespace ParadoxLoLCompanion.Core.Augments;

public interface IBlitzAugmentSource
{
    /// <summary>HTML de la página del tier list, o <c>null</c> ante cualquier fallo.</summary>
    Task<string?> GetAugmentsHtmlAsync(CancellationToken ct = default);
}

/// <summary>
/// Descarga el tier list de Blitz. La página es SSR y pública, pero Cloudflare
/// rechaza user-agents no-browser (403), así que se envía uno de Chrome
/// (verificado 2026-07-17).
/// </summary>
public sealed class BlitzAugmentClient : IBlitzAugmentSource
{
    private const string Url = "https://blitz.gg/lol/aram-mayhem-augments";
    private const string BrowserUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    // Compartido: mismo motivo que OpggMcpClient (no agotar sockets).
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly HttpClient _http;
    private readonly Action<string>? _log;

    public BlitzAugmentClient(HttpClient? http = null, Action<string>? log = null)
    {
        _http = http ?? SharedHttp;
        _log = log;
    }

    public async Task<string?> GetAugmentsHtmlAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Url);
            req.Headers.TryAddWithoutValidation("User-Agent", BrowserUa);
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _log?.Invoke($"Blitz augments: HTTP {(int)resp.StatusCode}.");
                return null;
            }
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Blitz augments: {ex.Message}");
            return null;
        }
    }
}
