using ParadoxLoLCompanion.Core.Mayhem;

namespace ParadoxLoLCompanion.Tests;

/// <summary>
/// La ventana de pick NO debe cerrarse en el instante del respawn: el picker de
/// augments sigue abierto en pantalla hasta que el jugador elige. El tracker
/// mantiene la ventana "activa" durante un período de gracia tras revivir
/// (bug real: las recomendaciones OFFERED NOW desaparecían antes de poder elegir).
/// </summary>
public class PickWindowTrackerTests
{
    private static readonly DateTime T0 = new(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ClosedBeforeFirstDeath()
    {
        var tracker = new PickWindowTracker();
        Assert.False(tracker.Update(pickWindowNow: false, T0));
    }

    [Fact]
    public void OpenWhileDead()
    {
        var tracker = new PickWindowTracker();
        Assert.True(tracker.Update(pickWindowNow: true, T0));
    }

    [Fact]
    public void StaysOpenDuringGraceAfterRespawn()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(pickWindowNow: true, T0);
        Assert.True(tracker.Update(pickWindowNow: false, T0.AddSeconds(30)));
    }

    [Fact]
    public void ClosesOnceGraceExpires()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(pickWindowNow: true, T0);
        Assert.False(tracker.Update(pickWindowNow: false, T0.AddSeconds(61)));
    }

    [Fact]
    public void NewDeathDuringGraceRestartsTheWindow()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(pickWindowNow: true, T0);
        tracker.Update(pickWindowNow: false, T0.AddSeconds(50));
        tracker.Update(pickWindowNow: true, T0.AddSeconds(55));   // murió de nuevo
        Assert.True(tracker.Update(pickWindowNow: false, T0.AddSeconds(110)));
    }

    [Fact]
    public void ResetClosesImmediately()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(pickWindowNow: true, T0);
        tracker.Reset();
        Assert.False(tracker.Update(pickWindowNow: false, T0.AddSeconds(1)));
    }
}
