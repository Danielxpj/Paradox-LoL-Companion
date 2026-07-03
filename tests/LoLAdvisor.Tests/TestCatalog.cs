using LoLAdvisor.Core.DataDragon;
using LoLAdvisor.Core.Models;

namespace LoLAdvisor.Tests;

/// <summary>
/// Catálogo estático de prueba con forma realista de Data Dragon (en_US): campeones con
/// <c>info</c>/tags y un set de items con stats, árbol de construcción, mapas (11/12) y
/// descripciones — suficiente para ejercitar el asesor completo sin red.
/// Items especiales para tests: 6672 (solo Grieta, no ARAM), 7031 (mejora tipo Obra
/// Maestra: no comprable, se construye desde 3031), 9068 (duplicado de nombre de
/// Sunfire Aegis con otro id, como los que trae el catálogo real), 994403/223069/663172
/// (items de modos/perks: ddragon los marca comprables en un mapa pero no tienen árbol
/// de construcción) y 3173 (botas tier 3 de Hazañas de Fuerza, mejora de 3111).
/// </summary>
internal static class TestCatalog
{
    private const string Champions = """
    {
      "data": {
        "Aatrox":     { "id":"Aatrox","key":"266","name":"Aatrox","tags":["Fighter","Tank"],"info":{"attack":8,"defense":4,"magic":3} },
        "Ahri":       { "id":"Ahri","key":"103","name":"Ahri","tags":["Mage","Assassin"],"info":{"attack":3,"defense":4,"magic":8} },
        "Jinx":       { "id":"Jinx","key":"222","name":"Jinx","tags":["Marksman"],"info":{"attack":9,"defense":2,"magic":4} },
        "Zed":        { "id":"Zed","key":"238","name":"Zed","tags":["Assassin"],"info":{"attack":9,"defense":2,"magic":1} },
        "Malzahar":   { "id":"Malzahar","key":"90","name":"Malzahar","tags":["Mage","Assassin"],"info":{"attack":2,"defense":2,"magic":9} },
        "Soraka":     { "id":"Soraka","key":"16","name":"Soraka","tags":["Support","Mage"],"info":{"attack":2,"defense":3,"magic":7} },
        "Warwick":    { "id":"Warwick","key":"19","name":"Warwick","tags":["Fighter","Tank"],"info":{"attack":9,"defense":5,"magic":3} },
        "Gwen":       { "id":"Gwen","key":"887","name":"Gwen","tags":["Fighter","Assassin"],"info":{"attack":7,"defense":4,"magic":5} },
        "Leona":      { "id":"Leona","key":"89","name":"Leona","tags":["Tank","Support"],"info":{"attack":4,"defense":8,"magic":5} },
        "Amumu":      { "id":"Amumu","key":"32","name":"Amumu","tags":["Tank","Mage"],"info":{"attack":2,"defense":6,"magic":8} },
        "Karma":      { "id":"Karma","key":"43","name":"Karma","tags":["Mage","Support"],"info":{"attack":1,"defense":7,"magic":8} },
        "Vayne":      { "id":"Vayne","key":"67","name":"Vayne","tags":["Marksman","Assassin"],"info":{"attack":10,"defense":1,"magic":1} },
        "Pyke":       { "id":"Pyke","key":"555","name":"Pyke","tags":["Support","Assassin"],"info":{"attack":9,"defense":3,"magic":1} },
        "MonkeyKing": { "id":"MonkeyKing","key":"62","name":"Wukong","tags":["Fighter","Tank"],"info":{"attack":8,"defense":5,"magic":2} }
      }
    }
    """;

    private const string Items = """
    {
      "data": {
        "1001": { "name":"Boots","gold":{"total":300,"purchasable":true},"tags":["Boots"],"maps":{"11":true,"12":true},"into":["3111","3047","3006","3020","3158"] },
        "1011": { "name":"Giant's Belt","gold":{"total":900,"purchasable":true},"tags":["Health"],"maps":{"11":true,"12":true},"into":["3075","3068","3083","6653"],"stats":{"FlatHPPoolMod":350} },
        "1018": { "name":"Cloak of Agility","gold":{"total":600,"purchasable":true},"tags":["CriticalStrike"],"maps":{"11":true,"12":true},"into":["3031","3033","3036"],"stats":{"FlatCritChanceMod":0.15} },
        "1029": { "name":"Chain Vest","gold":{"total":800,"purchasable":true},"tags":["Armor"],"maps":{"11":true,"12":true},"into":["3157","3026","3075","3068","3047"],"stats":{"FlatArmorMod":40} },
        "1033": { "name":"Null-Magic Mantle","gold":{"total":450,"purchasable":true},"tags":["SpellBlock"],"maps":{"11":true,"12":true},"into":["3102","3156","3139","3111","3065"],"stats":{"FlatSpellBlockMod":25} },
        "1036": { "name":"Long Sword","gold":{"total":350,"purchasable":true},"tags":["Damage"],"maps":{"11":true,"12":true},"into":["6695","3033","3123"],"stats":{"FlatPhysicalDamageMod":10} },
        "1038": { "name":"B.F. Sword","gold":{"total":1300,"purchasable":true},"tags":["Damage"],"maps":{"11":true,"12":true},"into":["3031","3026","3156","3036"],"stats":{"FlatPhysicalDamageMod":40} },
        "1052": { "name":"Amplifying Tome","gold":{"total":435,"purchasable":true},"tags":["SpellDamage"],"maps":{"11":true,"12":true},"into":["3089","3135","3165"],"stats":{"FlatMagicDamageMod":20} },
        "1053": { "name":"Vampiric Scepter","gold":{"total":900,"purchasable":true},"tags":["Damage","LifeSteal"],"maps":{"11":true,"12":true},"into":["3139"],"stats":{"FlatPhysicalDamageMod":15} },
        "1058": { "name":"Needlessly Large Rod","gold":{"total":1250,"purchasable":true},"tags":["SpellDamage"],"maps":{"11":true,"12":true},"into":["3089","3157","3102","3115","6653"],"stats":{"FlatMagicDamageMod":60} },
        "3123": { "name":"Executioner's Calling","description":"Passive: inflicts Grievous Wounds","gold":{"total":800,"purchasable":true},"tags":["Damage","LifeSteal"],"maps":{"11":true,"12":true},"into":["3033"],"from":["1036"],"depth":2,"stats":{"FlatPhysicalDamageMod":15} },
        "3916": { "name":"Oblivion Orb","description":"Passive: inflicts Grievous Wounds","gold":{"total":800,"purchasable":true},"tags":["SpellDamage"],"maps":{"11":true,"12":true},"into":["3165"],"stats":{"FlatMagicDamageMod":30} },

        "3031": { "name":"Infinity Edge","gold":{"total":3400,"purchasable":true},"tags":["Damage","CriticalStrike"],"maps":{"11":true,"12":true},"from":["1038","1018"],"depth":3,"stats":{"FlatPhysicalDamageMod":65,"FlatCritChanceMod":0.25} },
        "3033": { "name":"Mortal Reminder","description":"Passive: inflicts Grievous Wounds","gold":{"total":3300,"purchasable":true},"tags":["Damage","CriticalStrike","ArmorPenetration"],"maps":{"11":true,"12":true},"from":["3123","1018","1036"],"depth":3,"stats":{"FlatPhysicalDamageMod":35,"FlatCritChanceMod":0.25} },
        "3036": { "name":"Lord Dominik's Regards","gold":{"total":3100,"purchasable":true},"tags":["Damage","CriticalStrike","ArmorPenetration"],"maps":{"11":true,"12":true},"from":["1038","1018"],"depth":3,"stats":{"FlatPhysicalDamageMod":35,"FlatCritChanceMod":0.25} },
        "3089": { "name":"Rabadon's Deathcap","gold":{"total":3600,"purchasable":true},"tags":["SpellDamage"],"maps":{"11":true,"12":true},"from":["1058","1052"],"depth":3,"stats":{"FlatMagicDamageMod":130} },
        "3135": { "name":"Void Staff","gold":{"total":3000,"purchasable":true},"tags":["SpellDamage","MagicPenetration"],"maps":{"11":true,"12":true},"from":["1052"],"depth":2,"stats":{"FlatMagicDamageMod":95} },
        "3165": { "name":"Morellonomicon","description":"Passive: inflicts Grievous Wounds","gold":{"total":2200,"purchasable":true},"tags":["SpellDamage","Health"],"maps":{"11":true,"12":true},"from":["3916","1052"],"depth":3,"stats":{"FlatMagicDamageMod":75,"FlatHPPoolMod":200} },
        "3157": { "name":"Zhonya's Hourglass","gold":{"total":3250,"purchasable":true},"tags":["SpellDamage","Armor"],"maps":{"11":true,"12":true},"from":["1058","1029"],"depth":3,"stats":{"FlatMagicDamageMod":105,"FlatArmorMod":50} },
        "3026": { "name":"Guardian Angel","gold":{"total":3200,"purchasable":true},"tags":["Damage","Armor"],"maps":{"11":true,"12":true},"from":["1038","1029"],"depth":3,"stats":{"FlatPhysicalDamageMod":55,"FlatArmorMod":45} },
        "3102": { "name":"Banshee's Veil","gold":{"total":3100,"purchasable":true},"tags":["SpellDamage","SpellBlock"],"maps":{"11":true,"12":true},"from":["1058","1033"],"depth":3,"stats":{"FlatMagicDamageMod":105,"FlatSpellBlockMod":50} },
        "3156": { "name":"Maw of Malmortius","gold":{"total":3100,"purchasable":true},"tags":["Damage","SpellBlock","LifeSteal"],"maps":{"11":true,"12":true},"from":["1038","1033"],"depth":3,"stats":{"FlatPhysicalDamageMod":60,"FlatSpellBlockMod":40} },
        "3139": { "name":"Mercurial Scimitar","description":"Active: Removes all crowd control debuffs","gold":{"total":3000,"purchasable":true},"tags":["Damage","SpellBlock","LifeSteal"],"maps":{"11":true,"12":true},"from":["1053","1033"],"depth":3,"stats":{"FlatPhysicalDamageMod":40,"FlatSpellBlockMod":40} },
        "6695": { "name":"Serpent's Fang","description":"Shield Reaver: reduces enemy shields","gold":{"total":2500,"purchasable":true},"tags":["Damage","ArmorPenetration"],"maps":{"11":true,"12":true},"from":["1036"],"depth":2,"stats":{"FlatPhysicalDamageMod":55} },
        "3153": { "name":"Blade of the Ruined King","gold":{"total":3200,"purchasable":true},"tags":["Damage","AttackSpeed","LifeSteal","OnHit"],"maps":{"11":true,"12":true},"from":["1053","1036"],"depth":3,"stats":{"FlatPhysicalDamageMod":40,"PercentAttackSpeedMod":0.25} },
        "6672": { "name":"Kraken Slayer","gold":{"total":3000,"purchasable":true},"tags":["Damage","AttackSpeed","OnHit"],"maps":{"11":true,"12":false},"from":["1038"],"depth":2,"stats":{"FlatPhysicalDamageMod":40,"PercentAttackSpeedMod":0.35} },
        "3115": { "name":"Nashor's Tooth","gold":{"total":3000,"purchasable":true},"tags":["SpellDamage","AttackSpeed"],"maps":{"11":true,"12":true},"from":["1058"],"depth":2,"stats":{"FlatMagicDamageMod":90,"PercentAttackSpeedMod":0.5} },
        "6653": { "name":"Liandry's Torment","gold":{"total":3000,"purchasable":true},"tags":["SpellDamage","Health"],"maps":{"11":true,"12":true},"from":["1058","1011"],"depth":3,"stats":{"FlatMagicDamageMod":90,"FlatHPPoolMod":300} },
        "3075": { "name":"Thornmail","description":"Passive: inflicts Grievous Wounds","gold":{"total":2450,"purchasable":true},"tags":["Armor","Health"],"maps":{"11":true,"12":true},"from":["1029","1011"],"depth":3,"stats":{"FlatArmorMod":70,"FlatHPPoolMod":350} },
        "3065": { "name":"Spirit Visage","gold":{"total":2700,"purchasable":true},"tags":["SpellBlock","Health","HealthRegen","CooldownReduction"],"maps":{"11":true,"12":true},"from":["1033","1011"],"depth":3,"stats":{"FlatSpellBlockMod":50,"FlatHPPoolMod":400} },
        "3068": { "name":"Sunfire Aegis","gold":{"total":2700,"purchasable":true},"tags":["Armor","Health"],"maps":{"11":true,"12":true},"from":["1029","1011"],"depth":3,"stats":{"FlatArmorMod":50,"FlatHPPoolMod":350} },
        "3143": { "name":"Randuin's Omen","description":"Reduce el daño de los golpes críticos que recibes en un 20%","gold":{"total":2700,"purchasable":true},"tags":["Armor","Health"],"maps":{"11":true,"12":true},"from":["1029","1011"],"depth":3,"stats":{"FlatArmorMod":55,"FlatHPPoolMod":350} },
        "3083": { "name":"Warmog's Armor","gold":{"total":3100,"purchasable":true},"tags":["Health","HealthRegen"],"maps":{"11":true,"12":true},"from":["1011"],"depth":2,"stats":{"FlatHPPoolMod":1000} },
        "2065": { "name":"Shurelya's Battlesong","gold":{"total":2200,"purchasable":true},"tags":["Health","CooldownReduction","ManaRegen","Aura"],"maps":{"11":true,"12":true},"from":["1011"],"depth":2,"stats":{"FlatHPPoolMod":200} },
        "7031": { "name":"Zenith Edge (Masterwork)","gold":{"total":3400,"purchasable":false},"tags":["Damage","CriticalStrike"],"maps":{"11":true,"12":true},"from":["3031"],"depth":4,"stats":{"FlatPhysicalDamageMod":75,"FlatCritChanceMod":0.25} },
        "9068": { "name":"Sunfire Aegis","gold":{"total":2600,"purchasable":true},"tags":["Armor","Health"],"maps":{"11":true,"12":true},"from":["1029","1011"],"depth":3,"stats":{"FlatArmorMod":45,"FlatHPPoolMod":300} },

        "1055": { "name":"Doran's Blade","gold":{"total":450,"purchasable":true},"tags":["Health","Damage","LifeSteal","Lane"],"maps":{"11":true,"12":true},"stats":{"FlatPhysicalDamageMod":8,"FlatHPPoolMod":80} },
        "2051": { "name":"Guardian's Horn","gold":{"total":950,"purchasable":true},"tags":["Health","HealthRegen","Lane"],"maps":{"11":false,"12":true},"stats":{"FlatHPPoolMod":150} },
        "3112": { "name":"Guardian's Orb","gold":{"total":950,"purchasable":true},"tags":["Health","SpellDamage","ManaRegen","Lane"],"maps":{"11":false,"12":true},"stats":{"FlatMagicDamageMod":40,"FlatHPPoolMod":100} },
        "3184": { "name":"Guardian's Hammer","gold":{"total":950,"purchasable":true},"tags":["Health","Damage","LifeSteal","Lane"],"maps":{"11":false,"12":true},"stats":{"FlatPhysicalDamageMod":25,"FlatHPPoolMod":100} },

        "994403": { "name":"Golden Spatula","gold":{"total":2500,"purchasable":true},"tags":["Health","Damage","CriticalStrike","AttackSpeed","LifeSteal","SpellDamage","Armor","SpellBlock"],"maps":{"11":false,"12":true},"stats":{"FlatHPPoolMod":250,"FlatPhysicalDamageMod":30,"FlatMagicDamageMod":45} },
        "223069": { "name":"Void Immolation","gold":{"total":6000,"purchasable":true},"tags":["Health","Armor","SpellBlock","Aura"],"maps":{"11":false,"12":true},"stats":{"FlatHPPoolMod":650,"FlatArmorMod":60,"FlatSpellBlockMod":60} },
        "663172": { "name":"Zephyr","gold":{"total":2800,"purchasable":true},"tags":["Damage","AttackSpeed"],"maps":{"11":true,"12":false},"stats":{"FlatPhysicalDamageMod":40,"PercentAttackSpeedMod":0.5} },

        "3111": { "name":"Mercury's Treads","gold":{"total":1100,"purchasable":true},"tags":["Boots","SpellBlock","Tenacity"],"maps":{"11":true,"12":true},"into":["3173"],"from":["1001","1033"],"depth":2,"stats":{"FlatSpellBlockMod":25} },
        "3173": { "name":"Forever Forward","gold":{"total":1850,"purchasable":true},"tags":["Boots","SpellBlock","Tenacity"],"maps":{"11":true,"12":false},"from":["3111"],"depth":3,"stats":{"FlatSpellBlockMod":30} },
        "3047": { "name":"Plated Steelcaps","gold":{"total":1100,"purchasable":true},"tags":["Boots","Armor"],"maps":{"11":true,"12":true},"from":["1001","1029"],"depth":2,"stats":{"FlatArmorMod":20} },
        "3006": { "name":"Berserker's Greaves","gold":{"total":1100,"purchasable":true},"tags":["Boots","AttackSpeed"],"maps":{"11":true,"12":true},"from":["1001"],"depth":2,"stats":{"PercentAttackSpeedMod":0.35} },
        "3020": { "name":"Sorcerer's Shoes","gold":{"total":1100,"purchasable":true},"tags":["Boots","MagicPenetration"],"maps":{"11":true,"12":true},"from":["1001"],"depth":2 },
        "3158": { "name":"Ionian Boots of Lucidity","gold":{"total":950,"purchasable":true},"tags":["Boots","CooldownReduction"],"maps":{"11":true,"12":true},"from":["1001"],"depth":2 }
      }
    }
    """;

    public static DataDragonCatalog Catalog() => DataDragonCatalog.FromJson("16.13.1", Champions, Items);

    /// <summary>
    /// Arma un GameState de Grieta (CLASSIC, mapa 11) con "Me" como jugador activo.
    /// Cada jugador se declara con (campeón, equipo, kills, items); el primero es el local.
    /// </summary>
    public static GameState State(double gold,
        params (string Champ, string Team, int Kills, int[] ItemIds)[] players) =>
        Build(gold, "CLASSIC", 11, players);

    /// <summary>Igual que <see cref="State"/> pero en ARAM (mapa 12).</summary>
    public static GameState AramState(double gold,
        params (string Champ, string Team, int Kills, int[] ItemIds)[] players) =>
        Build(gold, "ARAM", 12, players);

    private static GameState Build(double gold, string gameMode, int mapNumber,
        (string Champ, string Team, int Kills, int[] ItemIds)[] players)
    {
        var state = new GameState
        {
            ActivePlayer = new ActivePlayer { SummonerName = "Me", CurrentGold = gold },
            GameData = new GameData { GameMode = gameMode, MapNumber = mapNumber },
        };
        var first = true;
        foreach (var (champ, team, kills, itemIds) in players)
        {
            var p = new Player
            {
                SummonerName = first ? "Me" : champ,
                ChampionName = champ,
                RawChampionName = $"game_character_displayname_{champ}",
                Team = team,
                Scores = new PlayerScores { Kills = kills },
            };
            foreach (var id in itemIds)
                p.Items.Add(new Item { ItemID = id, Count = 1 });
            state.AllPlayers.Add(p);
            first = false;
        }
        return state;
    }
}
