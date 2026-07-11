using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Tests;

public class GameStateTests
{
    [Fact]
    public void ActivePlayerEntry_MatchesSummonerName_CaseInsensitive()
    {
        // riotId vacío (payload viejo/parcial) + casing distinto: el fallback por
        // summonerName debe ser case-insensitive, como el camino por riotId. Si no,
        // el jugador activo no se resuelve y el asesor calla toda la partida.
        var state = new GameState
        {
            ActivePlayer = new ActivePlayer { SummonerName = "XxJinxxX" },
            AllPlayers = { new Player { SummonerName = "xxjinxxx", ChampionName = "Jinx" } },
        };

        Assert.NotNull(state.ActivePlayerEntry);
    }
}
