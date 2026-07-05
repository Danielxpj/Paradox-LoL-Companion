using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using LoLAdvisor.App.Diagnostics;

namespace LoLAdvisor.App.Update;

/// <summary>
/// Auto-actualización contra GitHub Releases.
///
/// Flujo:
///  1. Lee <c>version.txt</c> (texto plano con la última versión) desde raw.githubusercontent.
///  2. Compara con la versión del ensamblado actual.
///  3. Si hay una más nueva, descarga el exe de la última Release y lo intercambia
///     "en caliente" (renombra el exe en ejecución a <c>.old</c> — permitido en Windows —,
///     deja el nuevo en su lugar, relanza y cierra la instancia actual).
///
/// Todo es *fail-open*: cualquier error (sin internet, permisos, timeout) se registra y
/// la app arranca normal con la versión que ya tenía. El objetivo es que la actualización
/// nunca impida usar la app.
/// </summary>
public static class Updater
{
    // --- Configuración de hosting (GitHub Releases del repo) ---
    private const string Owner = "Danielxpj";
    private const string Repo = "Paradox-LoL-Companion";
    private const string Branch = "main";
    private const string AssetName = "ParadoxLoLCompanion.exe";

    private static string VersionUrl =>
        $"https://raw.githubusercontent.com/{Owner}/{Repo}/{Branch}/version.txt";

    // "releases/latest/download/<asset>" siempre apunta a la Release más reciente.
    private static string DownloadUrl =>
        $"https://github.com/{Owner}/{Repo}/releases/latest/download/{AssetName}";

    /// <summary>Elimina restos <c>*.old</c> de una actualización previa (ya se puede borrar).</summary>
    public static void CleanupOldVersions()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return;
            var dir = Path.GetDirectoryName(exePath);
            if (dir is null)
                return;
            foreach (var stale in Directory.EnumerateFiles(dir, "*.old"))
            {
                try { File.Delete(stale); }
                catch { /* si sigue tomado, se limpia el próximo arranque */ }
            }
        }
        catch (Exception ex) { FileLog.WriteException("Updater.CleanupOldVersions", ex); }
    }

    /// <summary>
    /// Chequea si hay actualización y, de haberla, la descarga y aplica.
    /// Devuelve <c>true</c> si se aplicó y la app se está relanzando (el llamador debe salir sin
    /// abrir la ventana principal). Devuelve <c>false</c> para continuar el arranque normal.
    /// </summary>
    public static async Task<bool> TryUpdateAsync(Dispatcher dispatcher)
    {
        // En desarrollo (con depurador) no auto-actualizamos.
        if (Debugger.IsAttached)
            return false;

        try
        {
            var current = CurrentVersion();
            var latest = await FetchLatestVersionAsync();
            if (latest is null)
                return false;

            FileLog.Write($"[Update] actual={current} remota={latest}");
            if (Normalize(latest) <= Normalize(current))
                return false;

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                FileLog.Write("[Update] no se pudo resolver el exe en ejecución; se omite.");
                return false;
            }

            var window = new UpdateWindow(current, latest);
            window.Show();
            try
            {
                var newPath = exePath + ".new";
                await DownloadFileAsync(DownloadUrl, newPath, window);

                window.SetStatus("Aplicando actualización…");
                SwapExecutable(exePath, newPath);

                window.SetStatus("Reiniciando…");
                Relaunch(exePath);
                return true;
            }
            catch (Exception ex)
            {
                FileLog.WriteException("Updater.TryUpdateAsync/apply", ex);
                window.Close();
                // fail-open: seguimos con la versión actual
                return false;
            }
        }
        catch (Exception ex)
        {
            FileLog.WriteException("Updater.TryUpdateAsync", ex);
            return false;
        }
    }

    private static Version CurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    private static async Task<Version?> FetchLatestVersionAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ParadoxLoLCompanion-Updater");
            var text = await http.GetStringAsync(VersionUrl);
            var token = FirstToken(text);
            return token is not null && Version.TryParse(token, out var v) ? v : null;
        }
        catch (Exception ex)
        {
            // Sin internet / repo inaccesible: no es un error, simplemente no hay update.
            FileLog.Write($"[Update] no se pudo leer version.txt: {ex.Message}");
            return null;
        }
    }

    /// <summary>Primer token no vacío del texto (ignora comentarios '#' y espacios).</summary>
    private static string? FirstToken(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var space = line.IndexOfAny(new[] { ' ', '\t', '\r' });
            return space > 0 ? line[..space] : line;
        }
        return null;
    }

    /// <summary>Normaliza a 4 componentes no negativos para comparar sin sorpresas (1.0 vs 1.0.0.0).</summary>
    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    private static async Task DownloadFileAsync(string url, string destPath, UpdateWindow window)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ParadoxLoLCompanion-Updater");

        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n));
            read += n;
            if (total is > 0)
                window.SetProgress((double)read / total.Value);
            else
                window.SetIndeterminate($"Descargando… {read / (1024 * 1024)} MB");
        }
    }

    /// <summary>
    /// Intercambia el exe en ejecución por el descargado. Windows permite renombrar (no borrar)
    /// un exe en uso, así que movemos el actual a <c>.old</c> y dejamos el nuevo en su lugar.
    /// Si algo falla a mitad de camino, revierte para no dejar la instalación rota.
    /// </summary>
    private static void SwapExecutable(string exePath, string newPath)
    {
        var oldPath = exePath + ".old";
        if (File.Exists(oldPath))
        {
            try { File.Delete(oldPath); } catch { /* se limpia luego */ }
        }

        File.Move(exePath, oldPath);              // renombra el exe en uso (permitido)
        try
        {
            File.Move(newPath, exePath);          // el nuevo toma el lugar del original
        }
        catch
        {
            // rollback: devolver el original a su sitio
            try { File.Move(oldPath, exePath); } catch { /* ya se registró arriba */ }
            throw;
        }
    }

    private static void Relaunch(string exePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
        };
        Process.Start(psi);
        Application.Current.Shutdown();
    }
}
