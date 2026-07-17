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
        var matcher = new AugmentNameMatcher(list);
        matcher.AddAlias(1, "Guantelete enjoyado");   // es_MX vía cdragon
        return matcher;
    }

    [Theory]
    [InlineData("Jeweled Gauntlet")]      // exacto
    [InlineData("jeweled gauntlet")]      // case
    [InlineData("Jeweled Gauntiet")]      // typo de OCR (l→i)
    [InlineData("Guantelete enjoyado")]   // alias localizado
    [InlineData("Guantelete enjoyad0")]   // typo de OCR en el alias (o→0)
    public void Matches_DespiteOcrNoise(string line) =>
        Assert.Equal(1, Matcher().Match(line)!.Id);

    [Theory]
    [InlineData("PRESS TAB FOR SCOREBOARD")]
    [InlineData("Recall to base")]
    [InlineData("")]
    [InlineData("HP")]                     // corto: nunca matchear por debajo de 4
    public void JunkLines_DoNotMatch(string line) =>
        Assert.Null(Matcher().Match(line));

    [Fact]
    public void ShortNameWithOneTypo_StillMatches() =>
        Assert.Equal(3, Matcher().Match("Eurek a")!.Id);

    [Fact]
    public void ExactMatch_BeatsFuzzyNeighbor()
    {
        // "Mystic Punch" exacto no debe irse a otro nombre parecido.
        Assert.Equal(2, Matcher().Match("Mystic Punch")!.Id);
    }

    [Fact]
    public void Normalize_StripsDiacriticsAndSymbols() =>
        Assert.Equal("bonk", AugmentNameMatcher.Normalize("¡BONK!"));

    [Theory]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "abd", 1)]
    [InlineData("abc", "ab", 1)]
    [InlineData("abcdef", "azcdef", 1)]
    public void BoundedLevenshtein_ComputesDistance(string a, string b, int expected) =>
        Assert.Equal(expected, AugmentNameMatcher.BoundedLevenshtein(a, b, 2));

    [Fact]
    public void BoundedLevenshtein_OverBudget_ReturnsMinusOne() =>
        Assert.Equal(-1, AugmentNameMatcher.BoundedLevenshtein("completely", "different!!", 2));
}
