namespace LoLAdvisor.Tests;

/// <summary>Carga los payloads grabados que se copian al directorio de salida de los tests.</summary>
internal static class Fixtures
{
    private static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string AllGameData() => File.ReadAllText(Path("allgamedata.json"));
}
