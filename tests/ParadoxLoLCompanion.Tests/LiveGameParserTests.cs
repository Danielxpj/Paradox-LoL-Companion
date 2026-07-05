using ParadoxLoLCompanion.Core.Live;

namespace ParadoxLoLCompanion.Tests;

public class LiveGameParserTests
{
    [Fact]
    public void Parse_MapsActivePlayer()
    {
        var state = LiveGameParser.Parse(Fixtures.AllGameData());

        Assert.NotNull(state.ActivePlayer);
        Assert.Equal("Faker", state.ActivePlayer!.SummonerName);
        Assert.Equal(6, state.ActivePlayer.Level);
        Assert.Equal(1450.5, state.ActivePlayer.CurrentGold);
        Assert.Equal(90.0, state.ActivePlayer.ChampionStats.AbilityPower);
    }

    [Fact]
    public void Parse_MapsAllPlayersWithScoresAndItems()
    {
        var state = LiveGameParser.Parse(Fixtures.AllGameData());

        Assert.Equal(2, state.AllPlayers.Count);

        var faker = state.AllPlayers[0];
        Assert.Equal("Ahri", faker.ChampionName);
        Assert.Equal("ORDER", faker.Team);
        Assert.Equal("3/1/2", faker.Scores.Kda);
        Assert.Equal(78, faker.Scores.CreepScore);
        Assert.Equal(2, faker.Items.Count);
        Assert.Equal("Doran's Ring", faker.Items[0].DisplayName);

        var chovy = state.AllPlayers[1];
        Assert.True(chovy.IsDead);
        Assert.Equal(8.0, chovy.RespawnTimer);
    }

    [Fact]
    public void Parse_MapsEvents_IncludingFlexibleStolenBool()
    {
        var state = LiveGameParser.Parse(Fixtures.AllGameData());

        Assert.Equal(5, state.Events.Events.Count);
        var dragon = state.Events.Events.Find(e => e.EventName == "DragonKill");
        Assert.NotNull(dragon);
        Assert.Equal("Fire", dragon!.DragonType);
        Assert.Equal("Faker", dragon.KillerName);
        Assert.False(dragon.Stolen); // venía como texto "False"
    }

    [Fact]
    public void Parse_MapsGameData()
    {
        var state = LiveGameParser.Parse(Fixtures.AllGameData());

        Assert.Equal("CLASSIC", state.GameData.GameMode);
        Assert.Equal(600.0, state.GameData.GameTime);
        Assert.Equal(10.0, state.GameData.GameTimeMinutes);
    }

    [Fact]
    public void ActivePlayerEntry_ResolvesFromAllPlayers()
    {
        var state = LiveGameParser.Parse(Fixtures.AllGameData());

        Assert.NotNull(state.ActivePlayerEntry);
        Assert.Equal("Ahri", state.ActivePlayerEntry!.ChampionName);
    }

    [Fact]
    public void TryParse_ReturnsNull_OnCorruptJson()
    {
        Assert.Null(LiveGameParser.TryParse("{ not valid json "));
    }

    [Fact]
    public void Parse_Normalizes_NullCollections()
    {
        // La API a veces manda campos null/ausentes: no debe romper.
        const string json = """
        { "activePlayer": null, "allPlayers": null, "events": null, "gameData": null }
        """;
        var state = LiveGameParser.Parse(json);
        Assert.Empty(state.AllPlayers);
        Assert.Empty(state.Events.Events);
        Assert.NotNull(state.GameData);
    }

    [Fact]
    public void ActivePlayerEntry_MatchesByRiotId_WhenSummonerNameEmpty()
    {
        // Parche nuevo: summonerName vacío, se empareja por riotId.
        const string json = """
        {
          "activePlayer": { "riotId": "Faker#KR1", "summonerName": "" },
          "allPlayers": [
            { "riotId": "Otro#EUW", "championName": "Zed", "summonerName": "" },
            { "riotId": "Faker#KR1", "championName": "Ahri", "summonerName": "" }
          ],
          "gameData": { "gameTime": 60.0 }
        }
        """;
        var state = LiveGameParser.Parse(json);
        Assert.NotNull(state.ActivePlayerEntry);
        Assert.Equal("Ahri", state.ActivePlayerEntry!.ChampionName);
    }
}
