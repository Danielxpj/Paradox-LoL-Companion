namespace ParadoxLoLCompanion.Core.Models;

/// <summary>
/// Recorte mínimo de <c>/lol-gameflow/v1/session</c> (LCU): fase y cola actual.
/// Sirve para distinguir modos que la Live Client API no diferencia (p.ej. ARAM 450
/// vs. ARAM: Mayhem 2400, ambos en el mapa 12).
/// </summary>
public sealed class GameflowSession
{
    public string Phase { get; set; } = "";
    public GameflowGameData GameData { get; set; } = new();

    /// <summary>Id de cola actual, o -1 si no hay partida/lobby.</summary>
    public int QueueId => GameData.Queue.Id;
}

public sealed class GameflowGameData
{
    public GameflowQueue Queue { get; set; } = new();
}

public sealed class GameflowQueue
{
    public int Id { get; set; } = -1;
}
