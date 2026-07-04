using LoLAdvisor.Core.DataDragon;

namespace LoLAdvisor.Tests;

public class PartypeTests
{
    private static DataDragonCatalog Catalog(string partype) => DataDragonCatalog.FromJson(
        "16.13.1",
        $$"""
        {"data":{"TestChamp":{"id":"TestChamp","key":"999","name":"Test Champ",
          "partype":"{{partype}}","tags":["Fighter"],"info":{"attack":5,"defense":5,"magic":5} } } }
        """,
        """{"data":{}}""");

    [Theory]
    [InlineData("Mana", true)]
    [InlineData("Maná", true)]       // es_MX
    [InlineData("Energy", false)]
    [InlineData("Fury", false)]
    [InlineData("None", false)]
    [InlineData("", true)]           // partype ausente/vacío: no hay evidencia, no penalizar
    public void UsesMana_follows_partype(string partype, bool expected)
    {
        var champ = Catalog(partype).ChampionByKey("TestChamp")!;
        Assert.Equal(expected, champ.UsesMana);
        Assert.Equal(partype, champ.Partype);
    }
}
