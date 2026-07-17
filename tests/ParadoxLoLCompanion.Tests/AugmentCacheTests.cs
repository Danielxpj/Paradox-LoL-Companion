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
        Assert.Equal(AugmentRarity.Prismatic, a.Rarity);
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

    [Fact]
    public void MissingDir_IsAMiss_NotACrash()
    {
        var cache = new AugmentCache(Path.Combine(_dir, "nope"));
        Assert.False(cache.TryRead("26.14", out _));
    }
}
