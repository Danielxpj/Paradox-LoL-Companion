namespace ParadoxLoLCompanion.Core.Mayhem;

/// <summary>
/// Mantiene "activa" la ventana de pick de augments durante un período de gracia
/// después del respawn. La señal cruda (<c>IsDead</c>) se apaga al revivir, pero
/// el picker sigue abierto en pantalla hasta que el jugador elige: cerrar el panel
/// en ese instante borraba las recomendaciones justo cuando se iban a usar.
/// </summary>
public sealed class PickWindowTracker
{
    private readonly TimeSpan _grace;
    private DateTime _lastOpenUtc = DateTime.MinValue;

    /// <param name="grace">Cuánto sobrevive la ventana tras revivir (default 60 s:
    /// alcanza para respawn + elegir con calma, sin dejar el panel colgado toda la vida).</param>
    public PickWindowTracker(TimeSpan? grace = null)
        => _grace = grace ?? TimeSpan.FromSeconds(60);

    /// <summary>Alimentar con la señal cruda en cada tick; devuelve si la ventana
    /// debe considerarse activa (muerto ahora, o dentro de la gracia post-respawn).</summary>
    public bool Update(bool pickWindowNow, DateTime utcNow)
    {
        if (pickWindowNow)
        {
            _lastOpenUtc = utcNow;
            return true;
        }
        return utcNow - _lastOpenUtc <= _grace;
    }

    /// <summary>Cierra la ventana ya (fin de partida / dejó de ser Mayhem).</summary>
    public void Reset() => _lastOpenUtc = DateTime.MinValue;
}
