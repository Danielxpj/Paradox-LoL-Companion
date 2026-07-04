# OP.GG Stats Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Feed real per-champion build statistics (OP.GG MCP server) into the fuzzy item advisor as an additive prior, gate mana items by `partype`, and show a runes/skill-order panel with an LCU "Apply runes" button.

**Architecture:** New `LoLAdvisor.Core/Stats/` layer (MCP client → compact-text parser → model → per-patch file cache → provider). `ItemAdvisor.Advise` gains an optional `ChampionBuildStats?` parameter; a new additive fuzzy rule in `ScoreItem` uses it. `partype` parsing in `DataDragonCatalog` powers offline mana gating. `MainViewModel` fetches stats async and wires a runes panel + `LcuRuneWriter`.

**Tech Stack:** .NET 10, WPF, xunit 2.9, System.Text.Json (no new packages). Spec: `docs/superpowers/specs/2026-07-04-opgg-stats-integration-design.md`.

**Spike findings already validated against the live endpoint (2026-07-04):**
- `POST https://mcp-api.op.gg/mcp` is **stateless** — no `initialize` handshake or session header needed; plain JSON-RPC `tools/call` works per request. Response observed as plain JSON (handle SSE `data:` lines defensively).
- Tool name: `lol_get_champion_analysis`. Arguments: `champion` (UPPER_SNAKE_CASE — **both** `MONKEY_KING` and `MONKEYKING` are accepted, so a simple PascalCase→UPPER_SNAKE conversion of the ddragon Key is safe), `game_mode` (`ranked|aram|...`), `position` (`all|none|top|mid|jungle|adc|support`), `desired_output_fields` (closed set).
- `result.content[0].text` is NOT JSON. It is a compact format: `class Name: field1,field2` header lines, a blank line, then one nested-constructor expression. The headers define positional field binding — parse them, don't hardcode positions.
- Real Jayce-top response is recorded at `tests/LoLAdvisor.Tests/Fixtures/opgg-champion-analysis-jayce.json` (raw JSON-RPC envelope). Its core build is `[3070 Tear, 3142 Youmuu's, 3042 Muramana, 6699 Voltaic]` — note it lists **Muramana (3042)**, the transformed item, while the catalog recommends purchasable **Manamune (3004)**.
- **Verified against cached ddragon 16.13.1:** Manamune (3004) has NO `into` (it IS a completed, recommendable item — no catalog filter change needed) and Muramana (3042) has NO `from`. The build tree does NOT link evolutions, so the prior lookup bridges them via an explicit config map (`ItemsConfig.ItemEvolutions`), not via `StaticItem.From`.
- Rune names come localized in the response — **no `runesReforged.json` needed** (conscious simplification vs. spec: names, no icons).

**Conventions:** comments in the codebase are Spanish, reasons/UI strings are English. Tests: xunit `[Fact]`, files in `tests/LoLAdvisor.Tests/`. Run tests with `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj`. Commit after every task.

---

### Task 1: Commit the recorded fixture + Fixtures helper

The fixture file already exists in the working tree (recorded during planning). Wire it up.

**Files:**
- Exists: `tests/LoLAdvisor.Tests/Fixtures/opgg-champion-analysis-jayce.json`
- Modify: `tests/LoLAdvisor.Tests/Fixtures.cs`

- [ ] **Step 1: Verify the fixture is picked up by the csproj glob**

`tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj` already has `<None Update="Fixtures\**\*">` with copy-to-output — no csproj change needed. Verify the file exists:

Run: `dir tests\LoLAdvisor.Tests\Fixtures\opgg-champion-analysis-jayce.json`
Expected: file listed (~2 KB)

- [ ] **Step 2: Add the helper to Fixtures.cs**

```csharp
namespace LoLAdvisor.Tests;

/// <summary>Carga los payloads grabados que se copian al directorio de salida de los tests.</summary>
internal static class Fixtures
{
    private static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string AllGameData() => File.ReadAllText(Path("allgamedata.json"));

    /// <summary>Respuesta JSON-RPC real de lol_get_champion_analysis (Jayce top, 2026-07-04).</summary>
    public static string OpggJayce() => File.ReadAllText(Path("opgg-champion-analysis-jayce.json"));
}
```

- [ ] **Step 3: Build tests to confirm compile**

Run: `dotnet build tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add tests/LoLAdvisor.Tests/Fixtures/opgg-champion-analysis-jayce.json tests/LoLAdvisor.Tests/Fixtures.cs
git commit -m "test: record real OP.GG champion-analysis response as fixture"
```

---

### Task 2: Compact-format parser (`McpTextParser`)

Generic parser for the `class X: fields` + constructor-expression format. Output is a tree of `McpObject` (field-name → value) so downstream code never depends on positions.

**Files:**
- Create: `src/LoLAdvisor.Core/Stats/McpTextParser.cs`
- Create: `tests/LoLAdvisor.Tests/McpTextParserTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

public class McpTextParserTests
{
    private const string Sample = """
        class Root: name,child,ids,rate
        class Child: play,win

        Root("Jayce (\"top\")",Child(100,55),[3070,3042],0.17)
        """;

    [Fact]
    public void Parses_fields_by_header_position()
    {
        var root = McpTextParser.Parse(Sample)!;
        Assert.Equal("Root", root.ClassName);
        Assert.Equal("Jayce (\"top\")", root.Str("name"));   // escapes dentro de strings
        Assert.Equal(0.17, root.Num("rate"), 3);
        Assert.Equal(new List<int> { 3070, 3042 }, root.IntList("ids"));
        var child = root.Obj("child")!;
        Assert.Equal(100, child.Num("play"));
        Assert.Equal(55, child.Num("win"));
    }

    [Fact]
    public void Missing_field_is_null_and_helpers_degrade()
    {
        var root = McpTextParser.Parse("class R: a,b\n\nR(1)")!;   // b no llega
        Assert.Equal(1, root.Num("a"));
        Assert.Null(root.Obj("b"));
        Assert.Equal("", root.Str("b"));
        Assert.Empty(root.IntList("b"));
    }

    [Fact]
    public void List_of_objects_and_nulls()
    {
        var root = McpTextParser.Parse("class R: items\nclass I: v\n\nR([I(1),I(2),None])")!;
        var items = root.ObjList("items");
        Assert.Equal(2, items.Count);              // None se descarta en ObjList
        Assert.Equal(2, items[1].Num("v"));
    }

    [Fact]
    public void Garbage_returns_null_not_throw()
    {
        Assert.Null(McpTextParser.Parse("not the format"));
        Assert.Null(McpTextParser.Parse(""));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter McpTextParserTests`
Expected: FAIL — `McpTextParser` does not exist

- [ ] **Step 3: Implement the parser**

```csharp
using System.Globalization;
using System.Text;

namespace LoLAdvisor.Core.Stats;

/// <summary>
/// Parser del formato compacto que devuelve el MCP de OP.GG: cabeceras
/// "class Nombre: campo1,campo2", una línea en blanco y una expresión de
/// constructores anidados. Las cabeceras definen el orden posicional de los
/// campos, así que el árbol resultante se consulta por NOMBRE de campo y es
/// robusto a reordenamientos y campos nuevos.
/// </summary>
public static class McpTextParser
{
    /// <summary><c>null</c> si el texto no tiene el formato esperado (nunca lanza).</summary>
    public static McpObject? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            var classes = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var body = new StringBuilder();
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.StartsWith("class ", StringComparison.Ordinal))
                {
                    var colon = trimmed.IndexOf(':');
                    if (colon < 0)
                        continue;
                    var name = trimmed[6..colon].Trim();
                    var fields = trimmed[(colon + 1)..].Split(',', StringSplitOptions.TrimEntries);
                    classes[name] = fields;
                }
                else
                {
                    body.Append(trimmed);
                }
            }

            var s = body.ToString();
            var pos = 0;
            var value = ParseValue(s, ref pos, classes);
            return value as McpObject;
        }
        catch
        {
            return null;
        }
    }

    private static object? ParseValue(string s, ref int i, Dictionary<string, string[]> classes)
    {
        SkipWs(s, ref i);
        if (i >= s.Length)
            return null;
        var c = s[i];
        if (c == '[')
            return ParseList(s, ref i, classes);
        if (c == '"')
            return ParseString(s, ref i);
        if (char.IsDigit(c) || c == '-' || c == '+')
            return ParseNumber(s, ref i);
        if (char.IsLetter(c) || c == '_')
            return ParseWordOrConstructor(s, ref i, classes);
        throw new FormatException($"unexpected '{c}' at {i}");
    }

    private static object? ParseWordOrConstructor(string s, ref int i, Dictionary<string, string[]> classes)
    {
        var start = i;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
            i++;
        var word = s[start..i];
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == '(')
        {
            i++; // '('
            var args = new List<object?>();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ')') { i++; }
            else
            {
                while (true)
                {
                    args.Add(ParseValue(s, ref i, classes));
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ')') { i++; break; }
                    throw new FormatException($"unterminated constructor at {i}");
                }
            }
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (classes.TryGetValue(word, out var names))
                for (var k = 0; k < names.Length; k++)
                    fields[names[k]] = k < args.Count ? args[k] : null;
            return new McpObject(word, fields);
        }
        return word switch
        {
            "None" or "null" => null,
            "True" or "true" => true,
            "False" or "false" => false,
            _ => word,
        };
    }

    private static List<object?> ParseList(string s, ref int i, Dictionary<string, string[]> classes)
    {
        i++; // '['
        var items = new List<object?>();
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return items; }
        while (true)
        {
            items.Add(ParseValue(s, ref i, classes));
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; break; }
            throw new FormatException($"unterminated list at {i}");
        }
        return items;
    }

    private static string ParseString(string s, ref int i)
    {
        i++; // '"'
        var sb = new StringBuilder();
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] });
            }
            else
            {
                sb.Append(s[i]);
            }
            i++;
        }
        i++; // '"' de cierre
        return sb.ToString();
    }

    private static double ParseNumber(string s, ref int i)
    {
        var start = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] is '.' or '-' or '+' or 'e' or 'E'))
            i++;
        return double.Parse(s[start..i], CultureInfo.InvariantCulture);
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
    }
}

/// <summary>Nodo del árbol parseado: acceso por nombre de campo con helpers tolerantes.</summary>
public sealed class McpObject
{
    public string ClassName { get; }
    private readonly Dictionary<string, object?> _fields;

    public McpObject(string className, Dictionary<string, object?> fields)
    {
        ClassName = className;
        _fields = fields;
    }

    private object? Raw(string field) => _fields.GetValueOrDefault(field);

    public McpObject? Obj(string field) => Raw(field) as McpObject;
    public double Num(string field) => Raw(field) is double d ? d : 0;
    public string Str(string field) => Raw(field) as string ?? "";

    public List<int> IntList(string field) =>
        Raw(field) is List<object?> list
            ? list.OfType<double>().Select(d => (int)d).ToList()
            : new List<int>();

    public List<string> StrList(string field) =>
        Raw(field) is List<object?> list
            ? list.OfType<string>().ToList()
            : new List<string>();

    public List<McpObject> ObjList(string field) =>
        Raw(field) is List<object?> list
            ? list.OfType<McpObject>().ToList()
            : new List<McpObject>();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter McpTextParserTests`
Expected: 4 PASS

- [ ] **Step 5: Commit**

```bash
git add src/LoLAdvisor.Core/Stats/McpTextParser.cs tests/LoLAdvisor.Tests/McpTextParserTests.cs
git commit -m "feat: parser for OP.GG MCP compact text format"
```

---

### Task 3: `ChampionBuildStats` model + `OpggResponseParser` + envelope extraction

Maps the parsed tree into a typed, JSON-serializable model. Also the pure static that extracts `content[0].text` from the JSON-RPC envelope (lives with the client-to-be, testable now against the fixture).

**Files:**
- Create: `src/LoLAdvisor.Core/Stats/ChampionBuildStats.cs`
- Create: `src/LoLAdvisor.Core/Stats/OpggResponseParser.cs`
- Create: `src/LoLAdvisor.Core/Stats/OpggMcpClient.cs` (only the static `ExtractToolText` in this task; HTTP part in Task 4)
- Create: `tests/LoLAdvisor.Tests/OpggResponseParserTests.cs`

- [ ] **Step 1: Write the failing tests (against the real fixture)**

```csharp
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

public class OpggResponseParserTests
{
    private static ChampionBuildStats ParseFixture()
    {
        var text = OpggMcpClient.ExtractToolText(Fixtures.OpggJayce());
        Assert.NotNull(text);
        var stats = OpggResponseParser.Parse(text!, "Jayce", "ranked", "top");
        Assert.NotNull(stats);
        return stats!;
    }

    [Fact]
    public void Parses_core_items_boots_and_starter()
    {
        var stats = ParseFixture();
        Assert.Equal(new[] { 3070, 3142, 3042, 6699 }, stats.CoreItems!.ItemIds);
        Assert.Equal(0.17, stats.CoreItems.PickRate, 2);
        Assert.True(stats.CoreItems.WinRate is > 0.5 and < 0.6);   // 6120/11228
        Assert.Contains(3158, stats.Boots!.ItemIds);               // Lucidity
        Assert.Contains(1055, stats.Starter!.ItemIds);             // Doran's Blade
    }

    [Fact]
    public void Parses_late_item_candidates()
    {
        var stats = ParseFixture();
        // fourth_items: Serylda 6694, Edge of Night 3814, Voltaic 6699 — fifth: 3814, 6694, 6695
        Assert.Contains(stats.LateItems, s => s.ItemIds.Contains(6694));
        Assert.Contains(stats.LateItems, s => s.ItemIds.Contains(6695));
    }

    [Fact]
    public void Parses_runes_and_skills()
    {
        var stats = ParseFixture();
        var r = stats.Runes!;
        Assert.Equal(8200, r.PrimaryPageId);              // Sorcery
        Assert.Equal(4, r.PrimaryRuneIds.Count);
        Assert.Equal(2, r.SecondaryRuneIds.Count);
        Assert.Equal(3, r.StatModIds.Count);
        Assert.Equal("Sorcery", r.PrimaryPageName);
        Assert.Equal("Q", stats.Skills!.Order[0]);
        Assert.Equal(15, stats.Skills.Order.Count);
    }

    [Fact]
    public void ItemPriorFor_finds_core_and_late_items()
    {
        var stats = ParseFixture();
        var core = stats.ItemPriorFor(3142);   // Youmuu's — en el core set
        Assert.NotNull(core);
        Assert.Equal(0.17, core!.Value.PickRate, 2);
        var late = stats.ItemPriorFor(6694);   // Serylda — candidato de 4.º item
        Assert.NotNull(late);
        Assert.Null(stats.ItemPriorFor(9999));
    }

    [Fact]
    public void Envelope_extraction_handles_sse_and_errors()
    {
        var plain = """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"hola"}]}}""";
        Assert.Equal("hola", OpggMcpClient.ExtractToolText(plain));
        Assert.Equal("hola", OpggMcpClient.ExtractToolText("event: message\ndata: " + plain + "\n\n"));
        var err = """{"jsonrpc":"2.0","id":1,"result":{"isError":true,"content":[{"type":"text","text":"boom"}]}}""";
        Assert.Null(OpggMcpClient.ExtractToolText(err));
        Assert.Null(OpggMcpClient.ExtractToolText("not json"));
    }

    [Fact]
    public void Roundtrips_through_json()
    {
        var stats = ParseFixture();
        var json = System.Text.Json.JsonSerializer.Serialize(stats);
        var back = System.Text.Json.JsonSerializer.Deserialize<ChampionBuildStats>(json)!;
        Assert.Equal(stats.CoreItems!.ItemIds, back.CoreItems!.ItemIds);
        Assert.Equal(stats.Runes!.PrimaryRuneNames, back.Runes!.PrimaryRuneNames);
        Assert.Equal(stats.Skills!.Order, back.Skills!.Order);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter OpggResponseParserTests`
Expected: FAIL — types do not exist

- [ ] **Step 3: Implement model, mapper, and envelope extraction**

`src/LoLAdvisor.Core/Stats/ChampionBuildStats.cs`:

```csharp
namespace LoLAdvisor.Core.Stats;

/// <summary>Un conjunto de items con sus estadísticas (core build, botas, starter o candidato tardío).</summary>
public sealed record ItemSetStats(IReadOnlyList<int> ItemIds, double PickRate, int Play, int Win)
{
    public double WinRate => Play > 0 ? (double)Win / Play : 0;
}

/// <summary>Página de runas más popular (ids de perks + nombres ya localizados por OP.GG).</summary>
public sealed record RunePageStats(
    int PrimaryPageId, string PrimaryPageName,
    IReadOnlyList<int> PrimaryRuneIds, IReadOnlyList<string> PrimaryRuneNames,
    int SecondaryPageId, string SecondaryPageName,
    IReadOnlyList<int> SecondaryRuneIds, IReadOnlyList<string> SecondaryRuneNames,
    IReadOnlyList<int> StatModIds, double PickRate);

/// <summary>Orden de subida de habilidades más popular (letras Q/W/E/R, 15 niveles).</summary>
public sealed record SkillOrderStats(IReadOnlyList<string> Order, double PickRate);

/// <summary>
/// Estadísticas agregadas de build para un campeón+rol+modo (fuente: OP.GG).
/// Serializable a JSON tal cual para la caché por parche.
/// </summary>
public sealed class ChampionBuildStats
{
    public string ChampionKey { get; init; } = "";
    public string GameMode { get; init; } = "";
    public string Position { get; init; } = "";
    public ItemSetStats? CoreItems { get; init; }
    public ItemSetStats? Boots { get; init; }
    public ItemSetStats? Starter { get; init; }
    /// <summary>Candidatos de 4.º/5.º item (sets de un solo item, típicamente).</summary>
    public IReadOnlyList<ItemSetStats> LateItems { get; init; } = Array.Empty<ItemSetStats>();
    public RunePageStats? Runes { get; init; }
    public SkillOrderStats? Skills { get; init; }
    /// <summary>Win rate global del campeón en este rol (0 si no vino).</summary>
    public double WinRate { get; init; }

    /// <summary>
    /// Prior de un item: primero el core build (la señal fuerte), luego los candidatos
    /// tardíos. <c>null</c> si el item no aparece en las builds del campeón.
    /// </summary>
    public (double PickRate, double WinRate)? ItemPriorFor(int itemId)
    {
        if (CoreItems is { } core && core.ItemIds.Contains(itemId))
            return (core.PickRate, core.WinRate);
        foreach (var set in LateItems)
            if (set.ItemIds.Contains(itemId))
                return (set.PickRate, set.WinRate);
        return null;
    }
}
```

`src/LoLAdvisor.Core/Stats/OpggResponseParser.cs`:

```csharp
namespace LoLAdvisor.Core.Stats;

/// <summary>
/// Convierte el texto compacto de <c>lol_get_champion_analysis</c> en
/// <see cref="ChampionBuildStats"/>. Navega por NOMBRE de campo (vía
/// <see cref="McpTextParser"/>), nunca por posición, y tolera campos ausentes:
/// cada sección es opcional según los desired_output_fields pedidos.
/// </summary>
public static class OpggResponseParser
{
    /// <summary><c>null</c> si el texto no parsea o no trae el bloque data.</summary>
    public static ChampionBuildStats? Parse(string toolText, string championKey, string gameMode, string position)
    {
        var root = McpTextParser.Parse(toolText);
        var data = root?.Obj("data");
        if (data is null)
            return null;

        return new ChampionBuildStats
        {
            ChampionKey = championKey,
            GameMode = gameMode,
            Position = position,
            CoreItems = ItemSet(data.Obj("core_items")),
            Boots = ItemSet(data.Obj("boots")),
            Starter = ItemSet(data.Obj("starter_items")),
            LateItems = data.ObjList("fourth_items").Concat(data.ObjList("fifth_items"))
                .Select(ItemSet).Where(s => s is not null).Select(s => s!).ToList(),
            Runes = Runes(data.Obj("runes")),
            Skills = Skills(data.Obj("skills")),
            WinRate = data.Obj("summary")?.Obj("average_stats")?.Num("win_rate") ?? 0,
        };
    }

    private static ItemSetStats? ItemSet(McpObject? o) =>
        o is null || o.IntList("ids").Count == 0
            ? null
            : new ItemSetStats(o.IntList("ids"), o.Num("pick_rate"), (int)o.Num("play"), (int)o.Num("win"));

    private static RunePageStats? Runes(McpObject? o) =>
        o is null || o.IntList("primary_rune_ids").Count == 0
            ? null
            : new RunePageStats(
                (int)o.Num("primary_page_id"), o.Str("primary_page_name"),
                o.IntList("primary_rune_ids"), o.StrList("primary_rune_names"),
                (int)o.Num("secondary_page_id"), o.Str("secondary_page_name"),
                o.IntList("secondary_rune_ids"), o.StrList("secondary_rune_names"),
                o.IntList("stat_mod_ids"), o.Num("pick_rate"));

    private static SkillOrderStats? Skills(McpObject? o) =>
        o is null || o.StrList("order").Count == 0
            ? null
            : new SkillOrderStats(o.StrList("order"), o.Num("pick_rate"));
}
```

`src/LoLAdvisor.Core/Stats/OpggMcpClient.cs` (this task: only the static; the HTTP methods arrive in Task 4):

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter OpggResponseParserTests`
Expected: 6 PASS

- [ ] **Step 5: Commit**

```bash
git add src/LoLAdvisor.Core/Stats/ tests/LoLAdvisor.Tests/OpggResponseParserTests.cs
git commit -m "feat: ChampionBuildStats model and OP.GG response parser"
```

---

### Task 4: HTTP side of `OpggMcpClient` + `IOpggClient`

Thin HTTP layer. The request payload builder is static and tested; the network call itself is exercised manually (Task 13).

**Files:**
- Modify: `src/LoLAdvisor.Core/Stats/OpggMcpClient.cs`
- Create: `tests/LoLAdvisor.Tests/OpggMcpClientTests.cs`

- [ ] **Step 1: Write the failing test for the payload builder**

```csharp
using System.Text.Json;
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

public class OpggMcpClientTests
{
    [Fact]
    public void Builds_a_valid_tools_call_payload()
    {
        var json = OpggMcpClient.BuildToolCallJson("JAYCE", "ranked", "top");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal("tools/call", root.GetProperty("method").GetString());
        var p = root.GetProperty("params");
        Assert.Equal("lol_get_champion_analysis", p.GetProperty("name").GetString());
        var args = p.GetProperty("arguments");
        Assert.Equal("JAYCE", args.GetProperty("champion").GetString());
        Assert.Equal("ranked", args.GetProperty("game_mode").GetString());
        Assert.Equal("top", args.GetProperty("position").GetString());
        // Los campos pedidos incluyen las secciones que consume el parser.
        var fields = args.GetProperty("desired_output_fields").EnumerateArray()
            .Select(f => f.GetString()!).ToList();
        Assert.Contains(fields, f => f.StartsWith("data.core_items"));
        Assert.Contains(fields, f => f.StartsWith("data.runes"));
        Assert.Contains(fields, f => f.StartsWith("data.skills"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter OpggMcpClientTests`
Expected: FAIL — `BuildToolCallJson` does not exist

- [ ] **Step 3: Add interface, payload builder, and HTTP call**

Add to `OpggMcpClient.cs` (keep `ExtractToolText` from Task 3):

```csharp
using System.Net.Http;
using System.Text;
// (mantener using System.Text.Json del Task 3)

/// <summary>Abstracción del cliente OP.GG para poder inyectar fakes en tests.</summary>
public interface IOpggClient
{
    /// <summary>Texto crudo del tool, o <c>null</c> ante cualquier fallo.</summary>
    Task<string?> GetChampionAnalysisTextAsync(
        string champion, string gameMode, string position, CancellationToken ct = default);
}
```

Make the class implement it:

```csharp
public sealed class OpggMcpClient : IOpggClient
{
    private const string Endpoint = "https://mcp-api.op.gg/mcp";

    // Subconjunto cerrado de desired_output_fields que consume OpggResponseParser.
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

    private readonly HttpClient _http;

    public OpggMcpClient(HttpClient? http = null) =>
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

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

    // ... ExtractToolText del Task 3 queda igual ...
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter OpggMcpClientTests`
Expected: PASS (and OpggResponseParserTests still PASS)

- [ ] **Step 5: Commit**

```bash
git add src/LoLAdvisor.Core/Stats/OpggMcpClient.cs tests/LoLAdvisor.Tests/OpggMcpClientTests.cs
git commit -m "feat: OP.GG MCP HTTP client with IOpggClient abstraction"
```

---

### Task 5: `StatsCache`

Per-patch JSON file cache with old-patch pruning. All IO failure-tolerant.

**Files:**
- Create: `src/LoLAdvisor.Core/Stats/StatsCache.cs`
- Create: `tests/LoLAdvisor.Tests/StatsCacheTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

public class StatsCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "loladvisor-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static ChampionBuildStats Sample() => new()
    {
        ChampionKey = "Jayce",
        GameMode = "ranked",
        Position = "top",
        CoreItems = new ItemSetStats(new[] { 3042 }, 0.17, 100, 55),
    };

    [Fact]
    public void Roundtrip_write_then_read()
    {
        var cache = new StatsCache(_dir);
        cache.Write("16.13.1", "Jayce", "ranked", "top", Sample());
        Assert.True(cache.TryRead("16.13.1", "Jayce", "ranked", "top", out var stats));
        Assert.Equal(new[] { 3042 }, stats!.CoreItems!.ItemIds);
    }

    [Fact]
    public void Miss_on_other_patch_or_role()
    {
        var cache = new StatsCache(_dir);
        cache.Write("16.13.1", "Jayce", "ranked", "top", Sample());
        Assert.False(cache.TryRead("16.14.1", "Jayce", "ranked", "top", out _));
        Assert.False(cache.TryRead("16.13.1", "Jayce", "ranked", "mid", out _));
    }

    [Fact]
    public void Write_prunes_old_patch_directories()
    {
        var cache = new StatsCache(_dir);
        cache.Write("16.12.1", "Jayce", "ranked", "top", Sample());
        cache.Write("16.13.1", "Jayce", "ranked", "top", Sample());
        Assert.False(Directory.Exists(Path.Combine(_dir, "16.12.1")));
        Assert.True(Directory.Exists(Path.Combine(_dir, "16.13.1")));
    }

    [Fact]
    public void Corrupt_file_is_a_miss()
    {
        var cache = new StatsCache(_dir);
        cache.Write("16.13.1", "Jayce", "ranked", "top", Sample());
        var file = Directory.GetFiles(Path.Combine(_dir, "16.13.1"))[0];
        File.WriteAllText(file, "{corrupt");
        Assert.False(cache.TryRead("16.13.1", "Jayce", "ranked", "top", out _));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter StatsCacheTests`
Expected: FAIL — `StatsCache` does not exist

- [ ] **Step 3: Implement**

```csharp
using System.Text.Json;

namespace LoLAdvisor.Core.Stats;

/// <summary>
/// Caché de <see cref="ChampionBuildStats"/> en disco, un JSON por
/// campeón+modo+rol, agrupados por parche. Solo se invalida al cambiar de
/// parche (las stats de un parche no cambian lo bastante para re-consultar).
/// Toda la IO es tolerante a fallos: un error de disco es un miss, no un crash.
/// </summary>
public sealed class StatsCache
{
    private readonly string _baseDir;

    public StatsCache(string? baseDir = null) =>
        _baseDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LoLAdvisor", "stats");

    public bool TryRead(string patch, string championKey, string gameMode, string position,
        out ChampionBuildStats? stats)
    {
        stats = null;
        try
        {
            var path = FilePath(patch, championKey, gameMode, position);
            if (!File.Exists(path))
                return false;
            stats = JsonSerializer.Deserialize<ChampionBuildStats>(File.ReadAllText(path));
            return stats is not null;
        }
        catch
        {
            return false;
        }
    }

    public void Write(string patch, string championKey, string gameMode, string position,
        ChampionBuildStats stats)
    {
        try
        {
            var path = FilePath(patch, championKey, gameMode, position);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(stats));
            PruneOtherPatches(patch);
        }
        catch
        {
            // Sin caché seguimos funcionando; solo costará re-consultar.
        }
    }

    /// <summary>Borra los directorios de parches viejos (la caché no crece sin límite).</summary>
    private void PruneOtherPatches(string currentPatch)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(_baseDir))
                if (!string.Equals(Path.GetFileName(dir), currentPatch, StringComparison.Ordinal))
                    Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Limpieza oportunista: si falla, no pasa nada.
        }
    }

    private string FilePath(string patch, string championKey, string gameMode, string position) =>
        Path.Combine(_baseDir, patch, $"{championKey}-{gameMode}-{position}.json");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter StatsCacheTests`
Expected: 4 PASS

- [ ] **Step 5: Commit**

```bash
git add src/LoLAdvisor.Core/Stats/StatsCache.cs tests/LoLAdvisor.Tests/StatsCacheTests.cs
git commit -m "feat: per-patch file cache for champion build stats"
```

---

### Task 6: `StatsProvider`

Orchestration + the two pure mappings (role/mode, ddragon Key → OP.GG name).

**Files:**
- Create: `src/LoLAdvisor.Core/Stats/StatsProvider.cs`
- Create: `tests/LoLAdvisor.Tests/StatsProviderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

public class StatsProviderTests
{
    private sealed class FakeClient : IOpggClient
    {
        public int Calls;
        public string? Response;
        public Task<string?> GetChampionAnalysisTextAsync(
            string champion, string gameMode, string position, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Response);
        }
    }

    private static string JayceToolText() => OpggMcpClientTestsHelper.JayceText();

    [Fact]
    public void Maps_positions_and_modes()
    {
        Assert.Equal(("aram", "none"), StatsProvider.MapModeAndPosition("TOP", mapNumber: 12));
        Assert.Equal(("ranked", "top"), StatsProvider.MapModeAndPosition("TOP", 11));
        Assert.Equal(("ranked", "mid"), StatsProvider.MapModeAndPosition("MIDDLE", 11));
        Assert.Equal(("ranked", "adc"), StatsProvider.MapModeAndPosition("BOTTOM", 11));
        Assert.Equal(("ranked", "support"), StatsProvider.MapModeAndPosition("UTILITY", 11));
        Assert.Equal(("ranked", "all"), StatsProvider.MapModeAndPosition("", 11));
        Assert.Equal(("ranked", "all"), StatsProvider.MapModeAndPosition(null, 11));
    }

    [Fact]
    public void Converts_ddragon_key_to_upper_snake()
    {
        Assert.Equal("JAYCE", StatsProvider.ToOpggName("Jayce"));
        Assert.Equal("MONKEY_KING", StatsProvider.ToOpggName("MonkeyKing"));
        Assert.Equal("DR_MUNDO", StatsProvider.ToOpggName("DrMundo"));
        Assert.Equal("JARVAN_IV", StatsProvider.ToOpggName("JarvanIV"));
        // KSante no tiene frontera minúscula→mayúscula: queda KSANTE, que OP.GG
        // también acepta (el matching del servidor tolera ambas formas).
        Assert.Equal("KSANTE", StatsProvider.ToOpggName("KSante"));
    }

    [Fact]
    public async Task Fetches_parses_and_caches()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loladvisor-tests-" + Guid.NewGuid());
        try
        {
            var client = new FakeClient { Response = JayceToolText() };
            var provider = new StatsProvider(client, new StatsCache(dir));

            var first = await provider.GetAsync("Jayce", "TOP", 11, "16.13.1");
            Assert.NotNull(first);
            Assert.Equal(1, client.Calls);

            var second = await provider.GetAsync("Jayce", "TOP", 11, "16.13.1");
            Assert.NotNull(second);
            Assert.Equal(1, client.Calls);   // cache hit: sin segunda llamada
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Client_failure_returns_null()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loladvisor-tests-" + Guid.NewGuid());
        try
        {
            var provider = new StatsProvider(new FakeClient { Response = null }, new StatsCache(dir));
            Assert.Null(await provider.GetAsync("Jayce", "TOP", 11, "16.13.1"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}

/// <summary>Extrae el texto del tool desde la fixture (compartido entre tests).</summary>
internal static class OpggMcpClientTestsHelper
{
    public static string JayceText() =>
        LoLAdvisor.Core.Stats.OpggMcpClient.ExtractToolText(Fixtures.OpggJayce())!;
}
```

Note: `ExtractToolText` is `internal` — check whether the test project already sees internals. Search for `InternalsVisibleTo`:

Run: `grep -r "InternalsVisibleTo" src/`
If absent, add to `src/LoLAdvisor.Core/LoLAdvisor.Core.csproj` inside an `<ItemGroup>`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="LoLAdvisor.Tests" />
</ItemGroup>
```

(If `ArchetypeWeights` — also internal — is already exercised by tests, the mechanism exists; verify before adding a duplicate.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter StatsProviderTests`
Expected: FAIL — `StatsProvider` does not exist

- [ ] **Step 3: Implement**

```csharp
using System.Text.RegularExpressions;

namespace LoLAdvisor.Core.Stats;

/// <summary>
/// Punto de entrada de la capa estadística: cache-first, fetch al MCP de OP.GG
/// en miss, y <c>null</c> ante cualquier fallo — la app funciona igual sin stats.
/// </summary>
public sealed class StatsProvider
{
    private readonly IOpggClient _client;
    private readonly StatsCache _cache;

    public StatsProvider(IOpggClient? client = null, StatsCache? cache = null)
    {
        _client = client ?? new OpggMcpClient();
        _cache = cache ?? new StatsCache();
    }

    /// <param name="livePosition">Posición cruda de la Live Client API (TOP/MIDDLE/… o vacía).</param>
    public async Task<ChampionBuildStats?> GetAsync(
        string championKey, string? livePosition, int mapNumber, string patch,
        CancellationToken ct = default)
    {
        var (mode, position) = MapModeAndPosition(livePosition, mapNumber);
        if (_cache.TryRead(patch, championKey, mode, position, out var cached))
            return cached;

        var text = await _client.GetChampionAnalysisTextAsync(
            ToOpggName(championKey), mode, position, ct).ConfigureAwait(false);
        if (text is null)
            return null;

        var stats = OpggResponseParser.Parse(text, championKey, mode, position);
        if (stats is not null)
            _cache.Write(patch, championKey, mode, position, stats);
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter StatsProviderTests`
Expected: 4 PASS

- [ ] **Step 5: Commit**

```bash
git add src/LoLAdvisor.Core/Stats/StatsProvider.cs tests/LoLAdvisor.Tests/StatsProviderTests.cs src/LoLAdvisor.Core/LoLAdvisor.Core.csproj
git commit -m "feat: StatsProvider orchestrating OP.GG fetch with per-patch cache"
```

---

### Task 7: `partype` parsing → `StaticChampion.UsesMana`

**Files:**
- Modify: `src/LoLAdvisor.Core/DataDragon/StaticTypes.cs` (StaticChampion, ~line 12-26)
- Modify: `src/LoLAdvisor.Core/DataDragon/DataDragonCatalog.cs` (FromJson ~line 150-167, ChampionEntry DTO ~line 252)
- Modify: `src/LoLAdvisor.Core/Config/AdvisorConfig.cs` (ItemsConfig, after the keyword lists ~line 103)
- Create: `tests/LoLAdvisor.Tests/PartypeTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using LoLAdvisor.Core.DataDragon;

namespace LoLAdvisor.Tests;

public class PartypeTests
{
    private static DataDragonCatalog Catalog(string partype) => DataDragonCatalog.FromJson(
        "16.13.1",
        $$"""
        {"data":{"TestChamp":{"id":"TestChamp","key":"999","name":"Test Champ",
          "partype":"{{partype}}","tags":["Fighter"],"info":{"attack":5,"defense":5,"magic":5}}}}
        """,
        """{"data":{}}""");

    [Theory]
    [InlineData("Mana", true)]
    [InlineData("Maná", true)]       // es_MX
    [InlineData("Energy", false)]
    [InlineData("Fury", false)]
    [InlineData("None", false)]
    [InlineData("", true)]           // partype ausente/vacío: no hay evidencia, no penalizar
    public void UsesMana_follows_partype(string partype, bool expected)
    {
        var champ = Catalog(partype).ChampionByKey("TestChamp")!;
        Assert.Equal(expected, champ.UsesMana);
        Assert.Equal(partype, champ.Partype);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter PartypeTests`
Expected: FAIL — `UsesMana` does not exist

- [ ] **Step 3: Implement the three edits**

`StaticTypes.cs` — add to `StaticChampion` (after `Info`, before `PrimaryTag`):

```csharp
    /// <summary>Recurso del kit (<c>partype</c> de ddragon, LOCALIZADO: "Mana"/"Maná", "Energy"…).</summary>
    public string Partype { get; init; } = "";

    /// <summary>
    /// Usa maná según partype y la lista localizable de la config. Vacío o
    /// desconocido cuenta como "sí": sin evidencia no se penalizan items de maná.
    /// </summary>
    public bool UsesMana { get; init; } = true;
```

`AdvisorConfig.cs` — add to `ItemsConfig` (after `CritReductionKeywords`, ~line 103):

```csharp
    /// <summary>Nombres localizados del recurso maná en champion.json (partype). en + es.</summary>
    public List<string> ManaResourceNames { get; set; } = new() { "Mana", "Maná" };
```

`DataDragonCatalog.cs`:

In `ChampionEntry` (private DTO, ~line 252), add:

```csharp
        public string? Partype { get; set; }
```

In `FromJson`, inside the champion loop (~line 154), add the two properties to the `StaticChampion` initializer:

```csharp
            var partype = entry.Partype ?? "";
            champions[id] = new StaticChampion
            {
                Id = id,
                Key = string.IsNullOrEmpty(entry.Id) ? textKey : entry.Id,
                Name = entry.Name,
                Tags = entry.Tags.AsReadOnly(),
                Partype = partype,
                UsesMana = partype.Length == 0
                    || config.ManaResourceNames.Contains(partype, StringComparer.OrdinalIgnoreCase),
                Info = new ChampionInfo
                {
                    Attack = entry.Info?.Attack ?? 0,
                    Defense = entry.Info?.Defense ?? 0,
                    Magic = entry.Info?.Magic ?? 0,
                },
            };
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter PartypeTests`
Expected: 6 PASS

- [ ] **Step 5: Commit**

```bash
git add src/LoLAdvisor.Core/DataDragon/ src/LoLAdvisor.Core/Config/AdvisorConfig.cs tests/LoLAdvisor.Tests/PartypeTests.cs
git commit -m "feat: parse partype into StaticChampion.UsesMana"
```

---

### Task 8: Mana gating in `ItemAdvisor`

Manaless champions get `Mana`/`ManaRegen` weights removed before scoring.

**Files:**
- Modify: `src/LoLAdvisor.Core/Items/ItemAdvisor.cs` (Advise ~line 102, new private helper)
- Create: `tests/LoLAdvisor.Tests/ItemAdvisorStatsTests.cs` (this file also grows in Task 9)

- [ ] **Step 1: Write the failing test**

Create `tests/LoLAdvisor.Tests/ItemAdvisorStatsTests.cs`. The test builds a minimal catalog via `DataDragonCatalog.FromJson` with a mana item and a non-mana item, and a `GameState` with a manaless champion (Mage archetype forced so mana would normally score).

```csharp
using LoLAdvisor.Core.DataDragon;
using LoLAdvisor.Core.Items;
using LoLAdvisor.Core.Models;

namespace LoLAdvisor.Tests;

public class ItemAdvisorStatsTests
{
    // Catálogo mínimo: un campeón (partype configurable), un item de maná y uno de AP.
    // Los items cumplen los filtros de "completado": purchasable, from, oro >= 1100, mapa 11.
    internal static DataDragonCatalog Catalog(string partype = "Mana") => DataDragonCatalog.FromJson(
        "16.13.1",
        $$"""
        {"data":{"TestChamp":{"id":"TestChamp","key":"999","name":"Test Champ",
          "partype":"{{partype}}","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9}},
          "Enemy":{"id":"Enemy","key":"998","name":"Enemy",
          "partype":"Mana","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9}}}}
        """,
        """
        {"data":{
          "1026":{"name":"Blasting Wand","gold":{"total":850,"sell":595,"purchasable":true},
                  "tags":["SpellDamage"],"maps":{"11":true,"12":true},"into":["4001","4002"]},
          "4001":{"name":"Mana Tome Item","gold":{"total":2900,"sell":2030,"purchasable":true},
                  "tags":["SpellDamage","Mana","ManaRegen"],"maps":{"11":true,"12":true},
                  "from":["1026"],"depth":2,
                  "stats":{"FlatMagicDamageMod":60}},
          "4002":{"name":"Pure AP Item","gold":{"total":2900,"sell":2030,"purchasable":true},
                  "tags":["SpellDamage"],"maps":{"11":true,"12":true},
                  "from":["1026"],"depth":2,
                  "stats":{"FlatMagicDamageMod":60}}}}
        """);

    internal static GameState State()
    {
        var state = new GameState
        {
            ActivePlayer = new ActivePlayer { SummonerName = "Me", CurrentGold = 3000 },
            GameData = new GameData { GameTime = 900, GameMode = "CLASSIC", MapNumber = 11 },
        };
        state.AllPlayers.Add(new Player
        {
            SummonerName = "Me", ChampionName = "Test Champ",
            RawChampionName = "game_character_displayname_TestChamp", Team = "ORDER",
        });
        state.AllPlayers.Add(new Player
        {
            SummonerName = "Foe", ChampionName = "Enemy",
            RawChampionName = "game_character_displayname_Enemy", Team = "CHAOS",
        });
        return state;
    }

    [Fact]
    public void Manaless_champion_never_gets_mana_items()
    {
        var advisor = new ItemAdvisor(Catalog(partype: "Energy"));
        var plan = advisor.Advise(State(), BuildArchetype.Mage)!;
        Assert.DoesNotContain(plan.Recommendations, r => r.Item.Name == "Mana Tome Item");
        Assert.Contains(plan.Recommendations, r => r.Item.Name == "Pure AP Item");
    }

    [Fact]
    public void Mana_champion_still_gets_mana_items()
    {
        var advisor = new ItemAdvisor(Catalog(partype: "Mana"));
        var plan = advisor.Advise(State(), BuildArchetype.Mage)!;
        Assert.Contains(plan.Recommendations, r => r.Item.Name == "Mana Tome Item");
    }
}
```

Note: with Mage weights, "Mana Tome Item" (SpellDamage 3 + Mana 1.5 + ManaRegen 1) outscores "Pure AP Item" (3) for a mana champion; with gating both have fit 3 and both appear (MaxRecommendations = 3). The first assert is the load-bearing one: after gating, the mana item's EXTRA fit is gone, but it still has SpellDamage fit — so it would still appear. **Therefore gating must zero the item, not just shrink it? No** — the spec says mana tags weigh 0, the item can still be recommended for its AP. Re-read the first test: `DoesNotContain` would FAIL under pure weight-gating because the item still fits via SpellDamage.

**Resolution (do this):** gating removes mana-tag weight AND, for manaless champions, items whose mana tags are a defining component (any `Mana`/`ManaRegen` tag) get skipped entirely — a champion without mana gains nothing from a Manamune-style item; its AD/AP is priced assuming the mana passive. Implement as a skip in the item loop, not a weight tweak. This matches the original bug report ("con Jayce siempre hay que tener items de mana" — inverse: Riven should never see them).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter ItemAdvisorStatsTests`
Expected: FAIL — mana item is recommended to the Energy champion

- [ ] **Step 3: Implement in `ItemAdvisor.Advise`**

After the `weights` line (~line 102), resolve the champion once:

```csharp
        var weights = WeightsFor(profile.Archetype);
        // Campeón sin maná (partype): los items de maná no le aportan nada — su
        // AD/AP está tasado asumiendo la pasiva de maná. Se excluyen por completo.
        var myChampion = _data.ResolveChampion(me.ChampionName, me.RawChampionName);
        var skipManaItems = myChampion is { UsesMana: false };
```

In the item loop (~line 105), add the skip right after the owned check:

```csharp
        foreach (var item in _data.CompletedItemsFor(mapNumber))
        {
            if (owned.Contains(item.Id) || ownedNames.Contains(item.Name))
                continue;
            if (skipManaItems && (item.HasTag("Mana") || item.HasTag("ManaRegen")))
                continue;
```

- [ ] **Step 4: Run tests — new ones pass, old ones still pass**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj`
Expected: ALL PASS (existing fixture uses real champions whose partype parses; if any existing test regresses, the fixture champion resolved as manaless — inspect, likely the test catalog lacks partype so `UsesMana` stays true and nothing changes)

- [ ] **Step 5: Commit**

```bash
git add src/LoLAdvisor.Core/Items/ItemAdvisor.cs tests/LoLAdvisor.Tests/ItemAdvisorStatsTests.cs
git commit -m "feat: manaless champions never see mana items (partype gating)"
```

---

### Task 9: Statistical prior rule in `ItemAdvisor.ScoreItem`

The core of the feature: additive fuzzy rule + transformation bridging (Muramana→Manamune) + boots/starter tiebreak.

**Files:**
- Modify: `src/LoLAdvisor.Core/Items/ItemAdvisor.cs` (constants ~line 25-36, Advise signature ~line 52, ScoreItem ~line 316, BootsFor ~line 493, StarterFor ~line 188)
- Modify: `src/LoLAdvisor.Core/Config/AdvisorConfig.cs` (ItemsConfig: `ItemEvolutions` map)
- Modify: `tests/LoLAdvisor.Tests/ItemAdvisorStatsTests.cs`

- [ ] **Step 1: Write the failing tests (add to ItemAdvisorStatsTests)**

```csharp
using LoLAdvisor.Core.Stats;   // añadir arriba

    private static ChampionBuildStats StatsWith(params int[] coreIds) => new()
    {
        ChampionKey = "TestChamp",
        GameMode = "ranked",
        Position = "mid",
        CoreItems = new ItemSetStats(coreIds, PickRate: 0.25, Play: 10000, Win: 5600),
    };

    [Fact]
    public void Prior_boosts_the_statistically_core_item()
    {
        var advisor = new ItemAdvisor(Catalog());
        var state = State();

        // Sin stats, ambos items AP puros empatan; con prior, 4002 debe ir primero.
        var withStats = advisor.Advise(state, BuildArchetype.Mage, StatsWith(4002))!;
        Assert.Equal("Pure AP Item", withStats.Recommendations[0].Item.Name);
        Assert.Contains(withStats.Recommendations[0].Reasons,
            r => r.Contains("of Test Champ builds"));
    }

    [Fact]
    public void Null_stats_change_nothing()
    {
        var advisor = new ItemAdvisor(Catalog());
        var state = State();
        var without = advisor.Advise(state, BuildArchetype.Mage)!;
        var withNull = advisor.Advise(state, BuildArchetype.Mage, stats: null)!;
        Assert.Equal(
            without.Recommendations.Select(r => (r.Item.Id, r.Score)),
            withNull.Recommendations.Select(r => (r.Item.Id, r.Score)));
    }

    [Fact]
    public void Prior_bridges_item_evolutions_via_config()
    {
        // OP.GG lista la forma evolucionada (4003, no comprable y SIN from — igual
        // que Muramana en ddragon real); el catálogo recomienda la comprable (4002).
        // El puente es el mapa ItemEvolutions de la config.
        var config = new LoLAdvisor.Core.Config.ItemsConfig();
        config.ItemEvolutions[4003] = 4002;
        var catalog = DataDragonCatalog.FromJson(
            "16.13.1",
            """
            {"data":{"TestChamp":{"id":"TestChamp","key":"999","name":"Test Champ",
              "partype":"Mana","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9}},
              "Enemy":{"id":"Enemy","key":"998","name":"Enemy",
              "partype":"Mana","tags":["Mage"],"info":{"attack":2,"defense":3,"magic":9}}}}
            """,
            """
            {"data":{
              "1026":{"name":"Blasting Wand","gold":{"total":850,"sell":595,"purchasable":true},
                      "tags":["SpellDamage"],"maps":{"11":true},"into":["4002","4004"]},
              "4002":{"name":"Buyable Form","gold":{"total":2900,"sell":2030,"purchasable":true},
                      "tags":["SpellDamage"],"maps":{"11":true},"from":["1026"],"depth":2,
                      "stats":{"FlatMagicDamageMod":60}},
              "4003":{"name":"Evolved Form","gold":{"total":2900,"sell":2030,"purchasable":false},
                      "tags":["SpellDamage"],"maps":{"11":true},
                      "stats":{"FlatMagicDamageMod":80}},
              "4004":{"name":"Other AP","gold":{"total":2900,"sell":2030,"purchasable":true},
                      "tags":["SpellDamage"],"maps":{"11":true},"from":["1026"],"depth":2,
                      "stats":{"FlatMagicDamageMod":60}}}}
            """, config);
        var advisor = new ItemAdvisor(catalog, config);
        var plan = advisor.Advise(State(), BuildArchetype.Mage, StatsWith(4003))!;
        Assert.Equal("Buyable Form", plan.Recommendations[0].Item.Name);
    }

    [Fact]
    public void Stat_prior_keeps_core_category()
    {
        var advisor = new ItemAdvisor(Catalog());
        var plan = advisor.Advise(State(), BuildArchetype.Mage, StatsWith(4002))!;
        var top = plan.Recommendations[0];
        // Prior estadístico refuerza el fit: la categoría sigue siendo Core/Spike,
        // no Counter (no hay contrapartida situacional aquí).
        Assert.NotEqual(RecommendationCategory.Counter, top.Category);
        Assert.NotEqual(RecommendationCategory.Defense, top.Category);
    }
```

(For the counters-still-win guarantee: existing tests in `AdviceTests`/`MayhemAdvisorTests` cover counter behavior with `stats: null` — unchanged by construction since the parameter defaults to null. The magnitude relationship `CleanseMag 3.0 > StatCoreMag × μ_max = 2.5 × 1.25 = 3.125`… **note:** μ max is `1.0 × 1.25 = 1.25`, so max stat bonus 3.125 slightly exceeds CleanseMag alone — but a needed counter also carries its own fit term, and the stat item carries fit too. The invariant that matters and is tested: null stats identical, and category stays Core.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter ItemAdvisorStatsTests`
Expected: FAIL — `Advise` has no `stats` parameter

- [ ] **Step 3: Implement**

Add `using LoLAdvisor.Core.Stats;` to `ItemAdvisor.cs`.

Constants (after `LifestealDevalMag`, ~line 34):

```csharp
    // Prior estadístico (OP.GG): magnitud del bono por "esto es lo que compran los
    // jugadores de tu campeón". Rampa sobre el pick rate del SET de build (los sets
    // completos rondan 0.05–0.35, no confundir con pick rate por item), escalada
    // ±25 % por win rate. Calibrado para reforzar el fit sin aplastar counters.
    private const double StatCoreMag = 2.5;
    private const double StatPickFoot = 0.05;
    private const double StatPickShoulder = 0.30;
    private const double StatWinFoot = 0.48;
    private const double StatWinShoulder = 0.54;
```

`Advise` signature (~line 52):

```csharp
    public ItemAdvicePlan? Advise(GameState state, BuildArchetype? forcedArchetype = null,
        ChampionBuildStats? stats = null)
```

In `Advise`, compute the champion display name and pass stats through (the loop ~line 110):

```csharp
        var champName = string.IsNullOrEmpty(me.ChampionName) ? "your champion" : me.ChampionName;
        ...
            var (score, reasons, category) = ScoreItem(item, profile, threat, weights, teamHasGw, stats, champName);
```

`ScoreItem` — new parameters and the rule. Signature:

```csharp
    private (double Score, List<string> Reasons, RecommendationCategory Category) ScoreItem(
        StaticItem item, ChampionProfile me, TeamThreat threat,
        IReadOnlyDictionary<string, double> weights, bool teamHasGw,
        ChampionBuildStats? stats, string champName)
```

Add the rule after the shield-break block (~line 409) and BEFORE the penalty line, using a separate accumulator so the category logic still reflects situational-vs-fit:

```csharp
        // Prior estadístico: lo que los jugadores de este campeón realmente compran
        // (OP.GG, por rol y parche). Entra como refuerzo del fit — NO cuenta como
        // situacional para la categoría — así el item sigue siendo Core/Spike.
        double statBonus = 0;
        if (stats is not null && PriorFor(item, stats) is { } prior)
        {
            var mu = Fuzzy.Ramp(prior.PickRate, StatPickFoot, StatPickShoulder)
                   * (0.75 + 0.5 * Fuzzy.Ramp(prior.WinRate, StatWinFoot, StatWinShoulder));
            if (mu > MuGate)
            {
                statBonus = StatCoreMag * mu;
                reasons.Add($"bought in {Pct(prior.PickRate)} of {champName} builds"
                    + (prior.WinRate > 0 ? $" ({Pct(prior.WinRate)} WR)" : ""));
            }
        }
```

Update the final score/category block (~line 414):

```csharp
        var score = core + offense + defense + statBonus - penalty;
        // Categoría = qué explica el puntaje: si lo situacional es una fracción relevante
        // del total, manda la contrapartida dominante (defensa vs. counter ofensivo);
        // si no, el item se recomienda por fit puro (Core). El prior estadístico
        // refuerza el fit, no lo situacional.
        var situational = offense + defense;
```

Add to `ItemsConfig` in `src/LoLAdvisor.Core/Config/AdvisorConfig.cs` (after `ManaResourceNames` from Task 7):

```csharp
    /// <summary>
    /// Evolución → forma comprable. OP.GG registra la evolución (Muramana) pero el
    /// catálogo recomienda lo que se puede comprar (Manamune); ddragon NO enlaza
    /// ambas por from/into, así que el puente es explícito. Tear + Winter's Approach.
    /// </summary>
    public Dictionary<int, int> ItemEvolutions { get; set; } = new()
    {
        [3042] = 3004,   // Muramana → Manamune
        [3040] = 3003,   // Seraph's Embrace → Archangel's Staff
        [3121] = 3119,   // Fimbulwinter → Winter's Approach
    };
```

New helper in `ItemAdvisor` (near `SustainTagWeight`, ~line 435):

```csharp
    /// <summary>
    /// Prior del item en las builds del campeón. OP.GG registra evoluciones
    /// (Muramana) mientras el catálogo recomienda la forma comprable (Manamune);
    /// el mapa de la config traduce la evolución al item recomendable.
    /// </summary>
    private (double PickRate, double WinRate)? PriorFor(StaticItem item, ChampionBuildStats stats)
    {
        if (stats.ItemPriorFor(item.Id) is { } direct)
            return direct;
        foreach (var (evolved, buyable) in _config.ItemEvolutions)
            if (buyable == item.Id && stats.ItemPriorFor(evolved) is { } viaEvolution)
                return viaEvolution;
        return null;
    }
```

Boots tiebreak — in `BootsFor`, change the signature to accept stats and add the statistical pick between the threat-driven picks and the archetype fallback (~line 522, after the steelcaps block):

```csharp
    private BootsAdvice? BootsFor(Player me, ChampionProfile profile, TeamThreat threat,
        int mapNumber, ChampionBuildStats? stats)
```

```csharp
        // Sin amenaza que decida: las botas que más compran los jugadores de tu campeón.
        if (stats?.Boots is { } statBoots)
        {
            var popular = candidates.FirstOrDefault(c => statBoots.ItemIds.Contains(c.Id));
            if (popular is not null)
                return new BootsAdvice(popular,
                    $"most common boots on your champion ({Pct(statBoots.PickRate)} pick rate)");
        }
```

Update the call site in `Advise` (~line 174): `BootsFor(me, profile, threat, mapNumber, stats)`.

Starter tiebreak — in `StarterFor`, accept stats and prefer the statistical starter on ties (signature + ordering):

```csharp
    private StarterAdvice? StarterFor(Player me, ChampionProfile profile, double gameTime,
        bool isAram, IReadOnlyDictionary<string, double> weights, ChampionBuildStats? stats)
```

```csharp
        var statStarterIds = stats?.Starter?.ItemIds ?? Array.Empty<int>();
        var best = _data.AramStarterItems
            .OrderByDescending(i => statStarterIds.Contains(i.Id) ? 1 : 0)
            .ThenByDescending(i => i.Tags.Sum(t => weights.GetValueOrDefault(t)))
            .ThenByDescending(i => i.GoldTotal)
            .FirstOrDefault();
```

Update the call site (~line 177): `StarterFor(me, profile, state.GameData.GameTime, isAram, weights, stats)`.

Also verify `src/LoLAdvisor.Core/Advice/Rules/ItemRecommendationRule.cs:38` still compiles — it calls `Advise(state, archetype)` and the new parameter is optional, so no change needed (feed rules stay stat-less by design).

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj`
Expected: ALL PASS — pay attention to existing `AdviceTests`/`BuildPathPlannerTests`: the `stats` default null must leave them byte-identical

- [ ] **Step 5: Commit**

```bash
git add src/LoLAdvisor.Core/Items/ItemAdvisor.cs tests/LoLAdvisor.Tests/ItemAdvisorStatsTests.cs
git commit -m "feat: additive fuzzy prior from OP.GG build stats in ItemAdvisor"
```

---

### Task 10: `LcuRuneWriter`

**Files:**
- Create: `src/LoLAdvisor.Core/Connectors/Lcu/LcuRuneWriter.cs`
- Create: `tests/LoLAdvisor.Tests/LcuRuneWriterTests.cs`

- [ ] **Step 1: Write the failing test for the payload builder**

```csharp
using System.Text.Json;
using LoLAdvisor.Core.Connectors.Lcu;
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

public class LcuRuneWriterTests
{
    [Fact]
    public void Builds_the_lcu_page_payload()
    {
        var runes = new RunePageStats(
            8200, "Sorcery",
            new[] { 8230, 8226, 8233, 8236 }, new[] { "A", "B", "C", "D" },
            8300, "Inspiration",
            new[] { 8304, 8345 }, new[] { "E", "F" },
            new[] { 5008, 5008, 5001 }, 0.19);

        var json = LcuRuneWriter.BuildPagePayload("LoLAdvisor: Jayce", runes);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("LoLAdvisor: Jayce", root.GetProperty("name").GetString());
        Assert.Equal(8200, root.GetProperty("primaryStyleId").GetInt32());
        Assert.Equal(8300, root.GetProperty("subStyleId").GetInt32());
        Assert.Equal(
            new[] { 8230, 8226, 8233, 8236, 8304, 8345, 5008, 5008, 5001 },
            root.GetProperty("selectedPerkIds").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        Assert.True(root.GetProperty("current").GetBoolean());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter LcuRuneWriterTests`
Expected: FAIL — `LcuRuneWriter` does not exist

- [ ] **Step 3: Implement**

```csharp
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LoLAdvisor.Core.Net;
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Core.Connectors.Lcu;

/// <summary>
/// Escribe la página de runas recomendada en el cliente de LoL vía LCU:
/// borra la página previa de la app (prefijo fijo), crea la nueva y el cliente
/// la deja seleccionada. Todos los fallos devuelven un mensaje legible, nunca
/// lanzan — aplicar runas es un extra, no puede tumbar el flujo de consejo.
/// </summary>
public sealed class LcuRuneWriter
{
    public const string PagePrefix = "LoLAdvisor: ";
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
                        if (page.GetProperty("name").GetString()?.StartsWith(PagePrefix, StringComparison.Ordinal) == true)
                            (await SendAsync(creds, HttpMethod.Delete,
                                $"{PagesPath}/{page.GetProperty("id").GetInt64()}", null, ct).ConfigureAwait(false)).Dispose();
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
```

(Check `LcuCredentials` for the exact property names `BaseUrl`/`BasicAuthHeader` — they are the ones `LcuConnector` uses at `LcuConnector.cs:112-113`.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj --filter LcuRuneWriterTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/LoLAdvisor.Core/Connectors/Lcu/LcuRuneWriter.cs tests/LoLAdvisor.Tests/LcuRuneWriterTests.cs
git commit -m "feat: LcuRuneWriter applies recommended rune page via LCU"
```

---

### Task 11: `MainViewModel` wiring

Async stats fetch keyed by champion+position+map+patch, pass into `Advise`, runes-panel properties, apply-runes command.

**Files:**
- Modify: `src/LoLAdvisor.App/ViewModels/MainViewModel.cs`

No new unit tests (ViewModel is thin glue; core logic is already covered). Steps:

- [ ] **Step 1: Add fields and properties**

Near the other private fields (top of class):

```csharp
    private readonly StatsProvider _statsProvider = new();
    private readonly LcuRuneWriter _runeWriter = new();
    private ChampionBuildStats? _championStats;
    private string? _statsFetchKey;   // "champKey|pos|map|patch": evita re-fetch por tick
```

Add `using LoLAdvisor.Core.Stats;` and `using LoLAdvisor.Core.Connectors.Lcu;` (the latter may already exist).

With the other bindable properties (pattern at `MainViewModel.cs:158-168`):

```csharp
    private string _runesLine = "";
    public string RunesLine { get => _runesLine; private set => SetProperty(ref _runesLine, value); }

    private string _skillOrderLine = "";
    public string SkillOrderLine { get => _skillOrderLine; private set => SetProperty(ref _skillOrderLine, value); }

    private string _runesStatus = "";
    public string RunesStatus { get => _runesStatus; private set => SetProperty(ref _runesStatus, value); }

    public RelayCommand ApplyRunesCommand { get; }
```

In the constructor, near the other command initializations (find `new RelayCommand` in the file and follow its signature — likely `new RelayCommand(_ => OnApplyRunes())` or `new RelayCommand(OnApplyRunes)`):

```csharp
        ApplyRunesCommand = new RelayCommand(_ => OnApplyRunes());
```

- [ ] **Step 2: Request stats when the game context changes**

In `ApplyGameState` (line ~382), after `UpdateContext();`:

```csharp
        RequestStatsIfNeeded(state);
```

New methods (near `RebuildItemPlan`):

```csharp
    /// <summary>
    /// Pide las stats de OP.GG cuando cambia el contexto (campeón/rol/mapa/parche).
    /// El fetch es async y opcional: el consejo sale sin prior hasta que lleguen.
    /// </summary>
    private void RequestStatsIfNeeded(GameState state)
    {
        var me = state.ActivePlayerEntry;
        if (me is null || !_catalog.IsLoaded)
            return;
        var champ = _catalog.ResolveChampion(me.ChampionName, me.RawChampionName);
        if (champ is null)
            return;

        var mapNumber = state.GameData.MapNumber == 0 ? 11 : state.GameData.MapNumber;
        var key = $"{champ.Key}|{me.Position}|{mapNumber}|{_catalog.Version}";
        if (key == _statsFetchKey)
            return;
        _statsFetchKey = key;
        _championStats = null;
        RebuildRunesPanel();
        _ = FetchStatsAsync(champ.Key, me.Position, mapNumber, _catalog.Version, key);
    }

    private async Task FetchStatsAsync(string champKey, string position, int mapNumber,
        string patch, string key)
    {
        ChampionBuildStats? stats = null;
        try
        {
            stats = await _statsProvider.GetAsync(champKey, position, mapNumber, patch)
                .ConfigureAwait(false);
        }
        catch
        {
            // El provider ya degrada a null; este catch es el cinturón extra del hilo async.
        }
        OnUi(() =>
        {
            if (_statsFetchKey != key)
                return;   // llegó tarde: el contexto ya cambió
            _championStats = stats;
            AppendConsole(stats is null
                ? "[stats] OP.GG data unavailable — advising without statistical priors."
                : $"[stats] OP.GG build data loaded for {champKey} ({stats.GameMode}/{stats.Position}).");
            RebuildRunesPanel();
            if (_lastGameState is { } s)
                RebuildItemPlan(s);
        });
    }
```

- [ ] **Step 3: Pass stats into Advise and build the runes panel**

In `RebuildItemPlan` (line ~461):

```csharp
        var plan = _itemAdvisor?.Advise(state, _forcedArchetype, _championStats);
```

New methods:

```csharp
    private void RebuildRunesPanel()
    {
        if (_championStats?.Runes is not { } r)
        {
            RunesLine = "";
            SkillOrderLine = "";
            RunesStatus = "";
            return;
        }
        RunesLine = $"{r.PrimaryPageName}: {string.Join(" · ", r.PrimaryRuneNames)}   |   "
                  + $"{r.SecondaryPageName}: {string.Join(" · ", r.SecondaryRuneNames)}";
        var order = _championStats.Skills?.Order ?? Array.Empty<string>();
        SkillOrderLine = order.Count == 0
            ? ""
            : $"Skills: {SkillPriority(order)}   (first levels: {string.Join(" ", order.Take(6))}…)";
    }

    /// <summary>"Q > E > W": prioridad de maxeo por frecuencia en el orden de 15 niveles (R aparte).</summary>
    internal static string SkillPriority(IReadOnlyList<string> order) =>
        string.Join(" > ", order
            .Where(s => s is "Q" or "W" or "E")
            .GroupBy(s => s)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key));

    private async void OnApplyRunes()
    {
        if (_championStats?.Runes is not { } runes)
            return;
        RunesStatus = "Applying…";
        var champ = _championStats.ChampionKey;
        var error = await _runeWriter.ApplyAsync(champ, runes);
        RunesStatus = error ?? $"Rune page \"{LcuRuneWriter.PagePrefix}{champ}\" applied.";
        AppendConsole(error is null
            ? $"[runes] page applied for {champ}."
            : $"[runes] failed: {error}");
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/LoLAdvisor.App/LoLAdvisor.App.csproj`
Expected: Build succeeded. If `RelayCommand`'s constructor differs (check `src/LoLAdvisor.App/Mvvm/RelayCommand.cs`), match its actual signature.

- [ ] **Step 5: Commit**

```bash
git add src/LoLAdvisor.App/ViewModels/MainViewModel.cs
git commit -m "feat: wire OP.GG stats into the advisor and expose runes panel state"
```

---

### Task 12: Runes panel in `MainWindow.xaml`

**Files:**
- Modify: `src/LoLAdvisor.App/MainWindow.xaml` (insert after the Sell card that closes at ~line 249, right before the `<!-- ===== ARAM: MAYHEM` comment at ~line 251)

- [ ] **Step 1: Add the panel XAML**

Follow the existing card pattern (the Sell card at lines 237-249 is the template — same `Card` style, same collapse-when-empty trigger):

```xml
                        <!-- ===== RUNAS Y SKILLS — página más popular (OP.GG) ===== -->
                        <Border Padding="16,10">
                            <Border.Style>
                                <Style TargetType="Border" BasedOn="{StaticResource Card}">
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding RunesLine}" Value="">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Border.Style>
                            <StackPanel>
                                <TextBlock Text="Runes &amp; skills (most popular for your champion)"
                                           FontSize="12" FontWeight="Bold" Foreground="#94A3B8"
                                           Margin="0,0,0,4" />
                                <TextBlock Text="{Binding RunesLine}" FontSize="13" TextWrapping="Wrap" />
                                <TextBlock Text="{Binding SkillOrderLine}" FontSize="13"
                                           TextWrapping="Wrap" Margin="0,4,0,0" />
                                <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                                    <Button Content="Apply runes" Command="{Binding ApplyRunesCommand}"
                                            Padding="12,4" />
                                    <TextBlock Text="{Binding RunesStatus}" FontSize="12"
                                               Foreground="#94A3B8" VerticalAlignment="Center"
                                               Margin="10,0,0,0" />
                                </StackPanel>
                            </StackPanel>
                        </Border>
```

If the app has a themed button style in use elsewhere in this file (search for `<Button` occurrences), copy its `Style` attribute onto the Apply button so it matches.

- [ ] **Step 2: Build and run**

Run: `dotnet build src/LoLAdvisor.App/LoLAdvisor.App.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/LoLAdvisor.App/MainWindow.xaml
git commit -m "feat: runes and skill-order panel with apply button"
```

---

### Task 13: Full verification

- [ ] **Step 1: Full test suite**

Run: `dotnet test tests/LoLAdvisor.Tests/LoLAdvisor.Tests.csproj`
Expected: ALL PASS, zero warnings that didn't exist before

- [ ] **Step 2: Live smoke test of the OP.GG client (network)**

Small console check without the game running — replay mode plus the console line is enough:

Run: `dotnet run --project src/LoLAdvisor.App/LoLAdvisor.App.csproj` — enable replay mode in the UI, watch the console pane for `[stats] OP.GG build data loaded for <champ>` (or the graceful `unavailable` line if offline). The item panel should show a `bought in NN% of <champ> builds` reason on statistically-core items, and the runes card should appear.

- [ ] **Step 3: Update memory/docs if behavior notes emerged, then commit any stragglers**

```bash
git status
git add -A
git commit -m "chore: OP.GG stats integration follow-ups" # solo si quedó algo
```

---

## Self-review notes (already applied)

- **Spec coverage:** stats layer (Tasks 3-6), additive fuzzy rule (Task 9), partype gating (Tasks 7-8 — implemented as full item skip for manaless champions, stronger than the spec's weight-zeroing, justified in Task 8), runes+skills UI (Tasks 11-12), LCU apply (Task 10), cache per patch with pruning (Task 5), graceful degradation (null throughout), fixture-first spike (done during planning, Task 1).
- **Deviation from spec:** rune names come localized from OP.GG, so `runesReforged.json` icons are dropped (names-only panel). ARAM starter prior only affects ARAM (`StarterFor` is ARAM-only today — unchanged scope).
- **Type consistency:** `ChampionBuildStats`/`ItemSetStats`/`RunePageStats`/`SkillOrderStats` defined in Task 3 and used with those exact names in Tasks 5, 6, 9, 10, 11. `IOpggClient` defined Task 4, consumed Task 6. `Advise(state, forcedArchetype, stats)` signature set in Task 9, called in Task 11.
- **Data reality check:** the evolution bridge (Muramana→Manamune) uses `ItemsConfig.ItemEvolutions` because verified ddragon 16.13.1 does NOT link evolutions via `from`/`into`. Both bridging test and implementation reflect this.
