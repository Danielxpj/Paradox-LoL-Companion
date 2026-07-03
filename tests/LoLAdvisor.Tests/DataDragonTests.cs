using LoLAdvisor.Core.DataDragon;

namespace LoLAdvisor.Tests;

public class DataDragonTests
{
    private const string Champions = """
    {
      "data": {
        "Aatrox": { "key": "266", "id": "Aatrox", "name": "Aatrox", "tags": ["Fighter","Tank"], "info": {"attack":8,"defense":4,"magic":3} },
        "Ahri":   { "key": "103", "id": "Ahri",   "name": "Ahri",   "tags": ["Mage","Assassin"], "info": {"attack":3,"defense":4,"magic":8} },
        "Jinx":   { "key": "222", "id": "Jinx",   "name": "Jinx",   "tags": ["Marksman"], "info": {"attack":9,"defense":2,"magic":4} }
      }
    }
    """;

    private const string Items = """
    {
      "data": {
        "3075": { "name": "Cota de espinas", "description": "Inflige Heridas graves al atacar", "gold": {"total":2700,"purchasable":true}, "tags":["Armor","Health"], "maps":{"11":true}, "from":["1029","1011"], "depth":3 },
        "3165": { "name": "Morellonomicon", "description": "Aplica Heridas graves", "gold": {"total":2500,"purchasable":true}, "tags":["SpellDamage","Health"], "maps":{"11":true}, "from":["3916","1052"], "depth":3 },
        "3089": { "name": "Sombrero de Rabadon", "description": "Aumenta poder de habilidad", "gold": {"total":3600,"purchasable":true}, "tags":["SpellDamage"], "maps":{"11":true}, "from":["1058","1052"], "depth":3 },
        "3020": { "name": "Botas de hechicero", "description": "", "gold": {"total":1100,"purchasable":true}, "tags":["Boots"], "maps":{"11":true} },
        "3143": { "name": "Presagio de Randuin", "description": "Reduce el daño de los golpes críticos que recibes en un 25%", "gold": {"total":2700,"purchasable":true}, "tags":["Armor","Health"], "maps":{"11":true}, "from":["1029","1011"], "depth":3 },
        "3031": { "name": "Filo Infinito", "description": "Tus golpes críticos infligen 40% más de daño", "gold": {"total":3400,"purchasable":true}, "tags":["Damage","CriticalStrike"], "maps":{"11":true}, "from":["1038","1018"], "depth":3 },
        "1055": { "name": "Hoja de Doran", "description": "", "gold": {"total":450,"purchasable":true}, "tags":["AttackDamage","LifeSteal"], "maps":{"11":true} }
      }
    }
    """;

    public static DataDragonCatalog Catalog() => DataDragonCatalog.FromJson("14.1.1", Champions, Items);

    [Fact]
    public void ChampionNameById_Resolves()
    {
        var c = Catalog();
        Assert.Equal("Aatrox", c.ChampionNameById(266));
        Assert.Equal("Jinx", c.ChampionNameById(222));
        Assert.Null(c.ChampionNameById(9999));
    }

    [Fact]
    public void ChampionInfoAndKey_AreParsed()
    {
        var c = Catalog();
        var ahri = c.ChampionByKey("Ahri");
        Assert.NotNull(ahri);
        Assert.Equal(8, ahri!.Info.Magic);
        Assert.Equal("Mage", ahri.PrimaryTag);
    }

    [Fact]
    public void ResolveChampion_UsesRawNameSuffix_ThenLocalizedName()
    {
        var c = Catalog();
        Assert.Equal("Ahri", c.ResolveChampion(null, "game_character_displayname_Ahri")!.Name);
        Assert.Equal("Jinx", c.ResolveChampion("Jinx", null)!.Name);
        Assert.Null(c.ResolveChampion("NoExiste", "game_character_displayname_NoExiste"));
    }

    [Fact]
    public void CompletedItemsByTag_FiltersAndSorts()
    {
        var armor = Catalog().CompletedItemsByTag("Armor");
        Assert.Contains(armor, i => i.Name == "Cota de espinas");
        Assert.DoesNotContain(armor, i => i.HasTag("Boots"));
    }

    [Fact]
    public void CompletedItems_ExcludeBootsAndCheapComponents()
    {
        var all = Catalog().CompletedItemsByTag("AttackDamage");
        // Hoja de Doran (450, componente barato) y botas quedan fuera de "items completos".
        Assert.DoesNotContain(all, i => i.Id == 1055);
        Assert.DoesNotContain(all, i => i.Id == 3020);
    }

    [Fact]
    public void CompletedItems_ExcludeModeItemsWithoutBuildPath()
    {
        var c = TestCatalog.Catalog();
        // Espátula Dorada / Inmolación del Vacío: ddragon los marca comprables en ARAM,
        // pero dependen de perks del modo y no tienen árbol de construcción.
        Assert.DoesNotContain(c.CompletedItemsFor(12), i => i.Id == 994403);
        Assert.DoesNotContain(c.CompletedItemsFor(12), i => i.Id == 223069);
        // Lo mismo con las variantes de modos rotativos marcadas en el mapa 11 (Céfiro).
        Assert.DoesNotContain(c.CompletedItems, i => i.Id == 663172);
        // Los items reales (con árbol de construcción) siguen presentes.
        Assert.Contains(c.CompletedItems, i => i.Id == 3031);
        Assert.Contains(c.CompletedItemsFor(12), i => i.Id == 3153);
    }

    [Fact]
    public void FinishedBoots_AreTierTwo_NotPerkGatedUpgrades()
    {
        var c = TestCatalog.Catalog();
        // Las tier 2 ahora construyen hacia la tier 3 (Hazañas de Fuerza), pero siguen
        // siendo las botas terminadas que cualquiera puede comprar.
        Assert.Contains(c.FinishedBoots, b => b.Id == 3111);
        // La tier 3 depende del perk de equipo: nunca se recomienda.
        Assert.DoesNotContain(c.FinishedBoots, b => b.Id == 3173);
        // En ARAM la tier 3 no existe: la lista no debe quedar vacía.
        Assert.Contains(c.FinishedBootsFor(12), b => b.Id == 3111);
    }

    [Fact]
    public void GrievousWounds_DetectedFromDescription()
    {
        var gw = Catalog().CompletedGrievousWoundsItems();
        Assert.Contains(gw, i => i.Name == "Cota de espinas");
        Assert.Contains(gw, i => i.Name == "Morellonomicon");
        Assert.DoesNotContain(gw, i => i.Name == "Sombrero de Rabadon");
    }

    [Fact]
    public void CritReduction_DetectedFromDescription_WithoutFalsePositiveOnCritItems()
    {
        var c = Catalog();
        // Presagio de Randuin reduce el daño crítico → anti-crit.
        Assert.True(c.ItemById(3143)!.ReducesCritDamage);
        // Filo Infinito habla de golpes críticos pero los POTENCIA: no debe marcarse.
        Assert.False(c.ItemById(3031)!.ReducesCritDamage);
        // Un item sin mención de crítico tampoco.
        Assert.False(c.ItemById(3165)!.ReducesCritDamage);
    }

    [Fact]
    public void Empty_IsNotLoaded()
    {
        Assert.False(DataDragonCatalog.Empty.IsLoaded);
        Assert.Null(DataDragonCatalog.Empty.ChampionNameById(266));
    }

    [Fact]
    public async Task LoadCached_ReturnsNull_WhenNoCache()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ddragon-test-" + Guid.NewGuid());
        var client = new DataDragonClient(cacheDir: dir);
        Assert.Null(await client.LoadCachedAsync());
    }

    [Fact]
    public async Task LoadCached_LoadsLatestVersionFromDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ddragon-test-" + Guid.NewGuid());
        WriteCache(dir, "14.1.1");
        WriteCache(dir, "16.13.1"); // debe elegir la más nueva
        try
        {
            var client = new DataDragonClient(cacheDir: dir);
            var catalog = await client.LoadCachedAsync();

            Assert.NotNull(catalog);
            Assert.Equal("16.13.1", catalog!.Version);
            Assert.Equal("Aatrox", catalog.ChampionNameById(266));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void WriteCache(string root, string version)
    {
        // El caché es por versión + idioma (en_US por defecto).
        var dir = Path.Combine(root, version, "en_US");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "champion.json"), Champions);
        File.WriteAllText(Path.Combine(dir, "item.json"), Items);
    }
}
