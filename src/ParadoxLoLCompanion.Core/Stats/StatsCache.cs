using System.Text.Json;

namespace ParadoxLoLCompanion.Core.Stats;

/// <summary>
/// Caché de <see cref="ChampionBuildStats"/> en disco, un JSON por
/// campeón+modo+rol, agrupados por parche. Solo se invalida al cambiar de
/// parche (las stats de un parche no cambian lo bastante para re-consultar).
/// Toda la IO es tolerante a fallos: un error de disco es un miss, no un crash.
/// </summary>
public sealed class StatsCache
{
    private readonly string _baseDir;

    public StatsCache(string? baseDir = null) =>
        _baseDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParadoxLoLCompanion", "stats");

    public bool TryRead(string patch, string championKey, string gameMode, string position,
        out ChampionBuildStats? stats)
    {
        stats = null;
        try
        {
            var path = FilePath(patch, championKey, gameMode, position);
            if (!File.Exists(path))
                return false;
            stats = JsonSerializer.Deserialize<ChampionBuildStats>(File.ReadAllText(path));
            return stats is not null;
        }
        catch
        {
            return false;
        }
    }

    public void Write(string patch, string championKey, string gameMode, string position,
        ChampionBuildStats stats)
    {
        try
        {
            var path = FilePath(patch, championKey, gameMode, position);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(stats));
            PruneOtherPatches(patch);
        }
        catch
        {
            // Sin caché seguimos funcionando; solo costará re-consultar.
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

    private string FilePath(string patch, string championKey, string gameMode, string position) =>
        Path.Combine(_baseDir, patch, $"{championKey}-{gameMode}-{position}.json");
}
