using System.Text;

namespace ParadoxLoLCompanion.Core.Connectors.Lcu;

/// <summary>
/// Credenciales para hablar con la LCU API, obtenidas del <c>lockfile</c> del cliente.
/// Formato del lockfile: <c>LeagueClient:PID:PORT:PASSWORD:PROTOCOL</c>.
/// </summary>
public sealed record LcuCredentials(int Pid, int Port, string Password, string Protocol)
{
    /// <summary>URL base, p. ej. <c>https://127.0.0.1:54321</c>.</summary>
    public string BaseUrl => $"{Protocol}://127.0.0.1:{Port}";

    /// <summary>Header <c>Authorization</c> con auth básica (usuario fijo "riot").</summary>
    public string BasicAuthHeader
    {
        get
        {
            var raw = Encoding.UTF8.GetBytes($"riot:{Password}");
            return "Basic " + Convert.ToBase64String(raw);
        }
    }
}
