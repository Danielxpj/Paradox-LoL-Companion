using System.Net.Http;
using System.Text.Json;
using ParadoxLoLCompanion.Core.Config;

namespace ParadoxLoLCompanion.Core.DataDragon;

/// <summary>
/// Descarga los datos estáticos de Data Dragon (campeones e items) y los cachea en
/// disco por versión + idioma. En arranques posteriores carga del caché (sin red).
/// El idioma por defecto es inglés (en_US): los nombres de items/campeones que ve el
/// usuario salen de aquí.
/// </summary>
public sealed class DataDragonClient
{
    private const string Base = "https://ddragon.leagueoflegends.com";

    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly IReadOnlyList<string> _locales;
    private readonly ItemsConfig _itemsConfig;

    public DataDragonClient(
        HttpClient? http = null,
        string? cacheDir = null,
        IEnumerable<string>? locales = null,
        ItemsConfig? itemsConfig = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _cacheDir = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParadoxLoLCompanion", "ddragon");
        _locales = locales?.ToList() ?? new List<string> { "en_US" };
        _itemsConfig = itemsConfig ?? new ItemsConfig();
    }

    /// <summary>Carga el catálogo de la última versión CACHEADA (sin red). <c>null</c> si no hay caché.</summary>
    public async Task<DataDragonCatalog?> LoadCachedAsync(CancellationToken ct = default)
    {
        var version = LatestCachedVersion();
        if (version is null)
            return null;

        var championJson = await ReadCachedAsync(version, "champion.json", ct).ConfigureAwait(false);
        var itemJson = await ReadCachedAsync(version, "item.json", ct).ConfigureAwait(false);
        if (championJson is null || itemJson is null)
            return null;
        var championFullJson = await ReadCachedAsync(version, "championFull.json", ct).ConfigureAwait(false);

        return DataDragonCatalog.FromJson(version, championJson, itemJson, _itemsConfig, championFullJson);
    }

    /// <summary>Última versión disponible online, o <c>null</c> si no hay conexión.</summary>
    public async Task<string?> GetLatestOnlineVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync($"{Base}/api/versions.json", ct).ConfigureAwait(false);
            var versions = JsonSerializer.Deserialize<List<string>>(json);
            return versions is { Count: > 0 } ? versions[0] : null;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    /// <summary>Carga (descargando si hace falta) el catálogo de una versión concreta.</summary>
    public async Task<DataDragonCatalog> LoadVersionAsync(string version, CancellationToken ct = default)
    {
        var championJson = await GetDataFileAsync(version, "champion.json", ct).ConfigureAwait(false);
        var itemJson = await GetDataFileAsync(version, "item.json", ct).ConfigureAwait(false);
        // championFull.json (spells/pasiva) es best-effort: si falla, los flags de kit
        // quedan en false y solo mandan las listas curadas.
        var championFullJson = await TryGetDataFileAsync(version, "championFull.json", ct).ConfigureAwait(false);
        return DataDragonCatalog.FromJson(version, championJson, itemJson, _itemsConfig, championFullJson);
    }

    private async Task<string?> TryGetDataFileAsync(string version, string fileName, CancellationToken ct)
    {
        try { return await GetDataFileAsync(version, fileName, ct).ConfigureAwait(false); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    /// <summary>
    /// Busca el archivo cacheado probando los idiomas configurados en orden. El caché
    /// viejo (archivos directamente bajo la carpeta de versión, sin idioma) se ignora:
    /// así un cambio de idioma fuerza la re-descarga en vez de servir datos en otro idioma.
    /// </summary>
    private async Task<string?> ReadCachedAsync(string version, string fileName, CancellationToken ct)
    {
        foreach (var locale in _locales)
        {
            var path = Path.Combine(_cacheDir, version, locale, fileName);
            if (File.Exists(path))
                return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        return null;
    }

    private string? LatestCachedVersion()
    {
        if (!Directory.Exists(_cacheDir))
            return null;
        return Directory.GetDirectories(_cacheDir)
            .Where(dir => _locales.Any(loc => File.Exists(Path.Combine(dir, loc, "item.json"))))
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private async Task<string> GetDataFileAsync(string version, string fileName, CancellationToken ct)
    {
        var cached = await ReadCachedAsync(version, fileName, ct).ConfigureAwait(false);
        if (cached is not null)
            return cached;

        var (locale, json) = await DownloadWithLocaleFallbackAsync(version, fileName, ct).ConfigureAwait(false);
        var dir = Path.Combine(_cacheDir, version, locale);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, fileName), json, ct).ConfigureAwait(false);
        return json;
    }

    private async Task<(string Locale, string Json)> DownloadWithLocaleFallbackAsync(
        string version, string fileName, CancellationToken ct)
    {
        HttpRequestException? last = null;
        foreach (var locale in _locales)
        {
            try
            {
                var url = $"{Base}/cdn/{version}/data/{locale}/{fileName}";
                var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
                return (locale, json);
            }
            catch (HttpRequestException ex)
            {
                last = ex; // idioma no disponible: probar el siguiente
            }
        }
        throw last ?? new HttpRequestException($"Could not download {fileName}.");
    }
}
