using System.Text.Json;

namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>
/// Nombres localizados de augments desde CommunityDragon (arena/*.json). Solo
/// cubre los augments compartidos con Arena; los exclusivos de Mayhem quedan
/// sin alias y se matchean por el nombre en inglés de Blitz. El join es por
/// nombre en inglés: los ids de Blitz NO coinciden con los de cdragon
/// (verificado 2026-07-17: Eureka es 1030 en Blitz y 30 en cdragon).
/// </summary>
public sealed class CdragonAugmentNames
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly HttpClient _http;

    public CdragonAugmentNames(HttpClient? http = null) => _http = http ?? SharedHttp;

    /// <summary>nombre_en (minúsculas) → nombre localizado. Vacío ante cualquier fallo.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetAliasesAsync(
        string locale, CancellationToken ct = default)
    {
        try
        {
            var en = ParseNames(await FetchAsync("en_us", ct).ConfigureAwait(false));
            var localized = ParseNames(await FetchAsync(locale, ct).ConfigureAwait(false));
            var aliases = new Dictionary<string, string>();
            foreach (var (id, enName) in en)
                if (localized.TryGetValue(id, out var locName)
                    && enName.Length > 0 && locName.Length > 0
                    && !string.Equals(enName, locName, StringComparison.OrdinalIgnoreCase))
                    aliases[enName.ToLowerInvariant()] = locName;
            return aliases;
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>id → name de un arena/*.json de cdragon.</summary>
    internal static Dictionary<int, string> ParseNames(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<int, string>();
        foreach (var augment in doc.RootElement.GetProperty("augments").EnumerateArray())
            if (augment.TryGetProperty("id", out var id)
                && augment.TryGetProperty("name", out var name))
                result[id.GetInt32()] = name.GetString() ?? "";
        return result;
    }

    private Task<string> FetchAsync(string locale, CancellationToken ct) =>
        _http.GetStringAsync(
            $"https://raw.communitydragon.org/latest/cdragon/arena/{locale}.json", ct);
}
