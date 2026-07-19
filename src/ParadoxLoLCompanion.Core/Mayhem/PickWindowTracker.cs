namespace ParadoxLoLCompanion.Core.Mayhem;

/// <summary>
/// Mantiene "activa" la ventana de pick de augments durante un período de gracia
/// después del respawn. La señal cruda (<c>IsDead</c>) se apaga al revivir, pero
/// el picker sigue abierto en pantalla hasta que el jugador elige: cerrar el panel
/// en ese instante borraba las recomendaciones justo cuando se iban a usar.
/// A la inversa, cuando el OCR confirma que la oferta se fue (<see cref="OfferGone"/>,
/// el jugador ya eligió) la ventana se cierra YA y queda suprimida — ni la muerte
/// en curso ni la gracia la reabren — hasta la próxima muerte u oferta nueva
/// (bug real: el panel quedaba un minuto entero visible tras elegir).
/// </summary>
public sealed class PickWindowTracker
{
    private readonly TimeSpan _grace;
    private DateTime _lastOpenUtc = DateTime.MinValue;
    private bool _suppressed;
    private bool _wasDead;

    /// <param name="grace">Cuánto sobrevive la ventana tras revivir (default 60 s:
    /// alcanza para respawn + elegir con calma, sin dejar el panel colgado toda la vida).</param>
    public PickWindowTracker(TimeSpan? grace = null)
        => _grace = grace ?? TimeSpan.FromSeconds(60);

    /// <summary>Alimentar en cada tick; devuelve si la ventana debe considerarse
    /// activa. <paramref name="isDeadNow"/> es la señal dura (muerto ahora);
    /// <paramref name="softOpen"/> agrupa las blandas (ventana inicial de partida,
    /// oferta vista por OCR hace poco). Una muerte NUEVA (transición vivo→muerto)
    /// siempre levanta la supresión: cada muerte es una posible oferta nueva.</summary>
    public bool Update(bool isDeadNow, bool softOpen, DateTime utcNow)
    {
        if (isDeadNow && !_wasDead)
            _suppressed = false;
        _wasDead = isDeadNow;

        if (!_suppressed && (isDeadNow || softOpen))
        {
            _lastOpenUtc = utcNow;
            return true;
        }
        return utcNow - _lastOpenUtc <= _grace;
    }

    /// <summary>El OCR confirmó que la oferta ya no está (el jugador eligió):
    /// cerrar de inmediato y no reabrir hasta nueva muerte u oferta nueva.</summary>
    public void OfferGone()
    {
        _suppressed = true;
        _lastOpenUtc = DateTime.MinValue;
    }

    /// <summary>El OCR volvió a ver cartas ofrecidas: hay oferta, levantar la supresión.</summary>
    public void OfferSeen() => _suppressed = false;

    /// <summary>Cierra la ventana ya (fin de partida / dejó de ser Mayhem).</summary>
    public void Reset()
    {
        _lastOpenUtc = DateTime.MinValue;
        _suppressed = false;
        _wasDead = false;
    }
}
