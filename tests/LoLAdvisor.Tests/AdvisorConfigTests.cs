using LoLAdvisor.Core.Config;

namespace LoLAdvisor.Tests;

public class AdvisorConfigTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefault()
    {
        var c = AdvisorConfig.Load("no-existe-este-archivo.json");
        Assert.Equal(1300, c.Gold.ComponentGold);
        Assert.Contains("Soraka", c.Items.HealerChampions);
    }

    [Fact]
    public void Default_HasSaneValues()
    {
        var c = AdvisorConfig.Default;
        Assert.Equal(7.0, c.CsPerMinute.Target);
        Assert.Equal(1200.0, c.Objectives.BaronFirstSpawn);
        Assert.Contains("LifeSteal", c.Items.SustainTags);
    }

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = AdvisorConfig.Default;
        original.Gold.LegendaryGold = 3200;
        var json = original.ToJson();

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "advisor-config-test.json");
        System.IO.File.WriteAllText(path, json);
        try
        {
            var loaded = AdvisorConfig.Load(path);
            Assert.Equal(3200, loaded.Gold.LegendaryGold);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
