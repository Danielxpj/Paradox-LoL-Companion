using ParadoxLoLCompanion.App.ViewModels;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;

namespace ParadoxLoLCompanion.App.Tests;

public class ItemReplacementViewModelTests
{
    [Theory]
    [InlineData(500, 400, false, "KEEP Magic Ward", "56%")]
    [InlineData(900, 0, true, "SWAP IN SHOP", "100%")]
    public void SwapCard_ShowsNetCostAndWaitsForEnoughGold(double gold, int missing,
        bool affordable, string action, string progress)
    {
        var old = new StaticItem { Id = 1, Name = "Magic Ward", SellGold = 2100 };
        var target = new StaticItem { Id = 2, Name = "Armor Ward", GoldTotal = 3000 };
        var recommendation = new ItemRecommendation(target, 3, new[] { "answers physical damage" },
            new PurchasePlan(target, 3000, affordable ? target : null), missing)
        {
            Replacement = new ItemReplacement(old, 0, 2100, 900, 3, 2),
        };
        var row = new ItemRecoRowViewModel(recommendation, "test", gold);
        Assert.Equal("900 NET", row.Cost);
        Assert.Equal("REPLACE", row.CategoryLabel);
        Assert.Contains(action, row.BuyBarText);
        Assert.Equal(progress, row.GoldPercent);
        Assert.DoesNotContain("BUY NOW", row.BuyBarText);
        if (!affordable)
            Assert.Contains("SAVE 400 MORE", row.BuyBarText);
    }
}
