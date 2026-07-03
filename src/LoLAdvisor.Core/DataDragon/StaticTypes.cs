namespace LoLAdvisor.Core.DataDragon;

/// <summary>Valores <c>info</c> de Data Dragon (escalas 0–10 que estima Riot).</summary>
public sealed class ChampionInfo
{
    public int Attack { get; init; }
    public int Defense { get; init; }
    public int Magic { get; init; }
}

/// <summary>Campeón del catálogo estático (Data Dragon).</summary>
public sealed class StaticChampion
{
    public int Id { get; init; }
    /// <summary>Id textual de ddragon (p.ej. <c>MonkeyKing</c>): estable entre idiomas.</summary>
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Tags de clase en el orden de ddragon (el primero es la clase primaria).</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public ChampionInfo Info { get; init; } = new();

    public string PrimaryTag => Tags.Count > 0 ? Tags[0] : "";

    public bool HasTag(string tag) =>
        Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Item del catálogo estático, con lo necesario para recomendar y contrarrestar.</summary>
public sealed class StaticItem
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int GoldTotal { get; init; }
    /// <summary>Oro que devuelve venderlo (<c>gold.sell</c> de ddragon; 0 = no se vende).</summary>
    public int SellGold { get; init; }
    public bool Purchasable { get; init; }
    public IReadOnlySet<string> Tags { get; init; } = new HashSet<string>();
    public bool OnSummonersRift { get; init; }
    /// <summary>Disponible en el Abismo de los Lamentos (mapa 12).</summary>
    public bool OnAram { get; init; }
    /// <summary>Es componente de otro item (no es un item final).</summary>
    public bool BuildsIntoSomething { get; init; }
    public bool Consumable { get; init; }
    /// <summary>Su pasiva aplica Heridas Graves (anti-curación).</summary>
    public bool AppliesGrievousWounds { get; init; }
    /// <summary>Su activa limpia efectos de control (QSS / Mercurial).</summary>
    public bool RemovesCc { get; init; }
    /// <summary>Su pasiva daña/reduce escudos (Colmillo de la Serpiente).</summary>
    public bool BreaksShields { get; init; }
    /// <summary>Su pasiva reduce el daño crítico que recibes (Presagio de Randuin).</summary>
    public bool ReducesCritDamage { get; init; }

    /// <summary>Profundidad en el árbol de construcción (1 = básico).</summary>
    public int Depth { get; init; } = 1;
    /// <summary>Ids de los componentes directos con los que se construye.</summary>
    public IReadOnlyList<int> From { get; init; } = Array.Empty<int>();

    // Stats planos del bloque `stats` de ddragon (0 si el item no los da).
    public double Armor { get; init; }
    public double SpellBlock { get; init; }
    public double Health { get; init; }
    public double AttackDamage { get; init; }
    public double AbilityPower { get; init; }
    /// <summary>Velocidad de ataque como fracción (0.35 = 35 %).</summary>
    public double AttackSpeedPct { get; init; }
    /// <summary>Prob. de crítico como fracción (0.25 = 25 %).</summary>
    public double CritChance { get; init; }

    public bool IsBoots => HasTag("Boots");

    public bool HasTag(string tag) => Tags.Contains(tag);
}

/// <summary>
/// Acceso de solo lectura a los datos estáticos del juego (campeones e items).
/// La implementación real es <c>DataDragonCatalog</c>; hay una versión vacía para
/// cuando aún no cargó (o no hay internet).
/// </summary>
public interface IStaticData
{
    bool IsLoaded { get; }
    string Version { get; }

    string? ChampionNameById(int id);
    StaticChampion? ChampionById(int id);
    StaticChampion? ChampionByName(string name);
    /// <summary>Busca por el id textual de ddragon (independiente del idioma).</summary>
    StaticChampion? ChampionByKey(string key);
    /// <summary>
    /// Resuelve un campeón de la Live Client API: primero por el sufijo de
    /// <c>rawChampionName</c> (<c>game_character_displayname_X</c>, estable entre
    /// idiomas) y luego por el nombre localizado.
    /// </summary>
    StaticChampion? ResolveChampion(string? name, string? rawName);

    StaticItem? ItemById(int id);
    /// <summary>Items completos (finales) comprables en la Grieta.</summary>
    IReadOnlyList<StaticItem> CompletedItems { get; }
    /// <summary>Items completos disponibles en el mapa dado (11 = Grieta, 12 = ARAM).</summary>
    IReadOnlyList<StaticItem> CompletedItemsFor(int mapNumber);
    /// <summary>Items completos (finales), en Grieta, con el tag dado, ordenados por costo desc.</summary>
    IReadOnlyList<StaticItem> CompletedItemsByTag(string tag);
    /// <summary>Items completos que aplican Heridas Graves, ordenados por costo asc.</summary>
    IReadOnlyList<StaticItem> CompletedGrievousWoundsItems();
    /// <summary>Botas terminadas (tier 2), comprables en la Grieta.</summary>
    IReadOnlyList<StaticItem> FinishedBoots { get; }
    /// <summary>Items iniciales de ARAM (tag <c>Lane</c>: Doran's y Guardian's), por precio desc.</summary>
    IReadOnlyList<StaticItem> AramStarterItems { get; }
    /// <summary>Botas terminadas disponibles en el mapa dado.</summary>
    IReadOnlyList<StaticItem> FinishedBootsFor(int mapNumber);
}
