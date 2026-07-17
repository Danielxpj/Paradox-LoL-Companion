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
        // Los augments nuevos del final de cada sección no traen badge de tier.
        Assert.Contains(List.Augments, a => a.Tier is null);
    }

    [Fact]
    public void AugmentIds_AreUniqueAndPositive()
    {
        Assert.All(List.Augments, a => Assert.True(a.Id > 0));
        Assert.Equal(List.Augments.Count, List.Augments.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void FitsChampion_MatchesCaseInsensitive_AndRejectsNull()
    {
        var dw = List.Augments.Single(a => a.Name == "Dual Wield");
        Assert.True(List.FitsChampion(dw, "vayne"));
        Assert.False(List.FitsChampion(dw, null));
    }

    [Fact]
    public void Garbage_ReturnsEmptyList()
    {
        Assert.Empty(BlitzAugmentParser.Parse("<html><body>nothing here</body></html>").Augments);
    }
}
