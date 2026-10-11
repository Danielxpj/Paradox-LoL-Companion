using System.Text.RegularExpressions;
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

        // Y el intento fallido no dejó nada cacheado.
        Assert.False(new AugmentCache(_dir).TryRead("26.14", out _));
    }

    [Fact]
    public async Task PageWithoutRecognizableRanks_IsRejected_NotCached()
    {
        var html = Regex.Replace(Fixtures.BlitzMayhemAugments(), "Tier [1-5]", "Unknown rank");
        var cache = new AugmentCache(_dir);
        var provider = new AugmentProvider(new FakeSource(html), cache);

        Assert.Null(await provider.GetAsync("26.14"));
        Assert.Empty(Directory.GetFiles(_dir, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CacheWithMissingRanks_IsRefetchedAndRepaired()
    {
        var legacyHtml = Fixtures.BlitzMayhemAugments();
        var brokenList = BlitzAugmentParser.Parse(Regex.Replace(legacyHtml, "Tier [1-5]", "Unknown rank"));
        var cache = new AugmentCache(_dir);
        cache.Write("26.14", brokenList);
        var currentHtml = Regex.Replace(legacyHtml, "Tier ([1-5])",
            m => $"{"SABCD"[int.Parse(m.Groups[1].Value) - 1]} Tier");
        var source = new FakeSource(currentHtml);
        var provider = new AugmentProvider(source, cache);

        var list = await provider.GetAsync("26.14");

        Assert.Equal(1, source.Calls);
        Assert.NotNull(list);
        Assert.Contains(list.Augments, a => a.Tier == 1);
        Assert.True(cache.TryRead("26.14", out var repaired));
        Assert.Contains(repaired!.Augments, a => a.Tier == 1);
        await provider.GetAsync("26.14");
        Assert.Equal(1, source.Calls);
    }
}
