using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Items;

namespace ParadoxLoLCompanion.Tests;

/// <summary>
/// Coherencia con las reglas del juego: el anti-heal debe pegar con el perfil de daño
/// (nada de Morello para Darius), y dos items con la misma pasiva con nombre
/// (Cleave/Lifeline/Annul/Immolate/Awe…) no pueden convivir en una build — ni entre
/// recomendaciones ni contra lo que el jugador ya compró (salvo mejora de componente).
/// </summary>
public class ItemCoherenceRulesTests
{
    // Catálogo mínimo con la forma REAL del parche actual: los items nuevos de
    // anti-curación dicen "40% Wounds" (pasiva Hackshorn/Thorns) sin "Grievous",
    // y las pasivas con nombre marcan los grupos excluyentes.
    internal static DataDragonCatalog Catalog() => DataDragonCatalog.FromJson(
        "16.13.1",
        """
        {"data":{
          "Darius": {"id":"Darius","key":"122","name":"Darius","tags":["Fighter","Tank"],"info":{"attack":9,"defense":5,"magic":1}},
          "Ahri":   {"id":"Ahri","key":"103","name":"Ahri","tags":["Mage","Assassin"],"info":{"attack":3,"defense":4,"magic":8}},
          "Rammus": {"id":"Rammus","key":"33","name":"Rammus","tags":["Tank"],"info":{"attack":4,"defense":10,"magic":5}},
          "Warwick":{"id":"Warwick","key":"19","name":"Warwick","tags":["Fighter","Tank"],"info":{"attack":9,"defense":5,"magic":3}},
          "Garen":  {"id":"Garen","key":"86","name":"Garen","tags":["Fighter","Tank"],"info":{"attack":7,"defense":7,"magic":1}}}}
        """,
        """
        {"data":{
          "1036": {"name":"Long Sword","gold":{"total":350,"purchasable":true},"tags":["Damage"],"maps":{"11":true,"12":true},"into":["6609","3074","3053","3156"],"stats":{"FlatPhysicalDamageMod":10}},
          "1052": {"name":"Amplifying Tome","gold":{"total":435,"purchasable":true},"tags":["SpellDamage"],"maps":{"11":true,"12":true},"into":["3165"],"stats":{"FlatMagicDamageMod":20}},
          "1029": {"name":"Chain Vest","gold":{"total":800,"purchasable":true},"tags":["Armor"],"maps":{"11":true,"12":true},"into":["3075","3068"],"stats":{"FlatArmorMod":40}},
          "1033": {"name":"Null-Magic Mantle","gold":{"total":450,"purchasable":true},"tags":["SpellBlock"],"maps":{"11":true,"12":true},"into":["6664","3156"],"stats":{"FlatSpellBlockMod":25}},
          "1011": {"name":"Giant's Belt","gold":{"total":900,"purchasable":true},"tags":["Health"],"maps":{"11":true,"12":true},"into":["3748","3068","6664"],"stats":{"FlatHPPoolMod":350}},
          "3077": {"name":"Tiamat","description":"<passive>Cleave</passive><br>Attacks deal damage around the target.","gold":{"total":1200,"purchasable":true},"tags":["Damage"],"maps":{"11":true,"12":true},"into":["3074","3748"],"from":["1036"],"depth":2,"stats":{"FlatPhysicalDamageMod":20}},

          "6609": {"name":"Chempunk Chainsword","description":"<passive>Hackshorn</passive><br>Dealing physical damage applies <keyword>40% Wounds</keyword> to enemy champions for 3 seconds.","gold":{"total":2500,"purchasable":true},"tags":["Damage","Health","AbilityHaste"],"maps":{"11":true,"12":true},"from":["1036"],"depth":2,"stats":{"FlatPhysicalDamageMod":45,"FlatHPPoolMod":450}},
          "3165": {"name":"Morellonomicon","description":"<passive>Grievous Wounds</passive><br>Dealing magic damage applies <keyword>40% Wounds</keyword> to enemy champions for 3 seconds.","gold":{"total":2850,"purchasable":true},"tags":["SpellDamage","Health","AbilityHaste"],"maps":{"11":true,"12":true},"from":["1052"],"depth":2,"stats":{"FlatMagicDamageMod":90,"FlatHPPoolMod":350}},
          "3075": {"name":"Thornmail","description":"<passive>Thorns</passive><br>When struck by an Attack, deal magic damage to the attacker and apply 40% <keyword>Wounds</keyword> for 3 seconds.","gold":{"total":2450,"purchasable":true},"tags":["Armor","Health"],"maps":{"11":true,"12":true},"from":["1029","1011"],"depth":2,"stats":{"FlatArmorMod":70,"FlatHPPoolMod":350}},

          "3074": {"name":"Ravenous Hydra","description":"<passive>Cleave</passive><br>Attacks deal damage around the target.","gold":{"total":3300,"purchasable":true},"tags":["Damage","Health"],"maps":{"11":true,"12":true},"from":["3077","1036"],"depth":3,"stats":{"FlatPhysicalDamageMod":65,"FlatHPPoolMod":300}},
          "3748": {"name":"Titanic Hydra","description":"<passive>Cleave</passive><br>Attacks deal damage around the target.","gold":{"total":3300,"purchasable":true},"tags":["Damage","Health"],"maps":{"11":true,"12":true},"from":["3077","1011"],"depth":3,"stats":{"FlatPhysicalDamageMod":40,"FlatHPPoolMod":500}},
          "3053": {"name":"Sterak's Gage","description":"<passive>Lifeline</passive><br>Upon taking damage that would reduce you below 30% Health, gain a shield.","gold":{"total":3200,"purchasable":true},"tags":["Damage","Health"],"maps":{"11":true,"12":true},"from":["1036","1011"],"depth":2,"stats":{"FlatPhysicalDamageMod":40,"FlatHPPoolMod":400}},
          "3156": {"name":"Maw of Malmortius","description":"<passive>Lifeline</passive><br>Upon taking magic damage that would reduce you below 30% Health, gain a shield.","gold":{"total":3100,"purchasable":true},"tags":["Damage","SpellBlock","LifeSteal"],"maps":{"11":true,"12":true},"from":["1036","1033"],"depth":2,"stats":{"FlatPhysicalDamageMod":60,"FlatSpellBlockMod":40}},
          "3068": {"name":"Sunfire Aegis","description":"<passive>Immolate</passive><br>Deal magic damage to nearby enemies.","gold":{"total":2700,"purchasable":true},"tags":["Armor","Health"],"maps":{"11":true,"12":true},"from":["1029","1011"],"depth":2,"stats":{"FlatArmorMod":50,"FlatHPPoolMod":350}},
          "6664": {"name":"Hollow Radiance","description":"<passive>Immolate</passive><br>Deal magic damage to nearby enemies.","gold":{"total":2800,"purchasable":true},"tags":["SpellBlock","Health"],"maps":{"11":true,"12":true},"from":["1033","1011"],"depth":2,"stats":{"FlatSpellBlockMod":50,"FlatHPPoolMod":350}},
          "3089": {"name":"Rabadon's Deathcap","gold":{"total":3600,"purchasable":true},"tags":["SpellDamage"],"maps":{"11":true,"12":true},"from":["1052"],"depth":2,"stats":{"FlatMagicDamageMod":130}}}}
        """);

    private static ItemAdvisor Advisor() => new(Catalog());

    /// <summary>Enemigos con Warwick fed (curador): dispara la regla de anti-heal.</summary>
    private static Core.Models.GameState SustainGame(string myChamp, double gold, params int[] myItems) =>
        TestCatalog.State(gold,
            (myChamp, "ORDER", 2, myItems),
            ("Warwick", "CHAOS", 6, new int[0]),
            ("Garen", "CHAOS", 1, new int[0]),
            ("Ahri", "CHAOS", 1, new int[0]));

    /// <summary>Enemigos sin curación relevante.</summary>
    private static Core.Models.GameState PlainGame(string myChamp, double gold, params int[] myItems) =>
        TestCatalog.State(gold,
            (myChamp, "ORDER", 2, myItems),
            ("Garen", "CHAOS", 3, new int[0]),
            ("Ahri", "CHAOS", 2, new int[0]));

    // --- Detección de Heridas Graves con el texto nuevo ("40% Wounds") ---

    [Fact]
    public void Wounds_only_text_is_detected_as_grievous()
    {
        var catalog = Catalog();
        Assert.True(catalog.ItemById(6609)!.AppliesGrievousWounds, "Chempunk (Hackshorn / 40% Wounds)");
        Assert.True(catalog.ItemById(3075)!.AppliesGrievousWounds, "Thornmail (Thorns / 40% Wounds)");
        Assert.True(catalog.ItemById(3165)!.AppliesGrievousWounds, "Morello (Grievous Wounds)");
    }

    [Fact]
    public void Passive_names_are_parsed_from_description()
    {
        var catalog = Catalog();
        Assert.Contains("Cleave", catalog.ItemById(3074)!.PassiveNames);
        Assert.Contains("Lifeline", catalog.ItemById(3053)!.PassiveNames);
        Assert.Empty(catalog.ItemById(3089)!.PassiveNames);
    }

    // --- Anti-heal coherente con el perfil de daño ---

    [Fact]
    public void Ad_fighter_never_gets_pure_ap_items()
    {
        var plan = Advisor().Advise(SustainGame("Darius", 3000))!;

        // Darius es físico puro: ningún item cuyo único stat ofensivo sea AP.
        Assert.DoesNotContain(plan.Recommendations,
            r => r.Item.HasTag("SpellDamage") && !r.Item.HasTag("Damage"));
        // El anti-heal correcto para él es el de daño físico.
        var chempunk = plan.Recommendations.FirstOrDefault(r => r.Item.Id == 6609);
        Assert.NotNull(chempunk);
        Assert.Contains(chempunk!.Reasons, reason => reason.Contains("healing"));
    }

    [Fact]
    public void Ad_fighter_never_falls_back_to_ap_antiheal()
    {
        // Con los anti-heal físicos ya comprados (Quimpunk + Cota), Morello queda como
        // el item de Heridas mejor puntuado — pero para un físico puro los items
        // solo-AP están fuera del pool: no puede colarse ni por fit ni por counter.
        var plan = Advisor().Advise(SustainGame("Darius", 3000, 6609, 3075))!;
        Assert.DoesNotContain(plan.Recommendations,
            r => r.Item.HasTag("SpellDamage") && !r.Item.HasTag("Damage"));
    }

    [Fact]
    public void Mage_still_gets_morello_for_antiheal()
    {
        var plan = Advisor().Advise(SustainGame("Ahri", 3000))!;
        var morello = plan.Recommendations.FirstOrDefault(r => r.Item.Id == 3165);
        Assert.NotNull(morello);
        Assert.Contains(morello!.Reasons, reason => reason.Contains("healing"));
        // Y a la maga no se le cuelan items de daño físico puro.
        Assert.DoesNotContain(plan.Recommendations,
            r => r.Item.HasTag("Damage") && !r.Item.HasTag("SpellDamage"));
    }

    [Fact]
    public void Tank_gets_thornmail_as_antiheal()
    {
        var plan = Advisor().Advise(SustainGame("Rammus", 3000))!;
        var thornmail = plan.Recommendations.FirstOrDefault(r => r.Item.Id == 3075);
        Assert.NotNull(thornmail);
        Assert.Contains(thornmail!.Reasons, reason => reason.Contains("healing"));
    }

    // --- Exclusividad por pasiva con nombre ---

    [Fact]
    public void Plan_never_recommends_two_items_with_the_same_passive()
    {
        var plan = Advisor().Advise(PlainGame("Darius", 3000))!;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in plan.Recommendations)
            foreach (var passive in r.Item.PassiveNames)
                Assert.True(seen.Add(passive),
                    $"{r.Item.Name} repite la pasiva '{passive}' dentro del mismo plan");
    }

    [Fact]
    public void Owned_hydra_blocks_recommending_another_hydra()
    {
        // Con la Hidra Titánica comprada, la Voraz (misma pasiva Cleave) es ilegal.
        var plan = Advisor().Advise(PlainGame("Darius", 6000, 3748))!;
        Assert.DoesNotContain(plan.Recommendations, r => r.Item.Id == 3074);
    }

    [Fact]
    public void Owned_component_does_not_block_its_upgrades()
    {
        // Tiamat también tiene Cleave, pero es componente de las Hidras: mejorar es legal.
        var plan = Advisor().Advise(PlainGame("Darius", 6000, 3077))!;
        Assert.Contains(plan.Recommendations, r => r.Item.PassiveNames.Contains("Cleave"));
    }

    [Fact]
    public void Tank_plan_does_not_pair_both_immolate_items()
    {
        // Mixto de daño enemigo: Egida (Armor) y Radiancia (SpellBlock) puntúan parejo,
        // pero comparten Immolate: solo una puede entrar al plan.
        var plan = Advisor().Advise(PlainGame("Rammus", 9000))!;
        var immolates = plan.Recommendations.Count(r => r.Item.PassiveNames.Contains("Immolate"));
        Assert.True(immolates <= 1, $"El plan trae {immolates} items con Immolate");
    }
}
