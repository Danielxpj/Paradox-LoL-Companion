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
}
