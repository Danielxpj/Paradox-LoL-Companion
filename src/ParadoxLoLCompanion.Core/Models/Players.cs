namespace ParadoxLoLCompanion.Core.Models;

/// <summary>Jugador activo (<c>activePlayer</c>): los datos privados del que corre el cliente.</summary>
public sealed class ActivePlayer
{
    public string SummonerName { get; set; } = "";
    /// <summary>Riot ID completo "Nombre#TAG" (parches recientes).</summary>
    public string RiotId { get; set; } = "";
    public string RiotIdGameName { get; set; } = "";
    public int Level { get; set; }
    public double CurrentGold { get; set; }
    public ChampionStats ChampionStats { get; set; } = new();
}

/// <summary>Estadísticas del campeón activo (<c>championStats</c>). Subconjunto usado en v1.</summary>
public sealed class ChampionStats
{
    public double CurrentHealth { get; set; }
    public double MaxHealth { get; set; }
    public double ResourceValue { get; set; }
    public double ResourceMax { get; set; }
    public double AttackDamage { get; set; }
    public double AbilityPower { get; set; }
    public double Armor { get; set; }
    public double MagicResist { get; set; }
    public double MoveSpeed { get; set; }
}

/// <summary>Entrada de <c>allPlayers</c>: datos públicos de cada jugador de la partida.</summary>
public sealed class Player
{
    public string SummonerName { get; set; } = "";
    /// <summary>Riot ID completo "Nombre#TAG" (parches recientes).</summary>
    public string RiotId { get; set; } = "";
    public string RiotIdGameName { get; set; } = "";
    public string ChampionName { get; set; } = "";
    public string RawChampionName { get; set; } = "";
    public int Level { get; set; }
    /// <summary>Equipo: <c>ORDER</c> (azul) u <c>CHAOS</c> (rojo).</summary>
    public string Team { get; set; } = "";
    public string Position { get; set; } = "";
    public bool IsDead { get; set; }
    public bool IsBot { get; set; }
    public double RespawnTimer { get; set; }
    public PlayerScores Scores { get; set; } = new();
    public List<Item> Items { get; set; } = new();
}

/// <summary>Marcador de un jugador (<c>scores</c>).</summary>
public sealed class PlayerScores
{
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int CreepScore { get; set; }
    public double WardScore { get; set; }

    /// <summary>Representación "K/D/A" para mostrar.</summary>
    public string Kda => $"{Kills}/{Deaths}/{Assists}";
}

/// <summary>Un item en el inventario de un jugador (<c>items</c>).</summary>
public sealed class Item
{
    public int ItemID { get; set; }
    public int Count { get; set; }
    public string DisplayName { get; set; } = "";
    public int Price { get; set; }
    public int Slot { get; set; }
    public bool Consumable { get; set; }
}
