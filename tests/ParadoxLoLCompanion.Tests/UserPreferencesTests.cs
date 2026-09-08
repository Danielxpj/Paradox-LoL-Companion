using ParadoxLoLCompanion.Core.Config;

namespace ParadoxLoLCompanion.Tests;

public class UserPreferencesTests
{
    [Fact]
    public void Load_MissingFile_UsesConfigDefaultForOverlayOnDeath()
    {
        var prefs = UserPreferences.Load("no-existe-user-preferences.json", overlayOnDeathDefault: false);
        Assert.False(prefs.OverlayOnDeath);
        Assert.True(prefs.AutoCloseOnDeath);
        Assert.Equal(5, prefs.AutoCloseSeconds);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsToggles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"paradox-prefs-{Guid.NewGuid():N}.json");
        try
        {
            var saved = new UserPreferences { OverlayOnDeath = false, AutoCloseOnDeath = false };
            Assert.Null(saved.Save(path));

            var loaded = UserPreferences.Load(path, overlayOnDeathDefault: true);
            Assert.False(loaded.OverlayOnDeath);
            Assert.False(loaded.AutoCloseOnDeath);
            Assert.Equal(5, loaded.AutoCloseSeconds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"paradox-prefs-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ no es json");
        try
        {
            var prefs = UserPreferences.Load(path);
            Assert.True(prefs.OverlayOnDeath);
            Assert.True(prefs.AutoCloseOnDeath);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
