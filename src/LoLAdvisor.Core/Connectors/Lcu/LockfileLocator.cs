using System.Diagnostics;
using System.Management;

namespace LoLAdvisor.Core.Connectors.Lcu;

/// <summary>
/// Descubre las <see cref="LcuCredentials"/> del cliente de League. No depende de la
/// ruta de instalación: usa (en orden) los argumentos de línea de comando del proceso
/// <c>LeagueClientUx</c>, luego el <c>lockfile</c> derivado del ejecutable en ejecución,
/// y por último rutas de instalación conocidas. El parseo está aislado para testearlo.
/// </summary>
public sealed class LockfileLocator
{
    private static readonly string[] DefaultInstallDirs =
    {
        @"C:\Riot Games\League of Legends",
        @"D:\Riot Games\League of Legends",
        @"C:\Program Files\Riot Games\League of Legends",
        @"C:\Program Files (x86)\Riot Games\League of Legends",
    };

    private readonly IReadOnlyList<string> _installDirs;

    public LockfileLocator(IEnumerable<string>? installDirs = null)
        => _installDirs = installDirs?.ToList() ?? DefaultInstallDirs.ToList();

    /// <summary>
    /// Obtiene las credenciales de la LCU probando, en orden: (1) los argumentos de
    /// línea de comando del proceso, (2) el lockfile derivado del ejecutable en
    /// ejecución, (3) rutas de instalación conocidas. <c>null</c> si no hay cliente.
    /// </summary>
    public LcuCredentials? Locate()
    {
        return LocateFromProcessCommandLine()
            ?? LocateFromRunningProcess()
            ?? LocateFromKnownPaths();
    }

    /// <summary>
    /// Método canónico (el que usan la mayoría de las herramientas): lee los argumentos
    /// del proceso <c>LeagueClientUx</c> (<c>--app-port</c> y <c>--remoting-auth-token</c>),
    /// que dan puerto y token directamente sin tocar el disco. Independiente de la ruta.
    /// </summary>
    private static LcuCredentials? LocateFromProcessCommandLine()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE Name = 'LeagueClientUx.exe'");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                using (obj)
                {
                    if (obj["CommandLine"] is string cmd && TryParseCommandLine(cmd, out var creds))
                        return creds;
                }
            }
        }
        catch
        {
            // WMI no disponible / sin permisos: se usan los métodos de respaldo
        }
        return null;
    }

    /// <summary>
    /// Encuentra el proceso del cliente en ejecución, deriva su carpeta de instalación
    /// desde el ejecutable y lee el lockfile de ahí. Robusto a cualquier ruta de instalación.
    /// </summary>
    private static LcuCredentials? LocateFromRunningProcess()
    {
        foreach (var name in new[] { "LeagueClientUx", "LeagueClient" })
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var proc in processes)
            {
                try
                {
                    var exePath = proc.MainModule?.FileName;
                    var dir = string.IsNullOrEmpty(exePath) ? null : Path.GetDirectoryName(exePath);
                    if (dir is null)
                        continue;
                    var creds = ReadLockfile(Path.Combine(dir, "lockfile"));
                    if (creds is not null)
                        return creds;
                }
                catch
                {
                    // acceso denegado / diferencia de bitness: probar el siguiente proceso
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        return null;
    }

    private LcuCredentials? LocateFromKnownPaths()
    {
        foreach (var dir in _installDirs)
        {
            var creds = ReadLockfile(Path.Combine(dir, "lockfile"));
            if (creds is not null)
                return creds;
        }
        return null;
    }

    private static LcuCredentials? ReadLockfile(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            // El cliente mantiene el lockfile abierto; hay que leerlo compartido.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            return TryParse(content, out var creds) ? creds : null;
        }
        catch (IOException)
        {
            return null; // en uso / bloqueado: se reintenta en el próximo ciclo
        }
    }

    /// <summary>
    /// Extrae las credenciales de la línea de comando del cliente, buscando los
    /// argumentos <c>--app-port=</c> y <c>--remoting-auth-token=</c>.
    /// </summary>
    public static bool TryParseCommandLine(string commandLine, out LcuCredentials credentials)
    {
        credentials = null!;
        if (string.IsNullOrEmpty(commandLine))
            return false;

        var portStr = ExtractArg(commandLine, "--app-port=");
        var token = ExtractArg(commandLine, "--remoting-auth-token=");
        if (!int.TryParse(portStr, out var port) || string.IsNullOrEmpty(token))
            return false;

        // El Pid no viene aquí; no se usa para conectar. Protocolo siempre https.
        credentials = new LcuCredentials(0, port, token, "https");
        return true;
    }

    private static string? ExtractArg(string cmd, string key)
    {
        var idx = cmd.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
            return null;
        var start = idx + key.Length;
        if (start < cmd.Length && cmd[start] == '"')
            start++;
        var end = start;
        while (end < cmd.Length && cmd[end] != '"' && cmd[end] != ' ')
            end++;
        return end > start ? cmd.Substring(start, end - start) : null;
    }

    /// <summary>Parsea el contenido del lockfile (<c>LeagueClient:PID:PORT:PASSWORD:PROTOCOL</c>).</summary>
    public static bool TryParse(string content, out LcuCredentials credentials)
    {
        credentials = null!;
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var parts = content.Trim().Split(':');
        if (parts.Length != 5)
            return false;

        if (!int.TryParse(parts[1], out var pid))
            return false;
        if (!int.TryParse(parts[2], out var port))
            return false;

        var password = parts[3];
        var protocol = parts[4];
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(protocol))
            return false;

        credentials = new LcuCredentials(pid, port, password, protocol);
        return true;
    }
}
