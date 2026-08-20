using System.Text.Json;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

/// <summary>
/// La prueba que importa: los dos casos concretos que el usuario reportó
/// ("nunca le ofrece la Lágrima a Jayce, nunca le ofrece Heartsteel a los tanques"),
/// contra el catálogo REAL de Data Dragon y la caché REAL de op.gg de esta máquina.
///
/// Si falta cualquiera de las dos, pasa en silencio — igual que
/// <see cref="RealCatalogSmokeTests"/>: su valor es cerrar el lazo sobre datos de
/// verdad, no sustituir a las pruebas con catálogo sintético.
/// </summary>
public class MetaSpineRealDataTests
{
    private static string DdragonDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ParadoxLoLCompanion", "ddragon");

    private static string StatsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ParadoxLoLCompanion", "stats");

    /// <summary>La caché de op.gg del campeón en ARAM, del parche más nuevo que haya.</summary>
    private static ChampionBuildStats? CachedAram(string championKey)
    {
        if (!Directory.Exists(StatsDir))
            return null;
        var patchDir = Directory.EnumerateDirectories(StatsDir)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .FirstOrDefault();
        if (patchDir is null)
            return null;
        var path = Path.Combine(patchDir, $"{championKey}-aram-none.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<ChampionBuildStats>(File.ReadAllText(path))
            : null;
    }

    [Fact]
    public async Task Nautilus_IsOfferedHeartsteel_BecauseItIsHisMetaCore()
    {
        var catalog = await new DataDragonClient(cacheDir: DdragonDir).LoadCachedAsync();
        var stats = CachedAram("Nautilus");
        if (catalog is null || stats is null)
            return;

        // El core real cacheado es [3070 Lágrima, 3084 Heartsteel, 3121 Fimbulwinter,
        // 2502 Unending Despair]: si el espinazo funciona, Heartsteel es una de las cartas.
        Assert.Contains(3084, stats.CoreItems!.ItemIds);

        var advisor = new ItemAdvisor(catalog);
        var state = TestCatalog.AramState(2500,
            ("Nautilus", "ORDER", 0, new int[0]),
            ("Jinx", "CHAOS", 4, new int[0]),
            ("Ahri", "CHAOS", 3, new int[0]),
            ("Zed", "CHAOS", 5, new int[0]),
            ("Leona", "CHAOS", 1, new int[0]),
            ("Soraka", "CHAOS", 0, new int[0]));

        var plan = advisor.Advise(state, stats: stats);

        Assert.NotNull(plan);
        Assert.Contains(plan!.Recommendations, r => r.Item.Id == 3084);
        // Y la carta explica que sale de la build real, no de la tabla de tags.
        var heartsteel = plan.Recommendations.First(r => r.Item.Id == 3084);
        Assert.Contains(heartsteel.Reasons, r => r.Contains("core build for Nautilus"));
    }

    [Fact]
    public async Task Jayce_IsOfferedTear_AsTheImmediateBuyTowardHisManaItem()
    {
        var catalog = await new DataDragonClient(cacheDir: DdragonDir).LoadCachedAsync();
        var stats = CachedAram("Jayce");
        if (catalog is null || stats is null)
            return;

        // El core real cacheado trae 3042 Muramana, que ddragon marca no comprable: el
        // puente ItemEvolutions lo lleva a 3004 Manamune, y la Lágrima es su componente.
        Assert.Contains(3042, stats.CoreItems!.ItemIds);

        var advisor = new ItemAdvisor(catalog);
        var state = TestCatalog.AramState(1400,
            ("Jayce", "ORDER", 0, new int[0]),
            ("Garen", "CHAOS", 3, new int[0]),
            ("Lux", "CHAOS", 2, new int[0]),
            ("Ashe", "CHAOS", 4, new int[0]),
            ("Braum", "CHAOS", 0, new int[0]),
            ("Ekko", "CHAOS", 2, new int[0]));

        var plan = advisor.Advise(state, stats: stats);

        Assert.NotNull(plan);
        var manamune = Assert.Single(plan!.Recommendations, r => r.Item.Id == 3004);
        // Con 1400 de oro la regla normal ("el componente más caro que alcanza") elegiría el
        // Martillo de Caulfield (1050) y la Lágrima quedaría para el final, sin stacks.
        Assert.NotNull(manamune.Purchase.NextComponent);
        Assert.Equal(3070, manamune.Purchase.NextComponent!.Id);
    }

    [Fact]
    public async Task EveryCachedChampion_GetsItsMetaCore_InTheRecommendations()
    {
        var catalog = await new DataDragonClient(cacheDir: DdragonDir).LoadCachedAsync();
        if (catalog is null || !Directory.Exists(StatsDir))
            return;

        var advisor = new ItemAdvisor(catalog);
        var checkedChampions = 0;
        foreach (var champion in new[]
                 {
                     "Nautilus", "Alistar", "Garen", "Amumu", "Trundle", "Veigar",
                     "Vladimir", "Karthus", "Kayle", "MissFortune", "Draven", "Mordekaiser",
                 })
        {
            var stats = CachedAram(champion);
            if (stats?.CoreItems is null || stats.CoreItems.ItemIds.Count == 0)
                continue;

            var state = TestCatalog.AramState(3000,
                (champion, "ORDER", 0, new int[0]),
                ("Jinx", "CHAOS", 3, new int[0]),
                ("Ahri", "CHAOS", 3, new int[0]),
                ("Zed", "CHAOS", 3, new int[0]),
                ("Leona", "CHAOS", 1, new int[0]),
                ("Soraka", "CHAOS", 0, new int[0]));

            var plan = advisor.Advise(state, stats: stats);
            Assert.NotNull(plan);
            checkedChampions++;

            // El primer slot del espinazo es la próxima compra del meta: tiene que estar
            // en la lista. (Amumu y Trundle son el caso límite: con 159 y 137 partidas el
            // prior aditivo valía exactamente 0 y su lista era idéntica a la de un campeón
            // sin datos.)
            var expected = MetaSpine.Build(catalog, new Core.Config.ItemsConfig(), stats, 12);
            if (expected.Count == 0)
                continue;
            Assert.Contains(plan!.Recommendations, r => r.Item.Id == expected[0].Item.Id);
            Assert.All(plan.Recommendations, r => Assert.NotEmpty(r.Reasons));
            Assert.True(plan.Recommendations.Count <= 3);
        }

        Assert.True(checkedChampions >= 6, $"esperaba varios campeones cacheados, hubo {checkedChampions}");
    }
}
