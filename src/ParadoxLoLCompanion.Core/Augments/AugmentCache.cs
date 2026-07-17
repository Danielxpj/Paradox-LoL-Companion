using System.Text.Json;

namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>
/// Caché en disco del tier list, un JSON por parche. Misma filosofía que
/// StatsCache: IO tolerante a fallos (error = miss), poda de parches viejos,
/// y caducidad dentro del parche (Blitz retoca el ranking a mano).
/// </summary>
public sealed class AugmentCache
{
    private readonly string _baseDir;

    public TimeSpan MaxAge { get; init; } = TimeSpan.FromHours(48);

    public AugmentCache(string? baseDir = null) =>
        _baseDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParadoxLoLCompanion", "augments");

    public bool TryRead(string patch, out AugmentTierList? list)
    {
        list = null;
        try
        {
            var path = FilePath(patch);
            if (!File.Exists(path))
                return false;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > MaxAge)
                return false;
            list = JsonSerializer.Deserialize<AugmentTierList>(File.ReadAllText(path));
            return list is { Augments.Count: > 0 };
        }
        catch
        {
            return false;
        }
    }

    public void Write(string patch, AugmentTierList list)
    {
        try
        {
            var path = FilePath(patch);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(list));
            PruneOtherPatches(patch);
        }
        catch
        {
            // Sin caché seguimos; solo cuesta re-descargar.
        }
    }

    /// <summary>Borra los directorios de parches viejos (la caché no crece sin límite).</summary>
    private void PruneOtherPatches(string currentPatch)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(_baseDir))
                if (!string.Equals(Path.GetFileName(dir), currentPatch, StringComparison.Ordinal))
                    Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Limpieza oportunista: si falla, no pasa nada.
        }
    }

    private string FilePath(string patch) =>
        Path.Combine(_baseDir, patch, "mayhem-augments.json");
}
