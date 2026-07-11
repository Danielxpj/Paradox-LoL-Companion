namespace ParadoxLoLCompanion.Core.Models;

/// <summary>
/// Raíz del payload <c>allgamedata</c> de la Live Client Data API.
/// Representa el estado completo de la partida en un instante.
/// </summary>
public sealed class GameState
{
    public ActivePlayer? ActivePlayer { get; set; }
    public List<Player> AllPlayers { get; set; } = new();
    public GameEvents Events { get; set; } = new();
    public GameData GameData { get; set; } = new();

    /// <summary>
    /// Encuentra en <see cref="AllPlayers"/> la entrada del jugador activo. En parches
    /// recientes <c>summonerName</c> viene vacío y hay que emparejar por <c>riotId</c>,
    /// así que se intenta primero por riotId y luego por summonerName.
    /// </summary>
    public Player? ActivePlayerEntry
    {
        get
        {
            var active = ActivePlayer;
            if (active is null)
                return null;

            if (!string.IsNullOrEmpty(active.RiotId))
            {
                var byRiot = AllPlayers.Find(p =>
                    string.Equals(p.RiotId, active.RiotId, StringComparison.OrdinalIgnoreCase));
                if (byRiot is not null)
                    return byRiot;
            }

            if (!string.IsNullOrEmpty(active.SummonerName))
            {
                var byName = AllPlayers.Find(p =>
                    string.Equals(p.SummonerName, active.SummonerName, StringComparison.OrdinalIgnoreCase));
                if (byName is not null)
                    return byName;
            }

            return null;
        }
    }
}

/// <summary>Datos generales de la partida (<c>gameData</c>).</summary>
public sealed class GameData
{
    public string GameMode { get; set; } = "";
    /// <summary>Tiempo de partida en segundos.</summary>
    public double GameTime { get; set; }
    public string MapName { get; set; } = "";
    public int MapNumber { get; set; }
    public string MapTerrain { get; set; } = "";

    /// <summary>Tiempo de partida expresado en minutos.</summary>
    public double GameTimeMinutes => GameTime / 60.0;
}
