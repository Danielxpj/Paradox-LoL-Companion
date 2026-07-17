# ARAM Mayhem Augment Advisor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Mayhem advisor real augment knowledge: a per-patch Blitz.gg tier list rendered as a ranked cheat-sheet in the overlay (L1), and live detection of the three augments the player is being offered via screen capture + Windows OCR, marking the best one (L2).

**Architecture:** L1 mirrors the existing OP.GG stats pipeline (client → parser → per-patch disk cache → provider → advisor → VM → overlay): `BlitzAugmentClient` fetches the server-rendered tier-list HTML with a browser User-Agent (verified: plain GET returns the full data; only non-browser UAs get 403), `BlitzAugmentParser` extracts ~220 augments (id, name, rarity, tier 1–5, description, icon slug, top champions) with structure-anchored regexes (never svelte class hashes), and `MayhemAdvisor` ranks them (champion-fit first, then tier). L2 avoids fragile card-position geometry entirely: capture the whole game window (PrintWindow → BGRA buffer → SoftwareBitmap), OCR it with `Windows.Media.Ocr` (built into Windows, needs TFM `net10.0-windows10.0.19041.0` on the App only), and fuzzy-match every recognized line against the augment name index; 2+ hits while the pick window is open = the offered augments. Localized (es_MX) augment names come from CommunityDragon's arena JSON where available.

**Tech Stack:** .NET 10 / WPF, System.Text.Json, xUnit; no new NuGet packages (WinRT OCR comes with the Windows TFM; capture is pure Win32 P/Invoke).

**Key facts discovered up front (do not re-derive):**
- SSR HTML card shape (verified 2026-07-17): `<div class="augment card-container svelte-hs10q8" id="1030">` … `<img src="…/arena/augments/eureka_large.webp" … alt="Eureka" class="augment-img …">` … `<img src="…/tier-icon/tier-s.svg" alt="Tier 1" …>` … `<h4 class="name type-title--bold">Eureka</h4>` … `<p class="rich-description description …">…</p>` … `<a href="../lol/champions/Vayne/aram-mayhem" class="champion-link …">`.
- Rarity sections are `<h3 …>Prismatic|Gold|Silver ARAM Mayhem Augments</h3>`; cards belong to the preceding h3.
- Some cards have no tier badge (new augments) → `Tier = null`.
- Fetch URL: `https://blitz.gg/lol/aram-mayhem-augments`. Chrome UA required.
- CommunityDragon `https://raw.communitydragon.org/latest/cdragon/arena/{en_us,es_mx}.json` exists (fields: `id`, `name`, `apiName`, `desc`, `rarity`, icons) and covers the Arena-shared subset of Mayhem augments; Mayhem-only augments (Poro Stampede, BONK!, …) are absent → alias mapping is best-effort by English-name join.
- Overlay requires Borderless/Windowed (already documented in OverlayWindow); PrintWindow capture has the same constraint — exclusive fullscreen yields black frames, which simply produces zero OCR matches (graceful).

---

### Task 1: Fixture + `AugmentTierList` model + `BlitzAugmentParser` (TDD)

**Files:**
- Create: `tests/ParadoxLoLCompanion.Tests/Fixtures/blitz-aram-mayhem-augments.html` (downloaded page, `<script>` bodies stripped to keep the repo light; all markup intact)
- Modify: `tests/ParadoxLoLCompanion.Tests/Fixtures.cs` (add accessor)
- Create: `src/ParadoxLoLCompanion.Core/Augments/AugmentTierList.cs`
- Create: `src/ParadoxLoLCompanion.Core/Augments/BlitzAugmentParser.cs`
- Test: `tests/ParadoxLoLCompanion.Tests/BlitzAugmentParserTests.cs`

- [ ] **Step 1: Download and save the fixture** (PowerShell)

```powershell
$ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
$html = (Invoke-WebRequest -Uri "https://blitz.gg/lol/aram-mayhem-augments" -UserAgent $ua -TimeoutSec 30).Content
# Strip script bodies (bulk of the 1.6 MB; parser must not depend on them anyway)
$html = [regex]::Replace($html, '(?is)<script\b[^>]*>.*?</script>', '<script></script>')
Set-Content -Path "tests/ParadoxLoLCompanion.Tests/Fixtures/blitz-aram-mayhem-augments.html" -Value $html -Encoding utf8
```

Add to `Fixtures.cs`:

```csharp
/// <summary>Página SSR real de blitz.gg/lol/aram-mayhem-augments (2026-07-17, scripts vaciados).</summary>
public static string BlitzMayhemAugments() => File.ReadAllText(Path("blitz-aram-mayhem-augments.html"));
```

- [ ] **Step 2: Write the model**

`AugmentTierList.cs` (class-with-init style to match `ChampionBuildStats` and stay STJ-cache-friendly):

```csharp
namespace ParadoxLoLCompanion.Core.Augments;

public enum AugmentRarity { Silver = 0, Gold = 1, Prismatic = 2 }

/// <summary>Un augment de ARAM: Mayhem según el tier list de Blitz.gg.</summary>
public sealed class AugmentInfo
{
    public int Id { get; init; }                       // id del card de Blitz (id de juego)
    public string Name { get; init; } = "";
    public AugmentRarity Rarity { get; init; }
    /// <summary>1 (mejor) a 5, o null si Blitz aún no lo rankeó.</summary>
    public int? Tier { get; init; }
    public string Description { get; init; } = "";     // texto plano, sin tags
    /// <summary>Slug del icono, p.ej. "eureka_large.webp" (CDN de Blitz).</summary>
    public string IconSlug { get; init; } = "";
    /// <summary>Keys tipo ddragon de los campeones donde Blitz lo marca top.</summary>
    public IReadOnlyList<string> TopChampions { get; init; } = Array.Empty<string>();

    public string IconUrl => IconSlug.Length == 0
        ? "" : $"https://blitz-cdn.blitz.gg/blitz/lol/arena/augments/{IconSlug}";
    /// <summary>Etiqueta S/A/B/C/D del tier numérico (1..5), o "—" sin rankear.</summary>
    public string TierLabel => Tier switch
    { 1 => "S", 2 => "A", 3 => "B", 4 => "C", 5 => "D", _ => "—" };
}

/// <summary>Tier list completo de augments (fuente: Blitz.gg), cacheable por parche.</summary>
public sealed class AugmentTierList
{
    public IReadOnlyList<AugmentInfo> Augments { get; init; } = Array.Empty<AugmentInfo>();

    public bool FitsChampion(AugmentInfo augment, string? championKey) =>
        championKey is not null && augment.TopChampions.Contains(
            championKey, StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 3: Write the failing parser tests**

```csharp
using ParadoxLoLCompanion.Core.Augments;

namespace ParadoxLoLCompanion.Tests;

public class BlitzAugmentParserTests
{
    private static readonly AugmentTierList List =
        BlitzAugmentParser.Parse(Fixtures.BlitzMayhemAugments());

    [Fact]
    public void Parses_AllRaritySections()
    {
        Assert.True(List.Augments.Count(a => a.Rarity == AugmentRarity.Prismatic) >= 60);
        Assert.True(List.Augments.Count(a => a.Rarity == AugmentRarity.Gold) >= 60);
        Assert.True(List.Augments.Count(a => a.Rarity == AugmentRarity.Silver) >= 30);
    }

    [Fact]
    public void Eureka_IsPrismaticTier1_WithIdIconAndDescription()
    {
        var eureka = Assert.Single(List.Augments, a => a.Name == "Eureka");
        Assert.Equal(AugmentRarity.Prismatic, eureka.Rarity);
        Assert.Equal(1, eureka.Tier);
        Assert.Equal("S", eureka.TierLabel);
        Assert.True(eureka.Id > 0);
        Assert.Equal("eureka_large.webp", eureka.IconSlug);
        Assert.Contains("Ability Haste", eureka.Description);
        Assert.DoesNotContain("<", eureka.Description);   // tags fuera
    }

    [Fact]
    public void DualWield_ListsTopChampions()
    {
        var dw = Assert.Single(List.Augments, a => a.Name == "Dual Wield");
        Assert.Contains("Vayne", dw.TopChampions);
        Assert.Contains("Jinx", dw.TopChampions);
    }

    [Fact]
    public void UnrankedAugments_HaveNullTier()
    {
        // Los "NEW" del final de cada sección no traen badge de tier.
        Assert.Contains(List.Augments, a => a.Tier is null);
    }

    [Fact]
    public void Garbage_ReturnsEmptyList()
    {
        Assert.Empty(BlitzAugmentParser.Parse("<html><body>nothing here</body></html>").Augments);
    }
}
```

- [ ] **Step 4: Run to verify failure** — `dotnet test --filter BlitzAugmentParser` → compile error (parser missing), then FAIL.

- [ ] **Step 5: Implement the parser**

```csharp
using System.Net;
using System.Text.RegularExpressions;

namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>
/// Parser del HTML SSR de blitz.gg/lol/aram-mayhem-augments. Se ancla en la
/// ESTRUCTURA estable (h3 de sección, div.augment con id numérico, alt="Tier N",
/// h4.name) y nunca en los hashes de clase de Svelte, que cambian por deploy.
/// </summary>
public static class BlitzAugmentParser
{
    private static readonly Regex SectionRx = new(
        "<h3[^>]*>\\s*(Prismatic|Gold|Silver)\\s+ARAM\\s+Mayhem\\s+Augments\\s*</h3>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CardRx = new(
        "<div class=\"augment card-container[^\"]*\"[^>]*\\bid=\"(?<id>\\d+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NameRx = new(
        "<h4 class=\"name[^\"]*\"[^>]*>(?<name>.*?)</h4>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TierRx = new(
        "alt=\"Tier (?<tier>[1-5])\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IconRx = new(
        "/augments/(?<slug>[a-z0-9_\\-]+\\.webp)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DescRx = new(
        "<p class=\"rich-description[^\"]*\"[^>]*>(?<desc>.*?)</p>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ChampRx = new(
        "/lol/champions/(?<key>[^/\"]+)/aram-mayhem",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagRx = new("<[^>]+>", RegexOptions.Compiled);

    public static AugmentTierList Parse(string html)
    {
        var sections = SectionRx.Matches(html);
        if (sections.Count == 0)
            return new AugmentTierList();

        var augments = new List<AugmentInfo>();
        for (var s = 0; s < sections.Count; s++)
        {
            var rarity = Enum.Parse<AugmentRarity>(sections[s].Groups[1].Value, ignoreCase: true);
            var start = sections[s].Index + sections[s].Length;
            var end = s + 1 < sections.Count ? sections[s + 1].Index : html.Length;
            var body = html[start..end];

            var cards = CardRx.Matches(body);
            for (var c = 0; c < cards.Count; c++)
            {
                var cardStart = cards[c].Index;
                var cardEnd = c + 1 < cards.Count ? cards[c + 1].Index : body.Length;
                var card = body[cardStart..cardEnd];
                var name = NameRx.Match(card);
                if (!name.Success)
                    continue;
                augments.Add(new AugmentInfo
                {
                    Id = int.Parse(cards[c].Groups["id"].Value),
                    Name = Clean(name.Groups["name"].Value),
                    Rarity = rarity,
                    Tier = TierRx.Match(card) is { Success: true } t
                        ? int.Parse(t.Groups["tier"].Value) : null,
                    IconSlug = IconRx.Match(card) is { Success: true } i
                        ? i.Groups["slug"].Value.ToLowerInvariant() : "",
                    Description = DescRx.Match(card) is { Success: true } d
                        ? Clean(d.Groups["desc"].Value) : "",
                    TopChampions = ChampRx.Matches(card)
                        .Select(m => m.Groups["key"].Value).Distinct().ToArray(),
                });
            }
        }
        return new AugmentTierList { Augments = augments };
    }

    /// <summary>Sin tags, entidades decodificadas y espacios colapsados.</summary>
    private static string Clean(string fragment) =>
        Regex.Replace(WebUtility.HtmlDecode(TagRx.Replace(fragment, " ")), "\\s+", " ").Trim();
}
```

- [ ] **Step 6: Run tests to green** — `dotnet test --filter BlitzAugmentParser` → PASS. Adjust regexes only against the fixture if any fail (e.g. attribute order).

- [ ] **Step 7: Commit** — `git add … && git commit -m "feat(augments): Blitz Mayhem tier-list model and SSR parser"`

---

### Task 2: `BlitzAugmentClient` + `AugmentCache` + `AugmentProvider` (TDD)

**Files:**
- Create: `src/ParadoxLoLCompanion.Core/Augments/BlitzAugmentClient.cs`
- Create: `src/ParadoxLoLCompanion.Core/Augments/AugmentCache.cs`
- Create: `src/ParadoxLoLCompanion.Core/Augments/AugmentProvider.cs`
- Test: `tests/ParadoxLoLCompanion.Tests/AugmentCacheTests.cs`, `tests/ParadoxLoLCompanion.Tests/AugmentProviderTests.cs`

- [ ] **Step 1: Failing cache tests** (mirror `StatsCacheTests` conventions: temp dir per test)

```csharp
using ParadoxLoLCompanion.Core.Augments;

namespace ParadoxLoLCompanion.Tests;

public class AugmentCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("augcache").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static AugmentTierList SampleList() => new()
    {
        Augments = new[] { new AugmentInfo { Id = 1030, Name = "Eureka",
            Rarity = AugmentRarity.Prismatic, Tier = 1, IconSlug = "eureka_large.webp",
            TopChampions = new[] { "Ahri" } } },
    };

    [Fact]
    public void RoundTrips_ByPatch()
    {
        var cache = new AugmentCache(_dir);
        cache.Write("26.14", SampleList());
        Assert.True(cache.TryRead("26.14", out var read));
        var a = Assert.Single(read!.Augments);
        Assert.Equal("Eureka", a.Name);
        Assert.Equal(1, a.Tier);
        Assert.Contains("Ahri", a.TopChampions);
    }

    [Fact]
    public void MissesOtherPatch_AndPrunesIt()
    {
        var cache = new AugmentCache(_dir);
        cache.Write("26.13", SampleList());
        cache.Write("26.14", SampleList());
        Assert.False(cache.TryRead("26.13", out _));   // podado al escribir 26.14
        Assert.True(cache.TryRead("26.14", out _));
    }

    [Fact]
    public void ExpiredEntry_IsAMiss()
    {
        var cache = new AugmentCache(_dir) { MaxAge = TimeSpan.Zero };
        cache.Write("26.14", SampleList());
        Assert.False(cache.TryRead("26.14", out _));
    }
}
```

- [ ] **Step 2: Failing provider tests**

```csharp
using ParadoxLoLCompanion.Core.Augments;

namespace ParadoxLoLCompanion.Tests;

public class AugmentProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("augprov").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeSource(string? html) : IBlitzAugmentSource
    {
        public int Calls;
        public Task<string?> GetAugmentsHtmlAsync(CancellationToken ct = default)
        { Calls++; return Task.FromResult(html); }
    }

    [Fact]
    public async Task FetchesParses_AndCaches()
    {
        var source = new FakeSource(Fixtures.BlitzMayhemAugments());
        var provider = new AugmentProvider(source, new AugmentCache(_dir));
        var list = await provider.GetAsync("26.14");
        Assert.NotNull(list);
        Assert.True(list!.Augments.Count >= 150);

        await provider.GetAsync("26.14");
        Assert.Equal(1, source.Calls);   // segunda vez: caché
    }

    [Fact]
    public async Task FetchFailure_ReturnsNull()
    {
        var provider = new AugmentProvider(new FakeSource(null), new AugmentCache(_dir));
        Assert.Null(await provider.GetAsync("26.14"));
    }

    [Fact]
    public async Task SuspiciouslySmallParse_IsRejected_NotCached()
    {
        // Un redesign de Blitz que rompa el parser no debe envenenar la caché.
        var source = new FakeSource("<html><h3>Gold ARAM Mayhem Augments</h3></html>");
        var provider = new AugmentProvider(source, new AugmentCache(_dir));
        Assert.Null(await provider.GetAsync("26.14"));
    }
}
```

- [ ] **Step 3: Run to verify failure**, then implement:

`BlitzAugmentClient.cs`:

```csharp
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
```

`AugmentCache.cs` (same contract & tolerances as `StatsCache`; single file per patch):

```csharp
using System.Text.Json;

namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>
/// Caché en disco del tier list, un JSON por parche. Misma filosofía que
/// StatsCache: IO tolerante a fallos (error = miss), poda de parches viejos,
/// y caducidad dentro del parche (Blitz retoca el ranking a mano).
/// </summary>
public sealed class AugmentCache
{
    private readonly string _baseDir;

    public TimeSpan MaxAge { get; init; } = TimeSpan.FromHours(48);

    public AugmentCache(string? baseDir = null) =>
        _baseDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParadoxLoLCompanion", "augments");

    public bool TryRead(string patch, out AugmentTierList? list)
    {
        list = null;
        try
        {
            var path = FilePath(patch);
            if (!File.Exists(path))
                return false;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > MaxAge)
                return false;
            list = JsonSerializer.Deserialize<AugmentTierList>(File.ReadAllText(path));
            return list is { Augments.Count: > 0 };
        }
        catch
        {
            return false;
        }
    }

    public void Write(string patch, AugmentTierList list)
    {
        try
        {
            var path = FilePath(patch);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(list));
            PruneOtherPatches(patch);
        }
        catch
        {
            // Sin caché seguimos; solo cuesta re-descargar.
        }
    }

    private void PruneOtherPatches(string currentPatch)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(_baseDir))
                if (!string.Equals(Path.GetFileName(dir), currentPatch, StringComparison.Ordinal))
                    Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    private string FilePath(string patch) =>
        Path.Combine(_baseDir, patch, "mayhem-augments.json");
}
```

Note: STJ needs `IReadOnlyList<T>` round-trip — it serializes fine and deserializes into `List<T>`/arrays for init properties (same as `ChampionBuildStats.LateItems` already cached today). `Augments.Count: > 0` property pattern needs C# 12+ (fine).

`AugmentProvider.cs`:

```csharp
namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>
/// Cache-first como StatsProvider: miss → fetch a Blitz → parse → cachear.
/// <c>null</c> ante cualquier fallo; la app funciona igual sin tier list.
/// </summary>
public sealed class AugmentProvider
{
    /// <summary>Un parse con menos que esto es un redesign de Blitz, no datos:
    /// mejor sin tier list que con uno truncado cacheado 48 h.</summary>
    internal const int MinCredibleAugments = 100;

    private readonly IBlitzAugmentSource _source;
    private readonly AugmentCache _cache;
    private readonly Action<string>? _log;

    public AugmentProvider(IBlitzAugmentSource? source = null, AugmentCache? cache = null,
        Action<string>? log = null)
    {
        _source = source ?? new BlitzAugmentClient(log: log);
        _cache = cache ?? new AugmentCache();
        _log = log;
    }

    public async Task<AugmentTierList?> GetAsync(string patch, CancellationToken ct = default)
    {
        if (_cache.TryRead(patch, out var cached))
            return cached;

        var html = await _source.GetAugmentsHtmlAsync(ct).ConfigureAwait(false);
        if (html is null)
            return null;

        var list = BlitzAugmentParser.Parse(html);
        if (list.Augments.Count < MinCredibleAugments)
        {
            _log?.Invoke($"Blitz augments: parse produced {list.Augments.Count} augments — page layout changed? Ignoring.");
            return null;
        }
        _cache.Write(patch, list);
        return list;
    }
}
```

- [ ] **Step 4: Run to green** — `dotnet test --filter "AugmentCache|AugmentProvider"` → PASS.
- [ ] **Step 5: Commit** — `feat(augments): Blitz client, per-patch cache and provider`

---

### Task 3: Rank augments in `MayhemAdvisor` + VM + overlay/Match-tab UI (completes L1)

**Files:**
- Modify: `src/ParadoxLoLCompanion.Core/Mayhem/MayhemAdvisor.cs`
- Test: `tests/ParadoxLoLCompanion.Tests/MayhemAdvisorTests.cs` (add cases)
- Modify: `src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs` (provider, fetch-once-per-patch, VM collection)
- Modify: `src/ParadoxLoLCompanion.App/ViewModels/ItemViewModels.cs` (new row VM)
- Modify: `src/ParadoxLoLCompanion.App/OverlayWindow.xaml` + `src/ParadoxLoLCompanion.App/MainWindow.xaml` (augment panel)

- [ ] **Step 1: Failing advisor tests** (append to `MayhemAdvisorTests`)

```csharp
using ParadoxLoLCompanion.Core.Augments;   // arriba del archivo

private static AugmentTierList TierList() => new()
{
    Augments = new[]
    {
        new AugmentInfo { Id = 1, Name = "Eureka", Rarity = AugmentRarity.Prismatic, Tier = 1 },
        new AugmentInfo { Id = 2, Name = "Goliath", Rarity = AugmentRarity.Prismatic, Tier = 2 },
        new AugmentInfo { Id = 3, Name = "Dual Wield", Rarity = AugmentRarity.Prismatic, Tier = 2,
            TopChampions = new[] { "Jinx" } },
        new AugmentInfo { Id = 4, Name = "Bad One", Rarity = AugmentRarity.Prismatic, Tier = 5 },
        new AugmentInfo { Id = 5, Name = "Deft", Rarity = AugmentRarity.Gold, Tier = 1 },
        new AugmentInfo { Id = 6, Name = "Unranked", Rarity = AugmentRarity.Gold, Tier = null },
        new AugmentInfo { Id = 7, Name = "Witchful", Rarity = AugmentRarity.Silver, Tier = 1 },
    },
};

[Fact]
public void TopAugments_RankChampionFitFirst_ThenTier()
{
    var advice = Advisor().Advise(State(7, enemies: ("Ahri", "CHAOS", 0, None)),
        augments: TierList())!;

    // Jugamos Jinx: Dual Wield (tier 2, fit) por delante de Eureka (tier 1 global).
    var prismatic = advice.TopAugments.Where(a => a.Rarity == AugmentRarity.Prismatic).ToList();
    Assert.Equal("Dual Wield", prismatic[0].Name);
    Assert.True(prismatic[0].FitsMyChampion);
    Assert.Equal("Eureka", prismatic[1].Name);
    Assert.DoesNotContain(advice.TopAugments, a => a.Name == "Bad One");   // tier 5 fuera
    Assert.DoesNotContain(advice.TopAugments, a => a.Name == "Unranked");  // sin tier fuera
    Assert.Contains(advice.TopAugments, a => a.Name == "Deft");
    Assert.Contains(advice.TopAugments, a => a.Name == "Witchful");
}

[Fact]
public void NoTierList_TopAugmentsEmpty()
{
    var advice = Advisor().Advise(State(7, enemies: ("Ahri", "CHAOS", 0, None)))!;
    Assert.Empty(advice.TopAugments);
}
```

- [ ] **Step 2: Run to verify failure**, then implement in `MayhemAdvisor.cs`:

New record + record field (keep old positional params, append):

```csharp
/// <summary>Un augment sugerido del cheat-sheet (fuente: tier list de Blitz).</summary>
public sealed record AugmentSuggestion(
    int Id, string Name, AugmentRarity Rarity, int Tier, string TierLabel,
    bool FitsMyChampion, string IconUrl);
```

`MayhemAdvice` gains `IReadOnlyList<AugmentSuggestion> TopAugments` (append as last positional param with default not allowed in records — add as init property instead):

```csharp
public sealed record MayhemAdvice(
    int UnlockedPicks,
    int TotalPicks,
    int? NextPickLevel,
    bool PickWindowNow,
    string StatusLine,
    string? PickNowLine,
    IReadOnlyList<string> Guidance)
{
    /// <summary>Cheat-sheet rankeado (vacío sin tier list descargado).</summary>
    public IReadOnlyList<AugmentSuggestion> TopAugments { get; init; } =
        Array.Empty<AugmentSuggestion>();
}
```

`Advise` signature: `public MayhemAdvice? Advise(GameState state, BuildArchetype? forcedArchetype = null, AugmentTierList? augments = null)`. Before the return, compute:

```csharp
return new MayhemAdvice(unlocked, total, next, me.IsDead, status, pickNow,
    Guidance(state, me, forcedArchetype))
{ TopAugments = TopAugments(augments, me) };
```

```csharp
/// <summary>Por rareza: primero los favoritos para MI campeón, luego por tier;
/// solo tiers 1-2 (S/A) — el cheat-sheet es para decidir en segundos.</summary>
private IReadOnlyList<AugmentSuggestion> TopAugments(AugmentTierList? list, Player me)
{
    if (list is null || list.Augments.Count == 0)
        return Array.Empty<AugmentSuggestion>();
    var champKey = _data.ResolveChampion(me.ChampionName, me.RawChampionName)?.Key;
    return list.Augments
        .Where(a => a.Tier is 1 or 2)
        .Select(a => (Augment: a, Fits: list.FitsChampion(a, champKey)))
        .OrderByDescending(x => x.Augment.Rarity)
        .ThenByDescending(x => x.Fits)
        .ThenBy(x => x.Augment.Tier)
        .GroupBy(x => x.Augment.Rarity)
        .SelectMany(g => g.Take(MaxPerRarity))
        .Select(x => new AugmentSuggestion(x.Augment.Id, x.Augment.Name, x.Augment.Rarity,
            x.Augment.Tier!.Value, x.Augment.TierLabel, x.Fits, x.Augment.IconUrl))
        .ToArray();
}
private const int MaxPerRarity = 4;
```

(`MayhemAdvisor` needs the `IStaticData _data` field stored — currently only passed to profiler/threats; add `private readonly IStaticData _data;`.)

- [ ] **Step 3: Run advisor tests to green.**

- [ ] **Step 4: VM wiring** (`MainViewModel.cs`):
  - Fields: `private readonly AugmentProvider _augmentProvider = new(log: null);` → construct in ctor with `log: line => OnUi(() => AppendConsole(line))` next to `_statsProvider`; `private AugmentTierList? _augmentTiers; private string? _augmentFetchPatch;`
  - `RebuildMayhemAdvice(state)`: pass `_augmentTiers` into `Advise`, and first call `RequestAugmentsIfNeeded()` when `isMayhem`:

```csharp
private void RequestAugmentsIfNeeded()
{
    if (!_catalog.IsLoaded || _catalog.Version == _augmentFetchPatch)
        return;
    _augmentFetchPatch = _catalog.Version;
    _ = FetchAugmentsAsync(_catalog.Version);
}

private async Task FetchAugmentsAsync(string patch)
{
    AugmentTierList? list = null;
    try { list = await _augmentProvider.GetAsync(patch).ConfigureAwait(false); }
    catch { }
    OnUi(() =>
    {
        _augmentTiers = list;
        if (list is null)
        {
            _augmentFetchPatch = null;   // permitir reintento en el próximo tick de Mayhem
            AppendConsole("[augments] Blitz tier list unavailable — Mayhem card will show generic guidance only.");
        }
        else
        {
            AppendConsole($"[augments] Blitz Mayhem tier list loaded ({list.Augments.Count} augments).");
            if (_lastGameState is { } s) RebuildMayhemAdvice(s);
        }
    });
}
```

  - Retry throttle: add `private DateTime _augmentRetryAtUtc = DateTime.MinValue;` — in `RequestAugmentsIfNeeded`, on re-entry after failure require `DateTime.UtcNow >= _augmentRetryAtUtc`; set `_augmentRetryAtUtc = DateTime.UtcNow.AddSeconds(60)` when scheduling a fetch.
  - Expose `public ObservableCollection<AugmentRowViewModel> MayhemAugments { get; } = new();` — repopulate in `RebuildMayhemAdvice` from `advice.TopAugments` (clear when not mayhem/no advice).

- [ ] **Step 5: Row VM** (in `ItemViewModels.cs`, following existing row-VM style):

```csharp
/// <summary>Fila del cheat-sheet de augments de Mayhem (overlay y MATCH tab).</summary>
public sealed class AugmentRowViewModel(AugmentSuggestion suggestion)
{
    public string Name { get; } = suggestion.Name;
    public string TierLabel { get; } = suggestion.TierLabel;
    public string RarityLabel { get; } = suggestion.Rarity.ToString().ToUpperInvariant();
    public string IconUrl { get; } = suggestion.IconUrl;
    public bool FitsMyChampion { get; } = suggestion.FitsMyChampion;
    public string FitChip { get; } = suggestion.FitsMyChampion ? "★ YOUR CHAMP" : "";
    public Brush TierBrush { get; } = suggestion.Tier switch
    {
        1 => Palette.Gold, 2 => Palette.Neon, _ => Palette.TextMuted,
    };
}
```

(Adjust to actual `Palette` members at implementation time; check how `CategoryBrush` rows resolve brushes and copy that mechanism.)

- [ ] **Step 6: Overlay + MATCH tab XAML.** In `OverlayWindow.xaml`, below the `ItemRecos` ItemsControl add an `AUGMENTS` block bound to `MayhemAugments`, collapsed when empty (same `Count=0 → Collapsed` trigger pattern as "Waiting for a live match…"), each row: 24×24 icon, tier letter in `TierBrush`, name, `FitChip`. In `MainWindow.xaml`, find the Mayhem card (bindings `MayhemStatus`/`MayhemPickNow`/`MayhemGuidance`) and add the same ItemsControl under the guidance text.

- [ ] **Step 7: Build + run all tests** — `dotnet build && dotnet test` → green. Manual smoke: `dotnet run --project src/ParadoxLoLCompanion.App` in replay mode scenario 1 (Mayhem) — augment rows appear once tier list downloads.

- [ ] **Step 8: Commit** — `feat(mayhem): ranked Blitz augment cheat-sheet in advisor, overlay and match tab`

---

### Task 4: L2 groundwork — Windows TFM + game window capture + OCR adapter

**Files:**
- Modify: `src/ParadoxLoLCompanion.App/ParadoxLoLCompanion.App.csproj` (`<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>`)
- Create: `src/ParadoxLoLCompanion.App/Capture/GameWindowCapture.cs`
- Create: `src/ParadoxLoLCompanion.App/Capture/WindowsOcrReader.cs`

- [ ] **Step 1: TFM bump.** Change TargetFramework; `dotnet build` the solution to confirm WPF + WinRT projections coexist (they do on net10.0-windows10.0.x; `Windows.Media.Ocr` & `Windows.Graphics.Imaging` become available without packages). Tests project unaffected (references Core only — verify with `grep ProjectReference tests/**/*.csproj`).

- [ ] **Step 2: Capture.** Find the game window by class `RiotWindowClass` (fallback: title `League of Legends (TM) Client`), PrintWindow with `PW_RENDERFULLCONTENT (2)` into a 32-bpp DIB, return BGRA8 bytes + dimensions:

```csharp
using System.Runtime.InteropServices;

namespace ParadoxLoLCompanion.App.Capture;

/// <summary>Frame BGRA8 del juego (premultiplied no importa: solo se OCRea).</summary>
public sealed record CapturedFrame(byte[] Bgra, int Width, int Height);

/// <summary>
/// Captura la ventana del juego con PrintWindow (GDI). Igual que el overlay,
/// requiere Borderless/Windowed: en Fullscreen exclusivo sale negro — eso no es
/// un error, simplemente no habrá matches de OCR.
/// </summary>
public static class GameWindowCapture
{
    public static CapturedFrame? Capture()
    {
        var hwnd = FindWindow("RiotWindowClass", null)
            is var byClass && byClass != IntPtr.Zero
            ? byClass
            : FindWindow(null, "League of Legends (TM) Client");
        if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var rect))
            return null;
        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        if (w < 320 || h < 240)
            return null;

        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var info = new Bitmapinfo
        {
            Size = Marshal.SizeOf<Bitmapinfo>(), Width = w, Height = -h,   // top-down
            Planes = 1, BitCount = 32,
        };
        var bitmap = CreateDIBSection(memDc, ref info, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            if (bitmap == IntPtr.Zero)
                return null;
            var old = SelectObject(memDc, bitmap);
            var ok = PrintWindow(hwnd, memDc, PwRenderfullcontent);
            SelectObject(memDc, old);
            if (!ok)
                return null;
            var buffer = new byte[w * h * 4];
            Marshal.Copy(bits, buffer, 0, buffer.Length);
            return new CapturedFrame(buffer, w, h);
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private const uint PwRenderfullcontent = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmapinfo
    {
        public int Size, Width, Height;
        public ushort Planes, BitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public uint[] Colors;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref Bitmapinfo info,
        uint usage, out IntPtr bits, IntPtr section, uint offset);
}
```

(Adjust the struct if the compiler complains about `BITMAPINFO` marshalling — an explicit 40-byte `BITMAPINFOHEADER` without the color table also works for 32-bpp.)

- [ ] **Step 3: OCR adapter** — SoftwareBitmap from the BGRA buffer, engine from user profile with English fallback:

```csharp
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using System.Runtime.InteropServices.WindowsRuntime;

namespace ParadoxLoLCompanion.App.Capture;

/// <summary>Windows.Media.Ocr sobre un frame capturado → líneas de texto.</summary>
public static class WindowsOcrReader
{
    public static async Task<IReadOnlyList<string>> ReadLinesAsync(CapturedFrame frame)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null)
            return Array.Empty<string>();
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            frame.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, frame.Width, frame.Height,
            BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bitmap);
        return result.Lines.Select(l => l.Text).ToArray();
    }
}
```

- [ ] **Step 4: Build.** `dotnet build` green. No unit tests here (thin Win32/WinRT adapters); the pure logic they feed is tested in Task 5.
- [ ] **Step 5: Commit** — `feat(capture): game window capture and Windows OCR adapter (net10.0-windows10.0.19041)`

---

### Task 5: `AugmentNameMatcher` + `OfferedAugmentDetector` (Core, TDD) + optional es_MX aliases

**Files:**
- Create: `src/ParadoxLoLCompanion.Core/Augments/AugmentNameMatcher.cs`
- Create: `src/ParadoxLoLCompanion.Core/Augments/OfferedAugmentDetector.cs`
- Create: `src/ParadoxLoLCompanion.Core/Augments/CdragonAugmentNames.cs`
- Test: `tests/ParadoxLoLCompanion.Tests/AugmentNameMatcherTests.cs`, `tests/ParadoxLoLCompanion.Tests/OfferedAugmentDetectorTests.cs`
- Fixture: `tests/ParadoxLoLCompanion.Tests/Fixtures/cdragon-arena-es_mx-sample.json` (hand-trimmed: 3 augments)

- [ ] **Step 1: Failing matcher tests**

```csharp
using ParadoxLoLCompanion.Core.Augments;

namespace ParadoxLoLCompanion.Tests;

public class AugmentNameMatcherTests
{
    private static AugmentNameMatcher Matcher()
    {
        var list = new AugmentTierList { Augments = new[]
        {
            new AugmentInfo { Id = 1, Name = "Jeweled Gauntlet", Rarity = AugmentRarity.Prismatic, Tier = 2 },
            new AugmentInfo { Id = 2, Name = "Mystic Punch", Rarity = AugmentRarity.Prismatic, Tier = 2 },
            new AugmentInfo { Id = 3, Name = "Eureka", Rarity = AugmentRarity.Prismatic, Tier = 1 },
        } };
        var m = new AugmentNameMatcher(list);
        m.AddAlias(1, "Guantelete enjoyado");   // es_MX de cdragon
        return m;
    }

    [Theory]
    [InlineData("Jeweled Gauntlet")]      // exacto
    [InlineData("jeweled gauntlet")]      // case
    [InlineData("Jeweled Gauntiet")]      // typo de OCR (l→i)
    [InlineData("Guantelete enjoyado")]   // alias localizado
    [InlineData("Guantelete enjoyadо")]   // typo en alias
    public void Matches_DespiteOcrNoise(string line) =>
        Assert.Equal(1, Matcher().Match(line)!.Id);

    [Theory]
    [InlineData("Recall to base")]         // texto de UI cualquiera
    [InlineData("Eurek")]                  // demasiado corto/incompleto para 6 letras? (dist 1 sobre 6: SÍ matchea)
    public void JunkLines_DoNotMatch_OrShortOnesDo(string line) { /* dividir: ver Step 2 */ }

    [Fact]
    public void JunkLine_DoesNotMatch() =>
        Assert.Null(Matcher().Match("PRESS TAB FOR SCOREBOARD"));

    [Fact]
    public void ShortNameOneTypo_StillMatches() =>
        Assert.Equal(3, Matcher().Match("Eurek a")!.Id);
}
```

(Clean up the placeholder theory when writing the real file — keep only concrete cases: `JunkLine_DoesNotMatch` and `ShortNameOneTypo_StillMatches`.)

- [ ] **Step 2: Implement matcher** — normalization (lowercase, strip diacritics via `string.Normalize(FormD)` + `UnicodeCategory.NonSpacingMark` filter, collapse non-alphanumerics) + bounded Levenshtein:

```csharp
using System.Globalization;
using System.Text;

namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>
/// Matchea líneas de OCR contra nombres de augments (con aliases localizados).
/// Tolerancia: distancia de Levenshtein ≤ 1 + len/8 sobre el texto normalizado —
/// el OCR confunde l/i/1 y o/о pero no inventa palabras enteras.
/// </summary>
public sealed class AugmentNameMatcher
{
    private readonly List<(string Normalized, AugmentInfo Augment)> _entries = new();

    public AugmentNameMatcher(AugmentTierList list)
    {
        foreach (var a in list.Augments)
            if (a.Name.Length > 0)
                _entries.Add((Normalize(a.Name), a));
    }

    public void AddAlias(int augmentId, string alias)
    {
        var augment = _entries.FirstOrDefault(e => e.Augment.Id == augmentId).Augment;
        if (augment is not null && alias.Length > 0)
            _entries.Add((Normalize(alias), augment));
    }

    /// <summary>El augment cuyo nombre matchea la línea, o null.</summary>
    public AugmentInfo? Match(string ocrLine)
    {
        var line = Normalize(ocrLine);
        if (line.Length < 4)
            return null;
        AugmentInfo? best = null;
        var bestDist = int.MaxValue;
        foreach (var (name, augment) in _entries)
        {
            var budget = 1 + name.Length / 8;
            if (Math.Abs(line.Length - name.Length) > budget)
                continue;
            var dist = BoundedLevenshtein(line, name, budget);
            if (dist >= 0 && dist < bestDist)
            {
                bestDist = dist;
                best = augment;
                if (dist == 0)
                    break;
            }
        }
        return best;
    }

    internal static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>-1 si supera el presupuesto (early-out por fila).</summary>
    internal static int BoundedLevenshtein(string a, string b, int budget)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, curr[j]);
            }
            if (rowMin > budget)
                return -1;
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length] <= budget ? prev[b.Length] : -1;
    }
}
```

- [ ] **Step 3: Failing detector tests**

```csharp
using ParadoxLoLCompanion.Core.Augments;

namespace ParadoxLoLCompanion.Tests;

public class OfferedAugmentDetectorTests
{
    private static readonly AugmentTierList List = new()
    {
        Augments = new[]
        {
            new AugmentInfo { Id = 1, Name = "Eureka", Rarity = AugmentRarity.Prismatic, Tier = 1 },
            new AugmentInfo { Id = 2, Name = "Goliath", Rarity = AugmentRarity.Prismatic, Tier = 2 },
            new AugmentInfo { Id = 3, Name = "Mystic Punch", Rarity = AugmentRarity.Prismatic, Tier = 3 },
        },
    };

    [Fact]
    public void ThreeNamesOnScreen_DetectedAndBestIsLowestTier()
    {
        var offered = new OfferedAugmentDetector(List).Detect(new[]
        { "CHOOSE ONE", "Goliath", "become large...", "Eureka", "Mystic Punch", "12s" });

        Assert.Equal(3, offered.Count);
        Assert.Equal("Eureka", offered[0].Name);   // tier 1 primero
        Assert.True(offered[0].IsBest);
        Assert.False(offered[1].IsBest);
    }

    [Fact]
    public void FewerThanTwoMatches_ReturnsEmpty_NotGuessing()
    {
        var offered = new OfferedAugmentDetector(List).Detect(new[] { "Eureka", "SHOP" });
        Assert.Empty(offered);
    }

    [Fact]
    public void DuplicateLines_CollapseToOneAugment()
    {
        var offered = new OfferedAugmentDetector(List)
            .Detect(new[] { "Eureka", "Eureka", "Goliath" });
        Assert.Equal(2, offered.Count);
    }
}
```

- [ ] **Step 4: Implement detector**

```csharp
namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>Un augment reconocido en pantalla durante la ventana de pick.</summary>
public sealed record OfferedAugment(
    int Id, string Name, AugmentRarity Rarity, int? Tier, string TierLabel, bool IsBest);

/// <summary>
/// De líneas de OCR del frame completo a los augments ofrecidos. Sin geometría
/// de cartas: los nombres son evidencia suficiente y sobreviven cambios de
/// resolución/idioma. Con menos de 2 matches no se afirma nada (una sola línea
/// puede ser el tooltip de un augment ya tomado).
/// </summary>
public sealed class OfferedAugmentDetector(AugmentTierList list)
{
    private readonly AugmentNameMatcher _matcher = new(list);

    public AugmentNameMatcher Matcher => _matcher;   // para inyectar aliases

    public IReadOnlyList<OfferedAugment> Detect(IReadOnlyList<string> ocrLines)
    {
        var hits = new List<AugmentInfo>();
        foreach (var line in ocrLines)
            if (_matcher.Match(line) is { } augment && hits.All(h => h.Id != augment.Id))
                hits.Add(augment);

        if (hits.Count < 2)
            return Array.Empty<OfferedAugment>();

        var ranked = hits
            .OrderBy(a => a.Tier ?? int.MaxValue)
            .ThenByDescending(a => a.Rarity)
            .ToList();
        return ranked.Select((a, i) => new OfferedAugment(
            a.Id, a.Name, a.Rarity, a.Tier, a.TierLabel, IsBest: i == 0)).ToArray();
    }
}
```

- [ ] **Step 5: cdragon aliases (best-effort).** `CdragonAugmentNames.cs`: fetch `https://raw.communitydragon.org/latest/cdragon/arena/{locale}.json` (plain GET, no UA games needed), parse `{ "augments": [ { "id": n, "name": "..." } ] }` with STJ into `IReadOnlyDictionary<int,string>`; return empty on any failure. Then `AugmentNameMatcher.AddAlias` joins **by id** if Blitz ids match cdragon ids — VERIFY at implementation time with one probe (Eureka: Blitz id 1030 vs cdragon id); if they don't match, join en_us name → id → localized name instead:

```csharp
using System.Text.Json;

namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>
/// Nombres localizados de augments desde CommunityDragon (solo cubre los
/// compartidos con Arena; los exclusivos de Mayhem quedan sin alias y se
/// matchean por el nombre en inglés de Blitz).
/// </summary>
public sealed class CdragonAugmentNames
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _http;

    public CdragonAugmentNames(HttpClient? http = null) => _http = http ?? SharedHttp;

    /// <summary>name_en (minúsculas) → nombre localizado. Vacío ante cualquier fallo.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetAliasesAsync(
        string locale, CancellationToken ct = default)
    {
        try
        {
            var en = await FetchAsync("en_us", ct).ConfigureAwait(false);
            var loc = await FetchAsync(locale, ct).ConfigureAwait(false);
            return en
                .Where(kv => loc.ContainsKey(kv.Key))
                .ToDictionary(kv => kv.Value.ToLowerInvariant(), kv => loc[kv.Key]);
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>id → name para un locale.</summary>
    private async Task<Dictionary<int, string>> FetchAsync(string locale, CancellationToken ct)
    {
        var url = $"https://raw.communitydragon.org/latest/cdragon/arena/{locale}.json";
        using var doc = JsonDocument.Parse(
            await _http.GetStringAsync(url, ct).ConfigureAwait(false));
        var result = new Dictionary<int, string>();
        foreach (var a in doc.RootElement.GetProperty("augments").EnumerateArray())
            if (a.TryGetProperty("id", out var id) && a.TryGetProperty("name", out var name))
                result[id.GetInt32()] = name.GetString() ?? "";
        return result;
    }
}
```

Test with the trimmed local fixture only for the JSON shape (parse via a `JsonDocument`-level internal helper) — no network in tests. Wire-up in Task 6 feeds aliases into the matcher by en-name join: `foreach (var a in list.Augments) if (aliases.TryGetValue(a.Name.ToLowerInvariant(), out var es)) matcher.AddAlias(a.Id, es);`

- [ ] **Step 6: Run to green; commit** — `feat(augments): OCR name matcher, offered-augment detector, cdragon aliases`

---

### Task 6: Live detection wiring — poll during pick window, mark best in overlay

**Files:**
- Modify: `src/ParadoxLoLCompanion.App/ViewModels/MainViewModel.cs`
- Modify: `src/ParadoxLoLCompanion.App/ViewModels/ItemViewModels.cs` (OfferedAugmentRowViewModel)
- Modify: `src/ParadoxLoLCompanion.App/OverlayWindow.xaml`, `src/ParadoxLoLCompanion.App/MainWindow.xaml`

- [ ] **Step 1: VM state + poll.** Fields: `private OfferedAugmentDetector? _offeredDetector; private bool _ocrBusy; private DateTime _lastOcrUtc; private bool _aliasesLoaded;`
  - Build `_offeredDetector` when `_augmentTiers` arrives (in `FetchAugmentsAsync` success branch), then fire-and-forget alias load: `CdragonAugmentNames.GetAliasesAsync("es_mx")` → `AddAlias` per match (once).
  - In `RebuildMayhemAdvice`, when `advice.PickWindowNow && _offeredDetector is not null` and `!_ocrBusy` and `DateTime.UtcNow - _lastOcrUtc > TimeSpan.FromSeconds(2)` → `_ = DetectOfferedAsync();`. When the pick window closes, clear `OfferedAugments`.

```csharp
private async Task DetectOfferedAsync()
{
    _ocrBusy = true;
    _lastOcrUtc = DateTime.UtcNow;
    try
    {
        var frame = await Task.Run(Capture.GameWindowCapture.Capture).ConfigureAwait(false);
        if (frame is null)
            return;
        var lines = await Capture.WindowsOcrReader.ReadLinesAsync(frame).ConfigureAwait(false);
        var offered = _offeredDetector!.Detect(lines);
        OnUi(() =>
        {
            OfferedAugments.Clear();
            foreach (var o in offered)
                OfferedAugments.Add(new OfferedAugmentRowViewModel(o));
        });
    }
    catch
    {
        // La detección es best-effort: un fallo de captura/OCR no molesta al resto.
    }
    finally
    {
        _ocrBusy = false;
    }
}
```

  - `public ObservableCollection<OfferedAugmentRowViewModel> OfferedAugments { get; } = new();`

- [ ] **Step 2: Row VM**

```csharp
/// <summary>Augment ofrecido detectado por OCR, con su veredicto.</summary>
public sealed class OfferedAugmentRowViewModel(OfferedAugment offered)
{
    public string Name { get; } = offered.Name;
    public string TierLabel { get; } = offered.TierLabel;
    public bool IsBest { get; } = offered.IsBest;
    public string Verdict { get; } = offered.IsBest ? "◆ PICK THIS" : "";
}
```

- [ ] **Step 3: XAML.** Overlay: an `OFFERED NOW` block above the cheat-sheet, visible only when `OfferedAugments.Count > 0`, rows show tier letter + name, `IsBest` row gets gold border/background via DataTrigger. Match tab: same list inside the Mayhem card.

- [ ] **Step 4: Build + full tests green.** Manual verification limited to replay mode (no live Mayhem game available in dev): confirm no OCR runs without a game window and UI stays empty. Note honestly in the commit/docs that live-game OCR needs a real Mayhem session to validate.

- [ ] **Step 5: Commit** — `feat(mayhem): detect offered augments on screen (OCR) and mark the best pick`

---

### Task 7: Verification, docs, graph

- [ ] `dotnet build` + `dotnet test` full suite green (paste summary in final report).
- [ ] Update `README.md` (feature bullet) and this plan's checkboxes; note Mayhem-only augments lack es_MX aliases (matched in English) and that Fullscreen-exclusive blocks both overlay and capture.
- [ ] `graphify update .`
- [ ] Final commit — `docs: mayhem augment advisor notes` (or fold into last feat commit if trivial).

## Self-Review

- Spec coverage: L1 (fetch/parse/cache/rank/UI) → Tasks 1–3; L2 (capture/OCR/match/best-pick UI) → Tasks 4–6; verification → Task 7. ✓
- Placeholders: Step 1 of Task 5 contains an intentionally-flagged placeholder theory with instructions to concretize — resolved at implementation (write only the concrete facts listed). Palette brush names to be confirmed against `Palette.cs` (explicitly called out). ✓
- Type consistency: `AugmentTierList`/`AugmentInfo` used consistently; `Advise(state, forcedArchetype, augments)` matches tests; `OfferedAugment` record matches detector and row VM. ✓
