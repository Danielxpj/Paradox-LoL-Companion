using LoLAdvisor.Core.Config;
using LoLAdvisor.Core.Items;
using LoLAdvisor.Core.Models;

namespace LoLAdvisor.Tests;

public class ChampionProfilerTests
{
    private static ChampionProfiler Profiler(ItemsConfig? config = null) =>
        new(TestCatalog.Catalog(), config);

    [Theory]
    [InlineData("Ahri", DamageProfile.Magical, BuildArchetype.Mage)]
    [InlineData("Jinx", DamageProfile.Physical, BuildArchetype.Marksman)]
    [InlineData("Zed", DamageProfile.Physical, BuildArchetype.AdAssassin)]
    [InlineData("Aatrox", DamageProfile.Physical, BuildArchetype.AdFighter)]
    [InlineData("Leona", DamageProfile.Mixed, BuildArchetype.Tank)]
    [InlineData("Amumu", DamageProfile.Magical, BuildArchetype.Tank)]
    [InlineData("Soraka", DamageProfile.Magical, BuildArchetype.Enchanter)]
    public void ClassifiesByInfoAndTags(string key, DamageProfile damage, BuildArchetype archetype)
    {
        var champ = TestCatalog.Catalog().ChampionByKey(key);
        var profile = Profiler().Profile(champ);
        Assert.Equal(damage, profile.Damage);
        Assert.Equal(archetype, profile.Archetype);
    }

    [Fact]
    public void PhysicalSupport_BuildsLikeAssassin()
    {
        // Pyke: tag primario Support pero daño físico → lethality, no items de soporte.
        var profile = Profiler().Profile(TestCatalog.Catalog().ChampionByKey("Pyke"));
        Assert.Equal(BuildArchetype.AdAssassin, profile.Archetype);
    }

    [Fact]
    public void DamageOverride_CorrectsMisleadingInfo()
    {
        // Gwen: info.attack 7 / magic 5 diría Mixed, pero hace daño mágico (override por defecto).
        var profile = Profiler().Profile(TestCatalog.Catalog().ChampionByKey("Gwen"));
        Assert.Equal(DamageProfile.Magical, profile.Damage);
        Assert.Equal(BuildArchetype.ApFighter, profile.Archetype);
    }

    [Fact]
    public void ArchetypeOverride_WinsOverTags()
    {
        var config = new ItemsConfig();
        config.ArchetypeOverrides["Jinx"] = "Tank";
        var profile = Profiler(config).Profile(TestCatalog.Catalog().ChampionByKey("Jinx"));
        Assert.Equal(BuildArchetype.Tank, profile.Archetype);
    }

    [Fact]
    public void ResolvesByRawName_WhenLocalizedNameDoesNotMatch()
    {
        // Cliente en otro idioma: ChampionName no coincide, pero el rawName trae el Key.
        var player = new Player
        {
            ChampionName = "NombreQueNoExiste",
            RawChampionName = "game_character_displayname_MonkeyKing",
        };
        var champ = Profiler().Resolve(player);
        Assert.NotNull(champ);
        Assert.Equal("Wukong", champ!.Name);
    }

    [Fact]
    public void UnknownChampion_FallsBackToGenericBruiser()
    {
        var player = new Player { ChampionName = "Desconocido", RawChampionName = "" };
        var profile = Profiler().Profile(player);
        Assert.Equal(ChampionProfile.Fallback, profile);
    }

    // --- Detección de build por inventario ---

    private static Player PlayerWith(string champ, params int[] itemIds)
    {
        var p = new Player
        {
            ChampionName = champ,
            RawChampionName = $"game_character_displayname_{champ}",
        };
        foreach (var id in itemIds)
            p.Items.Add(new Item { ItemID = id, Count = 1 });
        return p;
    }

    [Fact]
    public void TankItems_OverrideTheFighterDefault()
    {
        // "Garen tanque": Aatrox (fighter por defecto) con Thornmail + Sunfire → Tank.
        var profile = Profiler().ProfileWithInventory(PlayerWith("Aatrox", 3075, 3068));

        Assert.Equal(BuildArchetype.Tank, profile.Archetype);
        Assert.True(profile.InferredFromItems);
        Assert.Equal(DamageProfile.Physical, profile.Damage); // el daño es del kit, no cambia
    }

    [Fact]
    public void ApItems_MakeTheEnchanterBuildMage()
    {
        // "Janna AP": Soraka (support por defecto) con Vara + Rabadon → Mage.
        var profile = Profiler().ProfileWithInventory(PlayerWith("Soraka", 1058, 3089));

        Assert.Equal(BuildArchetype.Mage, profile.Archetype);
        Assert.True(profile.InferredFromItems);
    }

    [Fact]
    public void EmptyInventory_KeepsChampionDefault()
    {
        var profile = Profiler().ProfileWithInventory(PlayerWith("Aatrox"));

        Assert.Equal(BuildArchetype.AdFighter, profile.Archetype);
        Assert.False(profile.InferredFromItems);
    }

    [Fact]
    public void SmallContraryItem_DoesNotFlipTheBuild()
    {
        // Una Espada larga (350) no convierte a Ahri en asesina: el prior aguanta.
        var profile = Profiler().ProfileWithInventory(PlayerWith("Ahri", 1036));

        Assert.Equal(BuildArchetype.Mage, profile.Archetype);
        Assert.False(profile.InferredFromItems);
    }

    [Fact]
    public void BootsAndConsumables_DoNotVote()
    {
        var profile = Profiler().ProfileWithInventory(PlayerWith("Ahri", 3111));

        Assert.Equal(BuildArchetype.Mage, profile.Archetype);
        Assert.False(profile.InferredFromItems);
    }

    [Fact]
    public void SellingEverything_SwitchesTheDetectedBuild()
    {
        // Sin estado: la detección sale del inventario actual en cada tick, así que
        // vender la build de tanque y comprar crítico cambia el arquetipo solo.
        var profiler = Profiler();
        var player = PlayerWith("Aatrox", 3075, 3068);
        Assert.Equal(BuildArchetype.Tank, profiler.ProfileWithInventory(player).Archetype);

        player.Items.Clear();
        player.Items.Add(new Item { ItemID = 1038, Count = 1 }); // B.F. Sword
        player.Items.Add(new Item { ItemID = 3031, Count = 1 }); // Infinity Edge
        Assert.Equal(BuildArchetype.Marksman, profiler.ProfileWithInventory(player).Archetype);
    }

    [Fact]
    public void Detection_CanBeDisabledByConfig()
    {
        var config = new ItemsConfig { DetectBuildFromItems = false };
        var profile = Profiler(config).ProfileWithInventory(PlayerWith("Aatrox", 3075, 3068));

        Assert.Equal(BuildArchetype.AdFighter, profile.Archetype);
        Assert.False(profile.InferredFromItems);
    }

    [Fact]
    public void SquishyFlag_MatchesArchetype()
    {
        var profiler = Profiler();
        var catalog = TestCatalog.Catalog();
        Assert.True(profiler.Profile(catalog.ChampionByKey("Jinx")).IsSquishy);
        Assert.True(profiler.Profile(catalog.ChampionByKey("Ahri")).IsSquishy);
        Assert.False(profiler.Profile(catalog.ChampionByKey("Leona")).IsSquishy);
        Assert.False(profiler.Profile(catalog.ChampionByKey("Aatrox")).IsSquishy);
    }
}
