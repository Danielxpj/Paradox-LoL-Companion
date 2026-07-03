using LoLAdvisor.Core.DataDragon;
using LoLAdvisor.Core.Items;

namespace LoLAdvisor.Tests;

/// <summary>
/// Prueba de humo contra el catálogo REAL cacheado por la app (Data Dragon en
/// %LocalAppData%). Si no hay caché en esta máquina, pasa en silencio: su valor es
/// validar los supuestos sobre los datos reales (tags, descripciones es_MX, árboles
/// de construcción) donde la app ya se usó.
/// </summary>
public class RealCatalogSmokeTests
{
    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoLAdvisor", "ddragon");

    [Fact]
    public async Task RealCatalog_ProducesFullPlan_ForRealisticGame()
    {
        var catalog = await new DataDragonClient(cacheDir: CacheDir).LoadCachedAsync();
        if (catalog is null)
            return; // sin caché local

        var advisor = new ItemAdvisor(catalog);
        var state = TestCatalog.State(1450,
            ("Ahri", "ORDER", 4, new int[0]),
            ("Zed", "CHAOS", 7, new int[0]),
            ("Warwick", "CHAOS", 6, new[] { 1053 }),
            ("Malphite", "CHAOS", 1, new[] { 1029, 3068 }),
            ("Jinx", "CHAOS", 3, new int[0]),
            ("Leona", "CHAOS", 0, new int[0]));

        var plan = advisor.Advise(state);

        Assert.NotNull(plan);
        Assert.NotEmpty(plan!.Recommendations);
        Assert.All(plan.Recommendations, r => Assert.NotEmpty(r.Reasons));
        Assert.True(plan.Recommendations.Count <= 3);

        // Warwick fed y curador → el plan debe traer un item de Heridas Graves real.
        Assert.Contains(plan.Recommendations, r => r.Item.AppliesGrievousWounds);
        // Ahri es maga: nada de items de crítico/AD puro entre las recomendaciones.
        Assert.DoesNotContain(plan.Recommendations, r => r.Item.HasTag("CriticalStrike"));
        // Sin botas terminadas → hay consejo de botas de un item real.
        Assert.NotNull(plan.Boots);
        Assert.True(plan.Boots!.Boots.IsBoots);

        // La detección por descripción es_MX debe encontrar items reales de cada tipo.
        Assert.NotEmpty(catalog.CompletedGrievousWoundsItems());
        Assert.Contains(catalog.CompletedItems, i => i.RemovesCc);      // Cimitarra Mercurial
        Assert.Contains(catalog.CompletedItems, i => i.BreaksShields);  // Colmillo de Serpiente
        Assert.Contains(catalog.CompletedItems, i => i.ReducesCritDamage); // Presagio de Randuin

        // Items de modos/perks (Espátula Dorada, prismáticos de Arena, mercado negro):
        // ddragon los marca comprables pero no tienen árbol de construcción. Nunca en
        // las listas de recomendables, en ningún mapa.
        foreach (var map in new[] { 11, 12 })
        {
            Assert.All(catalog.CompletedItemsFor(map), i => Assert.NotEmpty(i.From));
            Assert.All(catalog.FinishedBootsFor(map), b => Assert.NotEmpty(b.From));
        }

        // Botas terminadas = tier 2 (comprables siempre), no las tier 3 de Hazañas de
        // Fuerza (3168-3175). En ARAM las tier 3 no existen: la lista no puede ser vacía.
        Assert.Contains(catalog.FinishedBoots, b => b.Id == 3111);
        Assert.DoesNotContain(catalog.FinishedBoots, b => b.Id is >= 3168 and <= 3175);
        Assert.NotEmpty(catalog.FinishedBootsFor(12));

        // Starters reales de ARAM: solo Doran's/Guardian's (tag Lane), sin junglas ni
        // items de quest de support.
        Assert.NotEmpty(catalog.AramStarterItems);
        Assert.All(catalog.AramStarterItems, i => Assert.True(i.HasTag("Lane")));
        Assert.All(catalog.AramStarterItems,
            i => Assert.False(i.HasTag("GoldPer") || i.HasTag("Jungle"), $"{i.Name} no es starter"));
        Assert.Contains(catalog.AramStarterItems, i => i.Id == 3112); // Orbe del Guardián
    }
}
