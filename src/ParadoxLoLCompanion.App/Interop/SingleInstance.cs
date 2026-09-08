using System.Diagnostics;
using System.Threading;

namespace ParadoxLoLCompanion.App.Interop;

/// <summary>
/// Guarda de instancia única. Dos copias de la app corriendo a la vez duplican TODO:
/// dos overlays superpuestos al morir, dos bucles de OCR peleando por la CPU y dos
/// escrituras de páginas de items/runas al cliente. Se resuelve con un mutex con nombre;
/// el segundo arranque le avisa al primero que se muestre y se va sin hacer ruido.
///
/// Caso especial: tras un auto-update, la instancia vieja sigue viva unos instantes
/// mientras se apaga, así que la relanzada espera a que muera (ver <see cref="Acquire"/>).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\ParadoxLoLCompanion.SingleInstance";
    private const string ActivateEventName = @"Local\ParadoxLoLCompanion.Activate";

    private readonly Mutex _mutex;
    private EventWaitHandle? _activateSignal;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// Toma la titularidad de la instancia única, o devuelve <c>null</c> si ya hay otra viva.
    /// <paramref name="wait"/> es cuánto esperar a que la otra suelte el mutex: cero en un
    /// arranque normal (respuesta inmediata) y unos segundos cuando venimos de un update.
    /// </summary>
    public static SingleInstance? Acquire(TimeSpan wait)
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // La instancia anterior murió sin soltarlo (crash o kill): el mutex es nuestro.
            owned = true;
        }

        if (owned)
            return new SingleInstance(mutex);

        mutex.Dispose();
        return null;
    }

    /// <summary>Espera a que termine el proceso indicado (la instancia vieja de un update).</summary>
    public static void WaitForProcessExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            // Ya no existe: justo lo que queríamos.
        }
    }

    /// <summary>
    /// Queda a la escucha del pedido "mostrate" que manda un segundo arranque.
    /// El callback llega en un hilo del pool: marshalear a la UI es del llamador.
    /// </summary>
    public void ListenForActivation(Action onActivate)
    {
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activateSignal, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Le pide a la instancia ya abierta que traiga su ventana al frente.</summary>
    public static void SignalExisting()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var signal))
            {
                using (signal)
                    signal.Set();
            }
        }
        catch
        {
            // Sin permisos o sin la otra instancia: no hay a quién avisarle, y ya nos vamos.
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activateSignal?.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // No éramos el dueño (ya liberado en el apagado): nada que hacer.
        }
        _mutex.Dispose();
    }
}
