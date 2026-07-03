using System.IO;

namespace LoLAdvisor.App.Diagnostics;

/// <summary>
/// Log a archivo, robusto (nunca lanza). Escribe la sesión a <c>logs/session.log</c> y
/// vuelca el último <c>allgamedata</c> a <c>logs/last-allgamedata.json</c> para poder
/// diagnosticar crashes con los datos reales que los provocaron.
/// </summary>
public static class FileLog
{
    private static readonly object Gate = new();
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "logs");
    private static string LogPath => Path.Combine(Dir, "session.log");
    private static string RawPath => Path.Combine(Dir, "last-allgamedata.json");

    /// <summary>Ruta del archivo de log (para mostrarla al usuario).</summary>
    public static string LogFilePath => LogPath;

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            lock (Gate)
                File.WriteAllText(LogPath, $"=== Session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
        catch { /* el logging nunca debe romper la app */ }
    }

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch { }
    }

    public static void WriteException(string context, Exception ex)
        => Write($"[EXCEPTION] {context}: {ex}");

    /// <summary>Vuelca el último payload crudo (sobreescribe) para inspección post-mortem.</summary>
    public static void WriteRaw(string json)
    {
        try
        {
            lock (Gate)
                File.WriteAllText(RawPath, json);
        }
        catch { }
    }
}
