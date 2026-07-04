namespace LoLAdvisor.Tests;

/// <summary>Carga los payloads grabados que se copian al directorio de salida de los tests.</summary>
internal static class Fixtures
{
    private static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string AllGameData() => File.ReadAllText(Path("allgamedata.json"));

    /// <summary>Respuesta JSON-RPC real de lol_get_champion_analysis (Jayce top, 2026-07-04).</summary>
    public static string OpggJayce() => File.ReadAllText(Path("opgg-champion-analysis-jayce.json"));
}
