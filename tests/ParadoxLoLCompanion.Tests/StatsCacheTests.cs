using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

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
