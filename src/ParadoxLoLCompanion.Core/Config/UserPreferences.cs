using System.Text.Json;

namespace ParadoxLoLCompanion.Core.Config;

/// <summary>
/// Preferencias de la UI que el usuario cambia desde la app (no desde el JSON de reglas):
/// se guardan solas en LocalAppData y sobreviven a la actualización de la app, que sí
/// pisa el advisor-config.json que viene empaquetado.
/// </summary>
public sealed class UserPreferences
{
    /// <summary>Abrir el overlay de recomendaciones automáticamente al morir (ventana de compra).</summary>
    public bool OverlayOnDeath { get; set; } = true;

    /// <summary>Cerrar solo ese overlay a los <see cref="AutoCloseSeconds"/> segundos.</summary>
    public bool AutoCloseOnDeath { get; set; } = true;

    /// <summary>Segundos que el overlay abierto al morir queda en pantalla antes de cerrarse solo.</summary>
    public double AutoCloseSeconds { get; set; } = 5;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>Ruta por defecto: %LOCALAPPDATA%\ParadoxLoLCompanion\user-preferences.json.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ParadoxLoLCompanion", "user-preferences.json");

    /// <summary>
    /// Carga las preferencias; si no hay archivo (o está corrupto) arranca con los defaults,
    /// tomando <paramref name="overlayOnDeathDefault"/> de la config de reglas para no
    /// contradecir lo que ya estaba configurado ahí.
    /// </summary>
    public static UserPreferences Load(string path, bool overlayOnDeathDefault = true)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                if (JsonSerializer.Deserialize<UserPreferences>(json, Options) is { } prefs)
                    return prefs;
            }
        }
        catch
        {
            // archivo ilegible o corrupto: se sigue con los defaults
        }
        return new UserPreferences { OverlayOnDeath = overlayOnDeathDefault };
    }

    /// <summary>Guarda en disco; un fallo de escritura no debe tumbar la UI, solo se reporta.</summary>
    public string? Save(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
