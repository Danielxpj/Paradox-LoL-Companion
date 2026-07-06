using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;

namespace ParadoxLoLCompanion.Tests;

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
        "ParadoxLoLCompanion", "ddragon");

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

    [Fact]
    public async Task RealCatalog_ItemAdviceIsCoherent_ForAdFighter()
    {
        var catalog = await new DataDragonClient(cacheDir: CacheDir).LoadCachedAsync();
        if (catalog is null)
            return; // sin caché local

        // Los items nuevos de Heridas dicen "40% Wounds" sin "Grievous": Quimpunk (6609)
        // y Cota de Espinas (3075) deben detectarse igual que Morello/Recordatorio.
        Assert.True(catalog.ItemById(6609)?.AppliesGrievousWounds, "Chempunk Chainsword");
        Assert.True(catalog.ItemById(3075)?.AppliesGrievousWounds, "Thornmail");

        // Las pasivas con nombre (grupos "límite de 1") se parsean del catálogo real.
        Assert.Contains("Cleave", catalog.ItemById(3074)!.PassiveNames);   // Hidra Voraz
        Assert.Contains("Lifeline", catalog.ItemById(3053)!.PassiveNames); // Sterak

        // Darius (físico puro) contra un curador fed: el anti-heal recomendado debe ser
        // de su perfil (Quimpunk/Recordatorio), jamás Morello (bug real reportado).
        var advisor = new ItemAdvisor(catalog);
        var state = TestCatalog.State(3000,
            ("Darius", "ORDER", 2, new int[0]),
            ("Warwick", "CHAOS", 6, new int[0]),
            ("Vladimir", "CHAOS", 4, new int[0]),
            ("Soraka", "CHAOS", 0, new int[0]),
            ("Jinx", "CHAOS", 3, new int[0]),
            ("Malphite", "CHAOS", 1, new int[0]));
        var plan = advisor.Advise(state);
        Assert.NotNull(plan);
        Assert.NotEmpty(plan!.Recommendations);
        Assert.Contains(plan.Recommendations, r => r.Item.AppliesGrievousWounds);
        Assert.DoesNotContain(plan.Recommendations,
            r => r.Item.HasTag("SpellDamage") && !r.Item.HasTag("Damage"));

        // Y nunca dos recomendaciones del mismo grupo excluyente (misma pasiva).
        var passives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in plan.Recommendations)
            foreach (var name in r.Item.PassiveNames)
                Assert.True(passives.Add(name), $"{r.Item.Name} repite la pasiva '{name}'");
    }

    [Fact]
    public async Task RealCatalog_VoidPenGroup_IsExclusive()
    {
        var catalog = await new DataDragonClient(cacheDir: CacheDir).LoadCachedAsync();
        if (catalog is null)
            return; // sin caché local

        // Bug real reportado: teniendo Void Staff, el plan ofrecía Cryptbloom y
        // Bloodletter's Curse (el juego los limita a 1, pero ddragon no expone el
        // grupo: sin pasiva compartida y 8010 ni siquiera sale de Blighting Jewel).
        var advisor = new ItemAdvisor(catalog);
        var state = TestCatalog.State(6000,
            ("Ahri", "ORDER", 3, new[] { 3135 }),               // Void Staff comprado
            ("Malphite", "CHAOS", 2, new[] { 3065 }),           // RM apilada
            ("Galio", "CHAOS", 2, new[] { 3065, 3111 }),
            ("Jinx", "CHAOS", 1, new int[0]),
            ("Soraka", "CHAOS", 0, new int[0]));
        var plan = advisor.Advise(state);

        Assert.NotNull(plan);
        Assert.DoesNotContain(plan!.Recommendations, r => r.Item.Id is 3135 or 3137 or 8010);

        // Y sin tener ninguno, jamás dos del trío en la misma lista.
        var fresh = TestCatalog.State(6000,
            ("Ahri", "ORDER", 3, new int[0]),
            ("Malphite", "CHAOS", 2, new[] { 3065 }),
            ("Galio", "CHAOS", 2, new[] { 3065, 3111 }),
            ("Jinx", "CHAOS", 1, new int[0]),
            ("Soraka", "CHAOS", 0, new int[0]));
        var freshPlan = advisor.Advise(fresh)!;
        Assert.True(freshPlan.Recommendations.Count(r => r.Item.Id is 3135 or 3137 or 8010) <= 1,
            "dos items del grupo del Vacío recomendados juntos");
    }

    [Fact]
    public async Task RealCatalog_TankAdviceIsCoherent_ForShen()
    {
        var catalog = await new DataDragonClient(cacheDir: CacheDir).LoadCachedAsync();
        if (catalog is null)
            return; // sin caché local

        // Shen top contra daño mixto, con Heartsteel core en las stats (como op.gg).
        var advisor = new ItemAdvisor(catalog);
        var state = TestCatalog.State(3200,
            ("Shen", "ORDER", 1, new int[0]),
            ("Darius", "CHAOS", 2, new int[0]),
            ("Ahri", "CHAOS", 2, new int[0]),
            ("Jinx", "CHAOS", 1, new int[0]),
            ("Vladimir", "CHAOS", 2, new int[0]));
        var stats = new Core.Stats.ChampionBuildStats
        {
            ChampionKey = "Shen", Position = "top",
            CoreItems = new Core.Stats.ItemSetStats(
                new[] { 3084, 3068, 3075 }, PickRate: 0.22, Play: 8000, Win: 4200),
        };

        var plan = advisor.Advise(state, null, stats);
        Assert.NotNull(plan);

        // Heartsteel (HP puro, core del campeón) tiene que aparecer en el top.
        Assert.Contains(plan!.Recommendations, r => r.Item.Id == 3084);
        // Y los items de dupla/soporte no van a un laner (Zeke's, Locket, Knight's Vow).
        Assert.DoesNotContain(plan.Recommendations,
            r => r.Item.Id is 3050 or 3109 or 3190 or 3107);
    }
}
