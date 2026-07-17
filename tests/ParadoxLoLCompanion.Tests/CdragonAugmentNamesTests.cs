using ParadoxLoLCompanion.Core.Augments;

namespace ParadoxLoLCompanion.Tests;

public class CdragonAugmentNamesTests
{
    [Fact]
    public void ParseNames_ExtractsIdNamePairs()
    {
        const string json = """
        { "augments": [
            { "id": 30, "name": "Eureka", "apiName": "Eureka", "rarity": 2 },
            { "id": 134, "name": "Desenvaina tu Espada" },
            { "id": 7, "desc": "sin nombre: se ignora" }
        ] }
        """;

        var names = CdragonAugmentNames.ParseNames(json);

        Assert.Equal(2, names.Count);
        Assert.Equal("Eureka", names[30]);
        Assert.Equal("Desenvaina tu Espada", names[134]);
    }

    [Fact]
    public void ParseNames_BadShape_Throws_SoCallerFallsBackToEmpty()
    {
        Assert.ThrowsAny<Exception>(() => CdragonAugmentNames.ParseNames("{ \"nope\": [] }"));
    }
}
