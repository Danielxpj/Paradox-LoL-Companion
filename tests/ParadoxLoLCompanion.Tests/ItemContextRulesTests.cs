using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Tests;

/// <summary>
/// Reglas de valor contextual: el crítico que desborda el 100% no puntúa, la vida
/// defiende contra daño mixto (Heartsteel), los items de dupla/soporte no van a laners,
/// la letalidad no es anti-tanque, Mejai's exige ir bien y los starters se venden al
/// llenarse el inventario.
/// </summary>
public class ItemContextRulesTests
{
    private static DataDragonCatalog Catalog() => DataDragonCatalog.FromJson(
        "16.13.1",
        """
        {"data":{
          "Jinx": {"id":"Jinx","key":"222","name":"Jinx","tags":["Marksman"],"info":{"attack":9,"defense":2,"magic":4}},
          "Shen": {"id":"Shen","key":"98","name":"Shen","tags":["Tank"],"info":{"attack":3,"defense":9,"magic":3}},
          "Zed":  {"id":"Zed","key":"238","name":"Zed","tags":["Assassin"],"info":{"attack":9,"defense":2,"magic":1}},
          "Ahri": {"id":"Ahri","key":"103","name":"Ahri","tags":["Mage"],"info":{"attack":3,"defense":4,"magic":8}},
          "Lulu": {"id":"Lulu","key":"117","name":"Lulu","tags":["Support","Mage"],"info":{"attack":2,"defense":3,"magic":7}},
          "Garen":{"id":"Garen","key":"86","name":"Garen","tags":["Fighter","Tank"],"info":{"attack":7,"defense":7,"magic":1}}}}
        """,
        """
        {"data":{
          "1036": {"name":"Long Sword","gold":{"total":350,"sell":245,"purchasable":true},"tags":["Damage"],"maps":{"11":true,"12":true},"into":["9001","9002","9020","9021"],"stats":{"FlatPhysicalDamageMod":10}},
          "1018": {"name":"Cloak of Agility","gold":{"total":600,"sell":420,"purchasable":true},"tags":["CriticalStrike"],"maps":{"11":true,"12":true},"into":["9001","9010"],"stats":{"FlatCritChanceMod":0.15}},
          "1052": {"name":"Amplifying Tome","gold":{"total":435,"sell":305,"purchasable":true},"tags":["SpellDamage"],"maps":{"11":true,"12":true},"into":["9050"],"stats":{"FlatMagicDamageMod":20}},
          "1029": {"name":"Chain Vest","gold":{"total":800,"sell":560,"purchasable":true},"tags":["Armor"],"maps":{"11":true,"12":true},"into":["3068","3190"],"stats":{"FlatArmorMod":40}},
          "1033": {"name":"Null-Magic Mantle","gold":{"total":450,"sell":315,"purchasable":true},"tags":["SpellBlock"],"maps":{"11":true,"12":true},"into":["3190"],"stats":{"FlatSpellBlockMod":25}},
          "1011": {"name":"Giant's Belt","gold":{"total":900,"sell":630,"purchasable":true},"tags":["Health"],"maps":{"11":true,"12":true},"into":["3084","3068"],"stats":{"FlatHPPoolMod":350}},
          "1082": {"name":"Dark Seal","gold":{"total":350,"sell":140,"purchasable":true},"tags":["SpellDamage"],"maps":{"11":true,"12":true},"into":["3041"],"stats":{"FlatMagicDamageMod":15}},
          "1055": {"name":"Doran's Blade","gold":{"total":450,"sell":185,"purchasable":true},"tags":["Health","Damage","LifeSteal","Lane"],"maps":{"11":true,"12":true},"stats":{"FlatPhysicalDamageMod":8,"FlatHPPoolMod":80}},

          "9001": {"name":"Crit Blade","gold":{"total":2600,"sell":1820,"purchasable":true},"tags":["Damage","CriticalStrike"],"maps":{"11":true,"12":true},"from":["1036","1018"],"depth":2,"stats":{"FlatPhysicalDamageMod":40,"FlatCritChanceMod":0.25}},
          "9002": {"name":"Plain Blade","gold":{"total":2600,"sell":1820,"purchasable":true},"tags":["Damage"],"maps":{"11":true,"12":true},"from":["1036"],"depth":2,"stats":{"FlatPhysicalDamageMod":55}},
          "9010": {"name":"Zeal Charm","gold":{"total":1300,"sell":910,"purchasable":true},"tags":["CriticalStrike"],"maps":{"11":true,"12":true},"from":["1018"],"depth":2,"stats":{"FlatCritChanceMod":0.25}},

          "9020": {"name":"Percent Shredder","description":"<passive>Shred</passive><br>Gain <attention>30%</attention> Armor Penetration.","gold":{"total":3000,"sell":2100,"purchasable":true},"tags":["Damage","ArmorPenetration"],"maps":{"11":true,"12":true},"from":["1036"],"depth":2,"stats":{"FlatPhysicalDamageMod":45}},
          "9021": {"name":"Flat Knife","description":"<passive>Extraction</passive><br>Gain <attention>18</attention> Lethality.","gold":{"total":3000,"sell":2100,"purchasable":true},"tags":["Damage","ArmorPenetration"],"maps":{"11":true,"12":true},"from":["1036"],"depth":2,"stats":{"FlatPhysicalDamageMod":55}},
          "9030": {"name":"Meat Wall","gold":{"total":3000,"sell":2100,"purchasable":true},"tags":["Health"],"maps":{"11":true,"12":true},"stats":{"FlatHPPoolMod":1500}},

          "3084": {"name":"Heartsteel","description":"<passive>Colossal Consumption</passive><br>Stack max Health.","gold":{"total":3000,"sell":2100,"purchasable":true},"tags":["Health","HealthRegen"],"maps":{"11":true,"12":true},"from":["1011"],"depth":2,"stats":{"FlatHPPoolMod":900}},
          "3068": {"name":"Sunfire Aegis","description":"<passive>Immolate</passive><br>Burn nearby enemies.","gold":{"total":2700,"sell":1890,"purchasable":true},"tags":["Health","Armor"],"maps":{"11":true,"12":true},"from":["1029","1011"],"depth":2,"stats":{"FlatArmorMod":50,"FlatHPPoolMod":350}},
          "3190": {"name":"Locket of the Iron Solari","description":"<active>Devotion</active><br>Shield nearby allies.","gold":{"total":2200,"sell":1540,"purchasable":true},"tags":["Health","Armor","SpellBlock","Aura"],"maps":{"11":true,"12":true},"from":["1029","1033"],"depth":2,"stats":{"FlatArmorMod":30,"FlatSpellBlockMod":30,"FlatHPPoolMod":200}},

          "3041": {"name":"Mejai's Soulstealer","description":"<passive>Glory</passive><br>Gain stacks on takedowns, lose them on death.","gold":{"total":1500,"sell":1050,"purchasable":true},"tags":["SpellDamage"],"maps":{"11":true,"12":true},"from":["1082"],"depth":2,"stats":{"FlatMagicDamageMod":20}},
          "9050": {"name":"Big Tome","gold":{"total":3000,"sell":2100,"purchasable":true},"tags":["SpellDamage"],"maps":{"11":true,"12":true},"from":["1052"],"depth":2,"stats":{"FlatMagicDamageMod":85}}}}
        """);

    private static ItemAdvisor Advisor() => new(Catalog());

    private static GameState Game(string myChamp, double gold, int[] myItems,
        params (string Champ, int Kills, int[] Items)[] enemies)
    {
        var players = new List<(string, string, int, int[])> { (myChamp, "ORDER", 0, myItems) };
        players.AddRange(enemies.Select(e => (e.Champ, "CHAOS", e.Kills, e.Items)));
        return TestCatalog.State(gold, players.ToArray());
    }

    // --- Crítico saturado: el crítico que desborda el 100% no da puntos ---

    [Fact]
    public void At_full_crit_plain_ad_beats_another_crit_item()
    {
        var advisor = Advisor();
        var state = Game("Jinx", 3000, new int[0], ("Garen", 2, new int[0]));
        state.ActivePlayer!.ChampionStats.CritChance = 1.0; // 100% (Live Client)

        var plan = advisor.Advise(state)!;
        var critIdx = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 9001);
        var plainIdx = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 9002);
        Assert.True(plainIdx >= 0, "Plain Blade debe recomendarse");
        Assert.True(critIdx < 0 || plainIdx < critIdx,
            "a 100% de crítico, el item sin crítico debe rankear por encima");
    }

    [Fact]
    public void Below_cap_crit_item_keeps_full_value()
    {
        var advisor = Advisor();
        // 50% de crítico por items (2× Zeal Charm): el Crit Blade (25%) entra entero.
        var state = Game("Jinx", 3000, new[] { 9010, 9010 }, ("Garen", 2, new int[0]));

        var plan = advisor.Advise(state)!;
        var critIdx = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 9001);
        var plainIdx = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 9002);
        Assert.True(critIdx >= 0, "Crit Blade debe recomendarse");
        Assert.True(plainIdx < 0 || critIdx < plainIdx,
            "con margen de crítico, el item de crítico sigue arriba");
    }

    [Fact]
    public void Owned_items_crit_counts_when_live_stat_is_missing()
    {
        var advisor = Advisor();
        // Sin championStats.critChance (datos viejos/replay): 4 Zeal = 100% por items.
        var state = Game("Jinx", 3000, new[] { 9010, 9010, 9010, 9010 }, ("Garen", 2, new int[0]));

        var plan = advisor.Advise(state)!;
        var critIdx = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 9001);
        var plainIdx = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 9002);
        Assert.True(plainIdx >= 0 && (critIdx < 0 || plainIdx < critIdx));
    }

    // --- La vida como defensa (Heartsteel) ---

    [Fact]
    public void Pure_hp_item_wins_against_mixed_damage_comp()
    {
        var advisor = Advisor();
        // Garen (físico) + Ahri (mágico) parejos → daño 50/50: la vida defiende ambos.
        var state = Game("Shen", 9000, new int[0], ("Garen", 2, new int[0]), ("Ahri", 2, new int[0]));

        var plan = advisor.Advise(state)!;
        var heart = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 3084);
        var sunfire = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 3068);
        Assert.True(heart >= 0, "Heartsteel debe recomendarse contra daño mixto");
        Assert.True(sunfire < 0 || heart < sunfire,
            "contra daño mixto, el muro de HP puro rankea sobre el de armadura");
    }

    [Fact]
    public void Armor_item_still_wins_against_full_physical_comp()
    {
        var advisor = Advisor();
        var state = Game("Shen", 9000, new int[0], ("Garen", 3, new int[0]), ("Garen", 2, new int[0]));

        var plan = advisor.Advise(state)!;
        var heart = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 3084);
        var sunfire = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 3068);
        Assert.True(sunfire >= 0, "Sunfire debe recomendarse contra full físico");
        Assert.True(heart < 0 || sunfire < heart,
            "contra full físico, la armadura sigue por encima del HP puro");
    }

    // --- Items de dupla/soporte solo para soportes ---

    [Fact]
    public void Support_duo_items_are_not_recommended_to_laners()
    {
        var advisor = Advisor();
        var state = Game("Shen", 9000, new int[0], ("Garen", 2, new int[0]), ("Ahri", 2, new int[0]));
        var plan = advisor.Advise(state)!;
        Assert.DoesNotContain(plan.Recommendations, r => r.Item.Id == 3190);
    }

    [Fact]
    public void Support_duo_items_allowed_for_enchanters_and_utility_position()
    {
        var advisor = Advisor();
        // Lulu es Enchanter: Locket permitido (y con este catálogo, recomendado).
        var lulu = Game("Lulu", 9000, new int[0], ("Garen", 2, new int[0]), ("Ahri", 2, new int[0]));
        Assert.Contains(advisor.Advise(lulu)!.Recommendations, r => r.Item.Id == 3190);

        // Shen SUPPORT (posición UTILITY): también permitido.
        var shenSup = Game("Shen", 9000, new int[0], ("Garen", 2, new int[0]), ("Ahri", 2, new int[0]));
        shenSup.AllPlayers[0].Position = "UTILITY";
        Assert.Contains(advisor.Advise(shenSup)!.Recommendations, r => r.Item.Id == 3190);
    }

    // --- Letalidad no es anti-tanque ---

    [Fact]
    public void Percent_pen_outranks_lethality_against_tanky_enemies()
    {
        var advisor = Advisor();
        // Enemigos con 3000 de vida comprada cada uno → EnemyTankiness alto.
        var state = Game("Zed", 4000, new int[0],
            ("Garen", 2, new[] { 9030, 9030 }), ("Garen", 1, new[] { 9030, 9030 }));

        var plan = advisor.Advise(state)!;
        var pct = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 9020);
        var flat = plan.Recommendations.ToList().FindIndex(r => r.Item.Id == 9021);
        Assert.True(pct >= 0, "el %pen debe recomendarse contra tanques");
        Assert.True(flat < 0 || pct < flat,
            "contra tanques, el %pen rankea sobre la letalidad");
    }

    [Fact]
    public void Lethality_keyword_is_detected()
    {
        var catalog = Catalog();
        Assert.True(catalog.ItemById(9021)!.HasLethality);
        Assert.False(catalog.ItemById(9020)!.HasLethality);
    }

    // --- Mejai's según tu propio desempeño ---

    [Fact]
    public void Snowball_item_requires_going_well()
    {
        var advisor = Advisor();
        // 0/0/0: Mejai's no aparece.
        var cold = Game("Ahri", 4000, new int[0], ("Garen", 2, new int[0]));
        Assert.DoesNotContain(advisor.Advise(cold)!.Recommendations, r => r.Item.Id == 3041);

        // 5/0: snowballeando, sí.
        var hot = Game("Ahri", 4000, new int[0], ("Garen", 2, new int[0]));
        hot.AllPlayers[0].Scores.Kills = 5;
        Assert.Contains(advisor.Advise(hot)!.Recommendations, r => r.Item.Id == 3041);
    }

    [Fact]
    public void Dying_with_mejai_suggests_selling_it()
    {
        var advisor = Advisor();
        var state = Game("Ahri", 1000, new[] { 3041 }, ("Garen", 4, new int[0]));
        state.AllPlayers[0].Scores.Kills = 1;
        state.AllPlayers[0].Scores.Deaths = 5;

        var plan = advisor.Advise(state)!;
        Assert.Contains(plan.Sells, s => s.Item.Id == 3041);
    }

    // --- Starters se venden con el inventario lleno ---

    [Fact]
    public void Full_inventory_suggests_selling_the_starter()
    {
        var advisor = Advisor();
        var state = Game("Jinx", 3000, new int[0], ("Garen", 2, new int[0]));
        var me = state.AllPlayers[0];
        var ids = new[] { 1055, 9001, 9002, 9010, 9020, 9021 };
        for (var slot = 0; slot < 6; slot++)
            me.Items.Add(new Item { ItemID = ids[slot], Slot = slot, Count = 1 });

        var plan = advisor.Advise(state)!;
        Assert.Contains(plan.Sells, s => s.Item.Id == 1055);

        // Con lugar libre, el starter no molesta: no se sugiere venderlo.
        var roomy = Game("Jinx", 3000, new[] { 1055 }, ("Garen", 2, new int[0]));
        Assert.DoesNotContain(advisor.Advise(roomy)!.Sells, s => s.Item.Id == 1055);
    }
}
