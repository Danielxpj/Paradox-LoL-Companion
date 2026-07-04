using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoLAdvisor.Core.Net;
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Core.Connectors.Lcu;

/// <summary>
/// Escribe las páginas de items en el cliente de LoL vía LCU. Preserva las
/// páginas del usuario: solo reemplaza las que empiezan con el prefijo propio,
/// y si no puede LEER el documento actual no escribe nada (el PUT reemplaza
/// todo). Nunca lanza: null = éxito, string = motivo para el log.
/// </summary>
public sealed class LcuItemSetWriter
{
    private const string SummonerPath = "/lol-summoner/v1/current-summoner";

    private readonly LockfileLocator _locator;
    private readonly HttpClient _http;

    public LcuItemSetWriter(LockfileLocator? locator = null, HttpClient? http = null)
    {
        _locator = locator ?? new LockfileLocator();
        _http = http ?? LocalHttpClientFactory.Create();
    }

    public async Task<string?> ApplyAsync(IReadOnlyList<ItemSetPage> pages, int championId,
        int mapNumber, CancellationToken ct = default)
    {
        if (pages.Count == 0)
            return "no pages to write";
        var creds = _locator.Locate();
        if (creds is null)
            return "LoL client is not running.";
        try
        {
            long summonerId;
            using (var resp = await SendAsync(creds, HttpMethod.Get, SummonerPath, null, ct).ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode)
                    return $"could not read current summoner (HTTP {(int)resp.StatusCode}).";
                using var doc = JsonDocument.Parse(
                    await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                summonerId = doc.RootElement.GetProperty("summonerId").GetInt64();
            }

            var setsPath = $"/lol-item-sets/v1/item-sets/{summonerId}/sets";
            string current;
            using (var resp = await SendAsync(creds, HttpMethod.Get, setsPath, null, ct).ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode)
                    return $"could not read current item sets (HTTP {(int)resp.StatusCode}).";
                current = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }

            var merged = MergeSets(current, pages, championId, mapNumber);
            if (merged is null)
                return "current item sets document did not parse — not overwriting it.";

            using var putResp = await SendAsync(creds, HttpMethod.Put, setsPath, merged, ct).ConfigureAwait(false);
            return putResp.IsSuccessStatusCode
                ? null
                : $"client rejected the item sets (HTTP {(int)putResp.StatusCode}).";
        }
        catch (Exception ex)
        {
            return $"could not write item sets: {ex.Message}";
        }
    }

    /// <summary>
    /// Documento nuevo: conserva intactos (mismo JSON, mismo uid) los sets ajenos,
    /// quita los nuestros previos y agrega las páginas nuevas al final.
    /// </summary>
    internal static string? MergeSets(string currentJson, IReadOnlyList<ItemSetPage> pages,
        int championId, int mapNumber)
    {
        try
        {
            if (JsonNode.Parse(currentJson) is not JsonObject root)
                return null;
            var sets = root["itemSets"] as JsonArray ?? new JsonArray();
            var result = new JsonArray();
            foreach (var set in sets)
            {
                var title = set?["title"]?.GetValue<string>() ?? "";
                if (!title.StartsWith(ItemSetBuilder.TitlePrefix, StringComparison.Ordinal))
                    result.Add(set!.DeepClone());
            }
            foreach (var page in pages)
                result.Add(BuildSetNode(page, championId, mapNumber));
            root["itemSets"] = result;
            return root.ToJsonString();
        }
        catch
        {
            return null;
        }
    }

    internal static JsonObject BuildSetNode(ItemSetPage page, int championId, int mapNumber) =>
        new()
        {
            ["title"] = page.Title,
            ["type"] = "custom",
            ["map"] = "any",
            ["mode"] = "any",
            ["startedFrom"] = "blank",
            ["sortrank"] = 0,
            ["associatedChampions"] = new JsonArray(championId),
            ["associatedMaps"] = new JsonArray(mapNumber),
            ["preferredItemSlots"] = new JsonArray(),
            ["blocks"] = new JsonArray(page.Blocks.Select(b => (JsonNode)new JsonObject
            {
                ["type"] = b.Title,
                ["hideIfSummonerSpell"] = "",
                ["showIfSummonerSpell"] = "",
                // La LCU espera el id del item como string.
                ["items"] = new JsonArray(b.ItemIds.Select(id => (JsonNode)new JsonObject
                {
                    ["id"] = id.ToString(),
                    ["count"] = 1,
                }).ToArray()),
            }).ToArray()),
        };

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
