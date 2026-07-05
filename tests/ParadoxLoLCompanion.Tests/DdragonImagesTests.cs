using ParadoxLoLCompanion.Core.DataDragon;

namespace ParadoxLoLCompanion.Tests;

public class DdragonImagesTests
{
    [Fact]
    public void Builds_champion_and_item_icon_urls()
    {
        Assert.Equal(
            "https://ddragon.leagueoflegends.com/cdn/16.13.1/img/champion/Ahri.png",
            DdragonImages.ChampionIcon("16.13.1", "Ahri"));
        Assert.Equal(
            "https://ddragon.leagueoflegends.com/cdn/16.13.1/img/item/3157.png",
            DdragonImages.ItemIcon("16.13.1", 3157));
    }

    [Fact]
    public void Missing_version_or_key_yields_null_not_a_broken_url()
    {
        Assert.Null(DdragonImages.ChampionIcon("", "Ahri"));
        Assert.Null(DdragonImages.ChampionIcon("16.13.1", null));
        Assert.Null(DdragonImages.ChampionIcon("16.13.1", ""));
        Assert.Null(DdragonImages.ItemIcon("", 3157));
        Assert.Null(DdragonImages.ItemIcon("16.13.1", 0));
    }
}
