using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Core.Connectors;

/// <summary>
/// Fuente de estado de partida en vivo. La implementa tanto el conector real
/// (<c>LiveClientConnector</c>) como la fuente de replay para desarrollo.
/// </summary>
public interface IGameDataSource
{
    /// <summary>Nuevo estado parseado + el JSON crudo que lo originó.</summary>
    event Action<GameState, string>? GameStateUpdated;

    /// <summary>Cambio de estado de conexión (+ mensaje opcional).</summary>
    event Action<ConnectionStatus, string?>? StatusChanged;

    /// <summary>Línea de log en texto para el panel de consola.</summary>
    event Action<string>? Log;

    /// <summary>Arranca el bucle de lectura en segundo plano.</summary>
    void Start();

    /// <summary>Detiene el bucle de lectura.</summary>
    Task StopAsync();
}
