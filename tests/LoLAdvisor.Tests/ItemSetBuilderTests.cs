using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

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
