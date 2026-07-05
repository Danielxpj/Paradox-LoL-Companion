using System.Text.Json;
using ParadoxLoLCompanion.Core.Connectors.Lcu;
using ParadoxLoLCompanion.Core.Live;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Tests;

public class LcuTests
{
    [Fact]
    public void Lockfile_Parse_Valid()
    {
        Assert.True(LockfileLocator.TryParse("LeagueClient:12345:54321:aB3xToKeN:https", out var creds));
        Assert.Equal(12345, creds.Pid);
        Assert.Equal(54321, creds.Port);
        Assert.Equal("aB3xToKeN", creds.Password);
        Assert.Equal("https://127.0.0.1:54321", creds.BaseUrl);
    }

    [Fact]
    public void Lockfile_BasicAuthHeader_IsRiotUserBase64()
    {
        LockfileLocator.TryParse("LeagueClient:1:2:pwd:https", out var creds);
        var expected = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("riot:pwd"));
        Assert.Equal(expected, creds.BasicAuthHeader);
    }

    [Theory]
    [InlineData("")]
    [InlineData("LeagueClient:1:2:pwd")]        // faltan campos
    [InlineData("LeagueClient:x:2:pwd:https")]  // pid no numérico
    [InlineData("LeagueClient:1:yy:pwd:https")] // puerto no numérico
    public void Lockfile_Parse_Invalid(string content)
    {
        Assert.False(LockfileLocator.TryParse(content, out _));
    }

    [Fact]
    public void CommandLine_Parse_ExtractsPortAndToken()
    {
        // Línea de comando representativa del proceso LeagueClientUx (args entre comillas).
        const string cmd =
            "\"D:\\Games\\Riot Games\\League of Legends\\LeagueClientUx.exe\" " +
            "\"--riotclient-auth-token=abc\" \"--riotclient-app-port=11111\" " +
            "\"--remoting-auth-token=XYZ123token\" \"--app-port=54321\" \"--locale=es_ES\"";

        Assert.True(LockfileLocator.TryParseCommandLine(cmd, out var creds));
        Assert.Equal(54321, creds.Port);
        Assert.Equal("XYZ123token", creds.Password);
        Assert.Equal("https://127.0.0.1:54321", creds.BaseUrl);
    }

    [Fact]
    public void CommandLine_Parse_Invalid_WhenMissingArgs()
    {
        Assert.False(LockfileLocator.TryParseCommandLine("\"LeagueClientUx.exe\" --locale=es_ES", out _));
    }

    [Fact]
    public void ChampSelect_Parse_MapsTeamsAndBans()
    {
        const string json = """
        {
          "localPlayerCellId": 0,
          "myTeam": [
            { "cellId": 0, "championId": 0, "championPickIntent": 103, "assignedPosition": "middle" },
            { "cellId": 1, "championId": 64, "championPickIntent": 0, "assignedPosition": "jungle" }
          ],
          "theirTeam": [ { "cellId": 5, "championId": 238, "championPickIntent": 0, "assignedPosition": "middle" } ],
          "bans": { "myTeamBans": [1, 2], "theirTeamBans": [3] },
          "timer": { "phase": "BAN_PICK", "adjustedTimeLeftInPhase": 25000 }
        }
        """;

        var session = JsonSerializer.Deserialize<ChampSelectSession>(json, LiveGameParser.Options)!;

        Assert.Equal(2, session.MyTeam.Count);
        Assert.Single(session.TheirTeam);
        Assert.Equal(new[] { 1, 2 }, session.Bans.MyTeamBans);
        Assert.Equal("BAN_PICK", session.Timer.Phase);

        // localPlayer aún no confirma: se muestra el intent (103).
        Assert.Equal(103, session.LocalPlayer!.DisplayChampionId);
        // el jungla ya confirmó: se muestra el championId (64).
        Assert.Equal(64, session.MyTeam[1].DisplayChampionId);
    }

    [Fact]
    public void ChampSelect_Parse_MapsAramBench()
    {
        const string json = """
        {
          "localPlayerCellId": 0,
          "benchEnabled": true,
          "benchChampions": [ { "championId": 32 }, { "championId": 16 } ],
          "myTeam": [ { "cellId": 0, "championId": 222 } ]
        }
        """;

        var session = JsonSerializer.Deserialize<ChampSelectSession>(json, LiveGameParser.Options)!;

        Assert.True(session.BenchEnabled);
        Assert.Equal(new[] { 32, 16 }, session.BenchChampions.Select(b => b.ChampionId));
    }

    [Fact]
    public void Gameflow_Parse_ExtractsQueueId()
    {
        const string json = """
        { "phase": "InProgress", "gameData": { "queue": { "id": 2400 } } }
        """;
        var session = JsonSerializer.Deserialize<GameflowSession>(json, LiveGameParser.Options)!;
        Assert.Equal(2400, session.QueueId);

        var empty = JsonSerializer.Deserialize<GameflowSession>("{}", LiveGameParser.Options)!;
        Assert.Equal(-1, empty.QueueId);
    }
}
