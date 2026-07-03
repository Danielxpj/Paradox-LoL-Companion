namespace LoLAdvisor.Core.Connectors;

/// <summary>Estado de una fuente de datos (Live Client o LCU).</summary>
public enum ConnectionStatus
{
    /// <summary>Aún no se ha intentado conectar / detenido.</summary>
    Disconnected,
    /// <summary>El servicio no responde: probablemente no hay partida / cliente abierto.</summary>
    WaitingForGame,
    /// <summary>Conectado y recibiendo datos.</summary>
    Connected,
    /// <summary>Error inesperado (se sigue reintentando).</summary>
    Error,
}
