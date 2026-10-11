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

    [Theory]
    [InlineData("aram-mayhem", 17, 14)]
    [InlineData("arena", 19, 15)]
    public void CurrentPages_ParseLetterRanksAndRarities(string mode, int total, int ranked)
    {
        var list = BlitzAugmentParser.Parse(Fixtures.BlitzCurrentAugments(mode));

        Assert.Equal(total, list.Augments.Count);
        Assert.Equal(ranked, list.Augments.Count(a => a.Tier.HasValue));
        foreach (var rarity in new[] { AugmentRarity.Prismatic, AugmentRarity.Gold, AugmentRarity.Silver })
        {
            var group = list.Augments.Where(a => a.Rarity == rarity).ToArray();
            Assert.Contains(group, a => a.Tier is null);
            foreach (var tier in new[] { 2, 3, 4, 5 })
                Assert.Contains(group, a => a.Tier == tier);
        }
        Assert.Contains(list.Augments, a => a.Tier == 1);
    }

    [Fact]
    public void CurrentMayhemPage_PreservesChampionLinksAndDescriptions()
    {
        var list = BlitzAugmentParser.Parse(Fixtures.BlitzCurrentAugments("aram-mayhem"));
        var augment = Assert.Single(list.Augments, a => a.Name == "Jeweled Gauntlet");

        Assert.Equal(1048, augment.Id);
        Assert.Equal(1, augment.Tier);
        Assert.Contains("Jayce", augment.TopChampions);
        Assert.Contains("Critically Strike", augment.Description);
        Assert.Equal("jeweledgauntlet_large.webp", augment.IconSlug);
    }

    [Fact]
    public void CurrentArenaPage_PreservesUniqueAugmentsAndChampionLinks()
    {
        var list = BlitzAugmentParser.Parse(Fixtures.BlitzCurrentAugments("arena"));
        var unique = Assert.Single(list.Augments, a => a.Name == "Abstain From Power");
        Assert.Equal("Unique", unique.Rarity.ToString());
        Assert.Null(unique.Tier);
        var clown = Assert.Single(list.Augments, a => a.Name == "Clown College");
        Assert.Equal(1, clown.Tier);
        Assert.NotEmpty(clown.TopChampions);
    }

    [Theory]
    [InlineData("S Tier", 1)]
    [InlineData("A Tier", 2)]
    [InlineData("B Tier", 3)]
    [InlineData("C Tier", 4)]
    [InlineData("D Tier", 5)]
    [InlineData("Tier 1", 1)]
    [InlineData("Tier 5", 5)]
    [InlineData("s tier", 1)]
    [InlineData("Unranked", null)]
    public void TierLabels_MapToNumericRanks(string label, int? expected)
    {
        var html = $$"""
            <h3>Gold ARAM Mayhem Augments</h3>
            <div class="augment card-container" id="1">
                <img alt="{{label}}" class="tier-icon" />
                <h4 class="name">Example</h4>
            </div>
            """;
        Assert.Equal(expected, Assert.Single(BlitzAugmentParser.Parse(html).Augments).Tier);
    }
}
