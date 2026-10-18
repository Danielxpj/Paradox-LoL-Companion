using System.Security.Cryptography;

namespace ParadoxLoLCompanion.Core.Update;

/// <summary>
/// Contenido de <c>version.txt</c>: la primera línea útil es <c>&lt;versión&gt; [sha256]</c>.
/// El hash es opcional; si está, el updater lo exige sobre el exe descargado.
/// </summary>
public sealed record UpdateManifest(Version Version, string? Sha256)
{
    /// <summary>
    /// Parsea el texto. Ignora líneas vacías y comentarios con <c>#</c>. Devuelve
    /// <c>null</c> si no hay una versión válida. Un segundo token que no parezca un
    /// SHA-256 (64 hex) se descarta: nunca bloquea la actualización por un typo.
    /// </summary>
    public static UpdateManifest? Parse(string? text)
    {
        if (text is null)
            return null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var hashIdx = line.IndexOf('#');
            if (hashIdx >= 0)
                line = line[..hashIdx].Trim();
            var tokens = line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || !Version.TryParse(tokens[0], out var version))
                return null;
            var sha = tokens.Length > 1 && IsSha256(tokens[1]) ? tokens[1].ToLowerInvariant() : null;
            return new UpdateManifest(version, sha);
        }
        return null;
    }

    public static bool IsSha256(string s) =>
        s.Length == 64 && s.All(Uri.IsHexDigit);
}

/// <summary>
/// Verificación del exe descargado ANTES de intercambiarlo por el que está corriendo.
/// Un portal cautivo (HTML con 200), un proxy que corta la descarga o un asset mal
/// subido pasarían <c>EnsureSuccessStatusCode</c> y dejarían la instalación rota sin
/// forma de auto-repararse: el exe nuevo ni arranca.
/// </summary>
public static class DownloadVerifier
{
    /// <summary>Un exe single-file de WPF self-contained pesa decenas de MB; por debajo es basura.</summary>
    public const long MinPlausibleBytes = 1024 * 1024;

    /// <summary>
    /// Devuelve <c>null</c> si el archivo es aceptable, o la razón del rechazo.
    /// </summary>
    /// <param name="path">Archivo descargado.</param>
    /// <param name="expectedLength">Content-Length anunciado por el servidor, si lo hubo.</param>
    /// <param name="expectedSha256">Hash publicado en <c>version.txt</c>, si lo hay.</param>
    public static string? Verify(string path, long? expectedLength, string? expectedSha256)
    {
        if (!File.Exists(path))
            return "el archivo descargado no existe";

        var length = new FileInfo(path).Length;
        if (expectedLength is > 0 && length != expectedLength.Value)
            return $"tamaño {length} B ≠ Content-Length {expectedLength.Value} B (descarga truncada)";
        if (length < MinPlausibleBytes)
            return $"tamaño {length} B implausible para el exe (¿portal cautivo / página de error?)";

        using var stream = File.OpenRead(path);
        Span<byte> head = stackalloc byte[2];
        if (stream.Read(head) != 2 || head[0] != (byte)'M' || head[1] != (byte)'Z')
            return "no es un ejecutable de Windows (falta la firma MZ)";

        if (expectedSha256 is not null)
        {
            stream.Position = 0;
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                return $"SHA-256 no coincide: esperado {expectedSha256}, obtenido {actual}";
        }
        return null;
    }
}
