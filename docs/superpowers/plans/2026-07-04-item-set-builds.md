# Item Set Builds Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Componer las 3 mejores builds desde las stats de OP.GG y escribirlas automáticamente como páginas de items en el cliente de LoL (tienda in-game), ampliando de paso el prior del asesor con el 6.º slot.

**Architecture:** Parser gana `FourthItems`/`FifthItems`/`SixthItems` por slot (el builder los necesita separados; `LateItems` sigue siendo la lista plana para el prior e incluye ahora el 6.º). `ItemSetBuilder` (puro) compone hasta 3 `ItemSetPage` con bloques Starter/Boots/Core/4.º/5.º/6.º. `LcuItemSetWriter` funde las páginas en el documento de item sets del invocador preservando las ajenas (prefijo `Paradox: ` identifica las nuestras; sin GET exitoso no hay PUT). `MainViewModel` dispara en champ select (principal) y al cargar stats en vivo (refuerzo), deduplicado por campeón+mapa+parche.

**Tech Stack:** C#/.NET 10, WPF, xUnit, System.Text.Json.Nodes, LCU (lockfile + basic auth).

**Branch:** trabajar en `feature/item-set-builds` desde `main`.

**Verified assumptions (probes reales 2026-07-04):**
- `data.sixth_items[].{ids[],ids_names[],pick_rate,play,win}` responde con la misma forma compacta que fourth/fifth. Texto real: `SixthItem([3026],["Guardian Angel"],0.21,181,109)`.
- LCU item sets: `GET /lol-summoner/v1/current-summoner` → `summonerId`; `GET|PUT /lol-item-sets/v1/item-sets/{summonerId}/sets` con documento `{ accountId, timestamp, itemSets: [...] }`; `items[].id` es STRING.

---

### Task 0: Branch

- [ ] **Step 1:**

```bash
git checkout -b feature/item-set-builds
```

---

### Task 1: Parser por slot + sixth_items en el cliente

**Files:**
- Modify: `src/ParadoxLoLCompanion.Core/Stats/ChampionBuildStats.cs`
- Modify: `src/ParadoxLoLCompanion.Core/Stats/OpggResponseParser.cs`
- Modify: `src/ParadoxLoLCompanion.Core/Stats/OpggMcpClient.cs` (DesiredFields)
- Test: `tests/ParadoxLoLCompanion.Tests/OpggResponseParserTests.cs`, `tests/ParadoxLoLCompanion.Tests/OpggMcpClientTests.cs`

- [ ] **Step 1: Write the failing tests**

En `OpggResponseParserTests.cs` agregar (el texto compacto es respuesta REAL del tool, verbatim del probe):

```csharp
    [Fact]
    public void Parses_sixth_items_and_flattens_them_into_late()
    {
        const string text = """
            class LolGetChampionAnalysis: data
            class Data: sixth_items,starter_items
            class SixthItem: ids,ids_names,pick_rate,play,win

            LolGetChampionAnalysis(Data([SixthItem([3026],["Guardian Angel"],0.21,181,109),SixthItem([3142],["Youmuu's Ghostblade"],0.11,90,51),SixthItem([6695],["Serpent's Fang"],0.11,90,43)],SixthItem([1055,2003],["Doran's Blade","Health Potion"],0.91,90004,44040)))
            """;
        var stats = OpggResponseParser.Parse(text, "Jayce", "ranked", "top")!;
        Assert.Equal(3, stats.SixthItems.Count);
        Assert.Equal(new[] { 3026 }, stats.SixthItems[0].ItemIds);
        Assert.Equal(0.21, stats.SixthItems[0].PickRate, 2);
        // El prior también los ve: LateItems aplana 4.º+5.º+6.º.
        Assert.NotNull(stats.ItemPriorFor(3026));
        Assert.False(stats.ItemPriorFor(3026)!.Value.IsCore);
    }

    [Fact]
    public void Splits_late_items_by_slot()
    {
        var stats = ParseFixture();
        // El fixture trae 3 candidatos de 4.º y 3 de 5.º; sixth no estaba grabado.
        Assert.Equal(3, stats.FourthItems.Count);
        Assert.Equal(3, stats.FifthItems.Count);
        Assert.Empty(stats.SixthItems);
        Assert.Equal(stats.FourthItems.Count + stats.FifthItems.Count, stats.LateItems.Count);
    }
```

En `OpggMcpClientTests.Builds_a_valid_tools_call_payload`, agregar al final:

```csharp
        Assert.Contains(fields, f => f.StartsWith("data.sixth_items"));
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests/ParadoxLoLCompanion.Tests.csproj --filter "OpggResponseParserTests|OpggMcpClientTests"`
Expected: FAIL — `SixthItems`/`FourthItems` no existen; falta el desired field.

- [ ] **Step 3: Implement**

`ChampionBuildStats.cs` — reemplazar la propiedad `LateItems` por las cuatro:

```csharp
    /// <summary>Candidatos del 4.º item (sets de un solo item, rankeados por OP.GG).</summary>
    public IReadOnlyList<ItemSetStats> FourthItems { get; init; } = Array.Empty<ItemSetStats>();
    /// <summary>Candidatos del 5.º item.</summary>
    public IReadOnlyList<ItemSetStats> FifthItems { get; init; } = Array.Empty<ItemSetStats>();
    /// <summary>Candidatos del 6.º item (muestras chicas: la rampa de confianza los modera).</summary>
    public IReadOnlyList<ItemSetStats> SixthItems { get; init; } = Array.Empty<ItemSetStats>();
    /// <summary>
    /// Candidatos tardíos aplanados (4.º+5.º+6.º) para el prior del asesor. Se
    /// persiste en la caché (compatibilidad con entradas viejas que solo traían esto).
    /// </summary>
    public IReadOnlyList<ItemSetStats> LateItems { get; init; } = Array.Empty<ItemSetStats>();
```

`OpggResponseParser.cs` — en el objeto que devuelve `Parse`, reemplazar la asignación de `LateItems`:

```csharp
            FourthItems = data.ObjList("fourth_items")
                .Select(ItemSet).Where(s => s is not null).Select(s => s!).ToList(),
            FifthItems = data.ObjList("fifth_items")
                .Select(ItemSet).Where(s => s is not null).Select(s => s!).ToList(),
            SixthItems = data.ObjList("sixth_items")
                .Select(ItemSet).Where(s => s is not null).Select(s => s!).ToList(),
            LateItems = data.ObjList("fourth_items").Concat(data.ObjList("fifth_items"))
                .Concat(data.ObjList("sixth_items"))
                .Select(ItemSet).Where(s => s is not null).Select(s => s!).ToList(),
```

`OpggMcpClient.cs` — en `DesiredFields`, después de la línea de `fifth_items`:

```csharp
        "data.sixth_items[].{ids[],ids_names[],pick_rate,play,win}",
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests/ParadoxLoLCompanion.Tests.csproj`
Expected: ALL PASS (las entradas viejas de caché siguen deserializando: `LateItems` sigue siendo propiedad almacenada).

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.Core/Stats/ tests/ParadoxLoLCompanion.Tests/
git commit -m "feat: per-slot late item lists + sixth_items in OP.GG stats"
```

---

### Task 2: `ItemSetBuilder`

**Files:**
- Create: `src/ParadoxLoLCompanion.Core/Stats/ItemSetBuilder.cs`
- Test: `tests/ParadoxLoLCompanion.Tests/ItemSetBuilderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

public class ItemSetBuilderTests
{
    private static ItemSetStats Set(double pick, int play, params int[] ids) =>
        new(ids, pick, play, (int)(play * 0.52));

    private static ChampionBuildStats FullStats() => new()
    {
        ChampionKey = "Jayce",
        GameMode = "ranked",
        Position = "top",
        Starter = Set(0.91, 90004, 1055, 2003),
        Boots = Set(0.36, 32517, 3158),
        CoreItems = Set(0.17, 11228, 3070, 3142, 3042),
        FourthItems = new[] { Set(0.30, 10673, 6694), Set(0.18, 6321, 3814), Set(0.12, 4261, 6699) },
        FifthItems = new[] { Set(0.22, 2521, 3814), Set(0.14, 1687, 6694) },
        SixthItems = new[] { Set(0.21, 181, 3026) },
    };

    [Fact]
    public void Builds_three_variants_with_all_blocks()
    {
        var pages = ItemSetBuilder.Build(FullStats(), "Jayce");
        Assert.Equal(3, pages.Count);
        Assert.Equal("Paradox: Jayce #1", pages[0].Title);
        Assert.Equal(
            new[] { "Starter", "Boots", "Core build", "4th item", "5th item", "6th item" },
            pages[0].Blocks.Select(b => b.Title).ToArray());
        Assert.Equal(new[] { 3070, 3142, 3042 }, pages[0].Blocks[2].ItemIds);
        // Variante i usa el candidato i-ésimo de cada slot.
        Assert.Equal(new[] { 6694 }, pages[0].Blocks[3].ItemIds);
        Assert.Equal(new[] { 3814 }, pages[1].Blocks[3].ItemIds);
        Assert.Equal(new[] { 6699 }, pages[2].Blocks[3].ItemIds);
        // Slot corto cae al último disponible (5.º solo tiene 2 candidatos).
        Assert.Equal(new[] { 6694 }, pages[2].Blocks[4].ItemIds);
    }

    [Fact]
    public void Skips_missing_blocks_and_dedupes_variants()
    {
        // Solo core + un candidato de 4.º: las variantes 2 y 3 serían idénticas → 1 página.
        var stats = new ChampionBuildStats
        {
            ChampionKey = "Test",
            CoreItems = Set(0.2, 9000, 1, 2, 3),
            FourthItems = new[] { Set(0.3, 8000, 4) },
        };
        var pages = ItemSetBuilder.Build(stats, "Test");
        Assert.Single(pages);
        Assert.Equal(new[] { "Core build", "4th item" }, pages[0].Blocks.Select(b => b.Title).ToArray());
    }

    [Fact]
    public void No_core_no_pages()
    {
        var stats = new ChampionBuildStats
        {
            ChampionKey = "Test",
            FourthItems = new[] { Set(0.3, 8000, 4) },
        };
        Assert.Empty(ItemSetBuilder.Build(stats, "Test"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests/ParadoxLoLCompanion.Tests.csproj --filter ItemSetBuilderTests`
Expected: FAIL — `ItemSetBuilder` no existe.

- [ ] **Step 3: Implement**

`src/ParadoxLoLCompanion.Core/Stats/ItemSetBuilder.cs`:

```csharp
namespace ParadoxLoLCompanion.Core.Stats;

/// <summary>Bloque de una página de items (sección de la tienda).</summary>
public sealed record ItemSetBlock(string Title, IReadOnlyList<int> ItemIds);

/// <summary>Página de items lista para escribir en el cliente (modelo puro, sin JSON).</summary>
public sealed record ItemSetPage(string Title, IReadOnlyList<ItemSetBlock> Blocks);

/// <summary>
/// Compone hasta 3 variantes del meta desde las stats de OP.GG: core fijo +
/// candidato i-ésimo de cada slot tardío. Variantes duplicadas se descartan
/// (campeones con pocos candidatos emiten menos de 3 páginas).
/// </summary>
public static class ItemSetBuilder
{
    /// <summary>Prefijo que identifica nuestras páginas al reemplazarlas en el cliente.</summary>
    public const string TitlePrefix = "Paradox: ";
    private const int MaxVariants = 3;

    public static IReadOnlyList<ItemSetPage> Build(ChampionBuildStats stats, string championName)
    {
        if (stats.CoreItems is not { } core || core.ItemIds.Count == 0)
            return Array.Empty<ItemSetPage>();

        var pages = new List<ItemSetPage>();
        var seen = new HashSet<string>();
        for (var i = 0; i < MaxVariants; i++)
        {
            var blocks = new List<ItemSetBlock>();
            Add(blocks, "Starter", stats.Starter?.ItemIds);
            Add(blocks, "Boots", stats.Boots?.ItemIds);
            Add(blocks, "Core build", core.ItemIds);
            Add(blocks, "4th item", Candidate(stats.FourthItems, i));
            Add(blocks, "5th item", Candidate(stats.FifthItems, i));
            Add(blocks, "6th item", Candidate(stats.SixthItems, i));

            var signature = string.Join("|",
                blocks.Select(b => b.Title + ":" + string.Join(",", b.ItemIds)));
            if (!seen.Add(signature))
                continue;
            pages.Add(new ItemSetPage($"{TitlePrefix}{championName} #{pages.Count + 1}", blocks));
        }
        return pages;
    }

    /// <summary>Candidato i-ésimo del slot; si el slot tiene menos, cae al último disponible.</summary>
    private static IReadOnlyList<int>? Candidate(IReadOnlyList<ItemSetStats> sets, int index) =>
        sets.Count == 0 ? null : sets[Math.Min(index, sets.Count - 1)].ItemIds;

    private static void Add(List<ItemSetBlock> blocks, string title, IReadOnlyList<int>? ids)
    {
        if (ids is { Count: > 0 })
            blocks.Add(new ItemSetBlock(title, ids));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests/ParadoxLoLCompanion.Tests.csproj --filter ItemSetBuilderTests`
Expected: 3 PASS

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.Core/Stats/ItemSetBuilder.cs tests/ParadoxLoLCompanion.Tests/ItemSetBuilderTests.cs
git commit -m "feat: ItemSetBuilder composes 3 meta build variants as item pages"
```

---

### Task 3: `LcuItemSetWriter`

**Files:**
- Create: `src/ParadoxLoLCompanion.Core/Connectors/Lcu/LcuItemSetWriter.cs`
- Test: `tests/ParadoxLoLCompanion.Tests/LcuItemSetWriterTests.cs`

- [ ] **Step 1: Write the failing tests (merge + payload, sin red)**

```csharp
using System.Text.Json;
using ParadoxLoLCompanion.Core.Connectors.Lcu;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

public class LcuItemSetWriterTests
{
    private static ItemSetPage Page(string title) => new(title, new[]
    {
        new ItemSetBlock("Core build", new[] { 3070, 3142 }),
        new ItemSetBlock("4th item", new[] { 6694 }),
    });

    [Fact]
    public void Merge_preserves_foreign_sets_and_replaces_ours()
    {
        const string current = """
            {"accountId":42,"timestamp":123,"itemSets":[
              {"title":"Mi página","uid":"u1","blocks":[{"type":"x","items":[{"id":"1001","count":1}]}]},
              {"title":"Paradox: Jayce #1","uid":"u2","blocks":[]}
            ]}
            """;
        var merged = LcuItemSetWriter.MergeSets(current, new[] { Page("Paradox: Jayce #1") }, 126, 11)!;
        using var doc = JsonDocument.Parse(merged);
        var sets = doc.RootElement.GetProperty("itemSets").EnumerateArray().ToList();
        Assert.Equal(2, sets.Count);
        // La ajena sobrevive INTACTA (mismo uid); la vieja nuestra desapareció.
        Assert.Equal("Mi página", sets[0].GetProperty("title").GetString());
        Assert.Equal("u1", sets[0].GetProperty("uid").GetString());
        Assert.Equal("Paradox: Jayce #1", sets[1].GetProperty("title").GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("accountId").GetInt64());
    }

    [Fact]
    public void Set_node_has_the_lcu_shape()
    {
        var node = LcuItemSetWriter.BuildSetNode(Page("Paradox: Jayce #2"), 126, 12);
        using var doc = JsonDocument.Parse(node.ToJsonString());
        var root = doc.RootElement;
        Assert.Equal("Paradox: Jayce #2", root.GetProperty("title").GetString());
        Assert.Equal(126, root.GetProperty("associatedChampions")[0].GetInt32());
        Assert.Equal(12, root.GetProperty("associatedMaps")[0].GetInt32());
        var block = root.GetProperty("blocks")[0];
        Assert.Equal("Core build", block.GetProperty("type").GetString());
        // La LCU espera el id del item como STRING.
        Assert.Equal("3070", block.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(1, block.GetProperty("items")[0].GetProperty("count").GetInt32());
    }

    [Fact]
    public void Unparseable_document_returns_null_never_overwrites()
    {
        Assert.Null(LcuItemSetWriter.MergeSets("not json", new[] { Page("Paradox: X #1") }, 1, 11));
        Assert.Null(LcuItemSetWriter.MergeSets("[1,2,3]", new[] { Page("Paradox: X #1") }, 1, 11));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests/ParadoxLoLCompanion.Tests.csproj --filter LcuItemSetWriterTests`
Expected: FAIL — `LcuItemSetWriter` no existe.

- [ ] **Step 3: Implement**

`src/ParadoxLoLCompanion.Core/Connectors/Lcu/LcuItemSetWriter.cs`:

```csharp
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ParadoxLoLCompanion.Core.Net;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Core.Connectors.Lcu;

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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests/ParadoxLoLCompanion.Tests.csproj --filter LcuItemSetWriterTests`
Expected: 3 PASS

- [ ] **Step 5: Commit**

```bash
git add src/ParadoxLoLCompanion.Core/Connectors/Lcu/LcuItemSetWriter.cs tests/ParadoxLoLCompanion.Tests/LcuItemSetWriterTests.cs
git commit -m "feat: LcuItemSetWriter merges Paradox pages into client item sets"
```

---

### Task 4: Disparadores en `MainViewModel`

**Files:**
- Modify: `src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs`

Sin tests nuevos (glue fino; la lógica vive en Core). Steps:

- [ ] **Step 1: Campos**

Junto a `_runeWriter`:

```csharp
    private readonly LcuItemSetWriter _itemSetWriter = new();
    private string? _itemSetsWrittenKey;   // "champKey|map|patch": una escritura por contexto
```

- [ ] **Step 2: Escribir al llegar stats (refuerzo en vivo + champ select comparten esto)**

En `FetchStatsAsync`, dentro del bloque `OnUi(...)` de éxito, después de `RebuildRunesPanel();` y antes del `if (_lastGameState...)`:

```csharp
            WriteItemSetsIfNeeded(stats);
```

Métodos nuevos (cerca de `RebuildRunesPanel`):

```csharp
    /// <summary>
    /// Escribe las 3 páginas de items en el cliente (automático). Deduplicado por
    /// campeón+mapa+parche; si falla se limpia la clave para reintentar en el
    /// próximo disparo natural (champ select o partida).
    /// </summary>
    private void WriteItemSetsIfNeeded(ChampionBuildStats? stats)
    {
        if (stats is null || _catalog.ChampionByKey(stats.ChampionKey) is not { } champ)
            return;
        var mapNumber = stats.GameMode == "aram" ? 12 : 11;
        var key = $"{stats.ChampionKey}|{mapNumber}|{_catalog.Version}";
        if (key == _itemSetsWrittenKey)
            return;
        _itemSetsWrittenKey = key;
        var pages = ItemSetBuilder.Build(stats, champ.Name);
        if (pages.Count == 0)
            return;
        _ = ApplyItemSetsAsync(pages, champ, mapNumber, key);
    }

    private async Task ApplyItemSetsAsync(IReadOnlyList<ItemSetPage> pages,
        StaticChampion champ, int mapNumber, string key)
    {
        var error = await _itemSetWriter.ApplyAsync(pages, champ.Id, mapNumber).ConfigureAwait(false);
        OnUi(() =>
        {
            if (error is null)
            {
                AppendConsole($"[builds] {pages.Count} item pages written for {champ.Name}.");
                return;
            }
            if (_itemSetsWrittenKey == key)
                _itemSetsWrittenKey = null;
            AppendConsole($"[builds] item pages not written: {error}");
        });
    }
```

- [ ] **Step 3: Disparador de champ select (el principal: el juego lee los sets al arrancar)**

En `PopulateChampSelect`, al final (después de `RebuildBenchAdvice(session);`):

```csharp
        RequestStatsFromChampSelect(session);
```

Método nuevo (junto a `RequestStatsIfNeeded`):

```csharp
    /// <summary>
    /// Champ select: el campeón del jugador local dispara el fetch de stats (y con
    /// él las páginas de items) ANTES de que arranque la partida. En ARAM la banca
    /// cambia el campeón: cada swap re-dispara con el nuevo.
    /// </summary>
    private void RequestStatsFromChampSelect(ChampSelectSession session)
    {
        if (!_catalog.IsLoaded)
            return;
        var me = session.MyTeam.FirstOrDefault(c => c.CellId == session.LocalPlayerCellId);
        if (me is null || me.DisplayChampionId == 0)
            return;
        var champ = _catalog.ChampionById(me.DisplayChampionId);
        if (champ is null)
            return;
        var isAram = _currentQueueId == 450 || _config.Mayhem.QueueIds.Contains(_currentQueueId);
        var mapNumber = isAram ? 12 : 11;
        var key = $"{champ.Key}|{me.AssignedPosition.ToUpperInvariant()}|{mapNumber}|{_catalog.Version}";
        if (key == _statsFetchKey)
            return;
        _statsFetchKey = key;
        _championStats = null;
        RebuildRunesPanel();
        _ = FetchStatsAsync(champ.Key, me.AssignedPosition, mapNumber, _catalog.Version, key);
    }
```

- [ ] **Step 4: Normalizar la clave del flujo en vivo (evita doble fetch al pasar de champ select a partida)**

En `RequestStatsIfNeeded`, cambiar la línea del key:

```csharp
        var key = $"{champ.Key}|{me.Position.ToUpperInvariant()}|{mapNumber}|{_catalog.Version}";
```

(La `AssignedPosition` de champ select es "middle" y la `Position` en vivo "MIDDLE": con `ToUpperInvariant` en ambos, la clave coincide y no se re-pide.)

- [ ] **Step 5: Build**

Run: `dotnet build src/ParadoxLoLCompanion.App/ParadoxLoLCompanion.App.csproj`
Expected: Build succeeded (si `ChampSelectCell.DisplayChampionId`/`CellId` no existen con esos nombres, verificar `src/ParadoxLoLCompanion.Core/Models/ChampSelectSession.cs` — son los que usa `PopulateChampSelect`).

- [ ] **Step 6: Commit**

```bash
git add src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs
git commit -m "feat: auto-write item set pages on champ select and live stats"
```

---

### Task 5: Verificación completa

- [ ] **Step 1: Suite completa**

Run: `dotnet test tests/ParadoxLoLCompanion.Tests/ParadoxLoLCompanion.Tests.csproj`
Expected: ALL PASS

- [ ] **Step 2: Smoke con replay (sin cliente de LoL)**

Lanzar la app, activar Replay: la consola debe mostrar `[stats] OP.GG build data loaded...` seguido de `[builds] item pages not written: LoL client is not running.` — el pipeline entero corre y degrada limpio sin cliente.

- [ ] **Step 3: Smoke real (con cliente de LoL abierto, manual)**

Con el cliente abierto y un lobby/champ select real: verificar en consola `[builds] 3 item pages written for <champ>.` y en el cliente (Colección → Items) las páginas `Paradox: <champ> #1..#3`. Verificar que las páginas propias del usuario siguen intactas.

- [ ] **Step 4: Merge según prefiera el usuario (finishing-a-development-branch)**

---

## Self-review notes (already applied)

- **Spec coverage:** parser por slot + sixth (Task 1), builder 3 variantes con dedupe y fallback (Task 2), escritor con preservación de ajenos y regla sin-GET-no-PUT (Task 3), disparador champ select + refuerzo vivo + dedup + logs (Task 4), prior ampliado vía LateItems que ahora incluye sixth (Task 1, `ItemPriorFor` sin cambios — ya escanea LateItems).
- **Type consistency:** `ItemSetPage`/`ItemSetBlock` definidos en Task 2 y usados en Tasks 3-4; `ItemSetBuilder.TitlePrefix` consumido por el writer; `ChampionBuildStats.FourthItems/FifthItems/SixthItems` definidos en Task 1 y usados en Task 2.
- **Data reality:** forma de `sixth_items` verificada con probe real (muestras 90-181: la rampa de confianza del prior ya los modera; para las PÁGINAS el orden del slot es la señal correcta).
- **Cache compat:** `LateItems` sigue siendo propiedad almacenada — entradas viejas de caché deserializan igual; las nuevas traen también los slots separados.
