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
            new AugmentInfo { Id = 4, Name = "Sin Rankear", Rarity = AugmentRarity.Gold, Tier = null },
        },
    };

    [Fact]
    public void ThreeNamesOnScreen_DetectedAndBestIsLowestTier()
    {
        var offered = new OfferedAugmentDetector(List).Detect(new[]
        { "CHOOSE ONE", "Goliath", "Become large...", "Eureka", "Mystic Punch", "12s" });

        Assert.Equal(3, offered.Count);
        Assert.Equal("Eureka", offered[0].Name);   // tier 1 primero
        Assert.True(offered[0].IsBest);
        Assert.False(offered[1].IsBest);
        Assert.False(offered[2].IsBest);
    }

    [Fact]
    public void FewerThanTwoMatches_ReturnsEmpty_NotGuessing()
    {
        // Una sola línea puede ser el tooltip de un augment ya tomado.
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

    [Fact]
    public void RealPickerOcrLines_DetectAllThree_AgainstRealTierList()
    {
        // Líneas EXACTAS que el OCR de Windows leyó del frame real del picker
        // (tools/OcrProbe sobre la captura del 2026-07-17, receta center 70x50 x2).
        var list = BlitzAugmentParser.Parse(Fixtures.BlitzMayhemAugments());
        var offered = new OfferedAugmentDetector(list).Detect(new[]
        {
            "Shrink Ray", "Utility", "Your Attacks reduce the target's damage",
            "and size by 15% On-Hit.",
            "Skilled Sniper", "Damage", "Snipe an enemy with a non-Ultimate",
            "Ability refunds 80% Cooldown (65% for", "periodic A bilities).",
            "With Haste", "Gain Move Speed equal to 70% of your", "Ability Haste.",
        });

        Assert.Equal(3, offered.Count);
        Assert.Contains(offered, a => a.Name == "Shrink Ray");
        Assert.Contains(offered, a => a.Name == "Skilled Sniper");
        Assert.Contains(offered, a => a.Name == "With Haste");
        // El mejor es uno de los dos tier 2 (Skilled Sniper / With Haste), nunca
        // Shrink Ray (tier 5).
        Assert.True(offered[0].IsBest);
        Assert.NotEqual("Shrink Ray", offered[0].Name);
    }

    [Fact]
    public void Matches_ReportTheSourceLineIndex()
    {
        var offered = new OfferedAugmentDetector(List).Detect(new[]
        { "CHOOSE ONE", "Goliath", "noise", "Eureka" });

        Assert.Equal(3, offered.Single(a => a.Name == "Eureka").LineIndex);
        Assert.Equal(1, offered.Single(a => a.Name == "Goliath").LineIndex);
    }

    [Fact]
    public void UnrankedAugment_SortsLast_AndRankedOneIsBest()
    {
        var offered = new OfferedAugmentDetector(List)
            .Detect(new[] { "Sin Rankear", "Goliath" });
        Assert.Equal("Goliath", offered[0].Name);
        Assert.True(offered[0].IsBest);
        Assert.Equal("Sin Rankear", offered[1].Name);
    }
}
