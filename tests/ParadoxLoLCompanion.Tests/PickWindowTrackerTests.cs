using ParadoxLoLCompanion.Core.Mayhem;

namespace ParadoxLoLCompanion.Tests;

/// <summary>
/// La ventana de pick NO debe cerrarse en el instante del respawn (el picker sigue
/// abierto hasta elegir), pero SÍ debe cerrarse apenas el OCR confirma que la oferta
/// se fue (el jugador ya eligió) — y quedarse cerrada hasta la PRÓXIMA muerte, sin
/// que la muerte actual ni la gracia post-respawn la reabran (bug real: el panel
/// RECOMMENDED quedaba un minuto entero tras elegir).
/// </summary>
public class PickWindowTrackerTests
{
    private static readonly DateTime T0 = new(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ClosedBeforeFirstDeath()
    {
        var tracker = new PickWindowTracker();
        Assert.False(tracker.Update(isDeadNow: false, softOpen: false, T0));
    }

    [Fact]
    public void OpenWhileDead()
    {
        var tracker = new PickWindowTracker();
        Assert.True(tracker.Update(isDeadNow: true, softOpen: false, T0));
    }

    [Fact]
    public void OpenDuringInitialWindow()
    {
        var tracker = new PickWindowTracker();
        Assert.True(tracker.Update(isDeadNow: false, softOpen: true, T0));
    }

    [Fact]
    public void StaysOpenDuringGraceAfterRespawn()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(isDeadNow: true, softOpen: false, T0);
        Assert.True(tracker.Update(isDeadNow: false, softOpen: false, T0.AddSeconds(30)));
    }

    [Fact]
    public void ClosesOnceGraceExpires()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(isDeadNow: true, softOpen: false, T0);
        Assert.False(tracker.Update(isDeadNow: false, softOpen: false, T0.AddSeconds(61)));
    }

    [Fact]
    public void NewDeathDuringGraceRestartsTheWindow()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(isDeadNow: true, softOpen: false, T0);
        tracker.Update(isDeadNow: false, softOpen: false, T0.AddSeconds(50));
        tracker.Update(isDeadNow: true, softOpen: false, T0.AddSeconds(55));   // murió de nuevo
        Assert.True(tracker.Update(isDeadNow: false, softOpen: false, T0.AddSeconds(110)));
    }

    [Fact]
    public void ResetClosesImmediately()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(isDeadNow: true, softOpen: false, T0);
        tracker.Reset();
        Assert.False(tracker.Update(isDeadNow: false, softOpen: false, T0.AddSeconds(1)));
    }

    [Fact]
    public void OfferGoneClosesImmediatelyEvenWhileStillDead()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(isDeadNow: true, softOpen: false, T0);
        tracker.OfferGone();   // el OCR confirmó que ya eligió
        Assert.False(tracker.Update(isDeadNow: true, softOpen: false, T0.AddSeconds(1)));
    }

    [Fact]
    public void OfferGoneLeavesNoGraceAfterRespawn()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(isDeadNow: true, softOpen: false, T0);
        tracker.OfferGone();
        tracker.Update(isDeadNow: true, softOpen: false, T0.AddSeconds(2));    // sigue muerto
        Assert.False(tracker.Update(isDeadNow: false, softOpen: false, T0.AddSeconds(10)));
    }

    [Fact]
    public void OfferGoneSuppressesSoftSignals()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(isDeadNow: false, softOpen: true, T0);   // ventana inicial
        tracker.OfferGone();
        Assert.False(tracker.Update(isDeadNow: false, softOpen: true, T0.AddSeconds(1)));
    }

    [Fact]
    public void NewDeathAfterPickReopensTheWindow()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(isDeadNow: true, softOpen: false, T0);
        tracker.OfferGone();
        tracker.Update(isDeadNow: false, softOpen: false, T0.AddSeconds(10));  // revivió
        Assert.True(tracker.Update(isDeadNow: true, softOpen: false, T0.AddSeconds(40)));
    }

    [Fact]
    public void OfferSeenReopensAfterGone()
    {
        var tracker = new PickWindowTracker(grace: TimeSpan.FromSeconds(60));
        tracker.Update(isDeadNow: true, softOpen: false, T0);
        tracker.OfferGone();
        tracker.OfferSeen();   // el OCR volvió a ver cartas: hay una oferta nueva
        Assert.True(tracker.Update(isDeadNow: true, softOpen: false, T0.AddSeconds(1)));
    }
}
