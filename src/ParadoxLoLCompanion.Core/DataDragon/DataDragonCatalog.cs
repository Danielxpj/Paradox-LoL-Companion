using System.Text.Json;
using System.Text.Json.Serialization;
using ParadoxLoLCompanion.Core.Config;

namespace ParadoxLoLCompanion.Core.DataDragon;

/// <summary>
/// Catálogo estático construido a partir de <c>champion.json</c> + <c>item.json</c> de
/// Data Dragon. Inmutable una vez creado. Provee búsquedas por id/nombre/tag.
/// </summary>
public sealed class DataDragonCatalog : IStaticData
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly Dictionary<int, StaticChampion> _championsById;
    private readonly Dictionary<string, StaticChampion> _championsByName;
    private readonly Dictionary<string, StaticChampion> _championsByKey;
    private readonly Dictionary<int, StaticItem> _itemsById;
    private readonly List<StaticItem> _completedItems;
    private readonly List<StaticItem> _completedItemsAram;
    private readonly List<StaticItem> _finishedBoots;
    private readonly List<StaticItem> _finishedBootsAram;
    private readonly List<StaticItem> _aramStarters;

    public bool IsLoaded { get; }
    public string Version { get; }

    private DataDragonCatalog(
        string version,
        Dictionary<int, StaticChampion> championsById,
        Dictionary<int, StaticItem> itemsById,
        bool isLoaded,
        ItemsConfig config)
    {
        Version = version;
        _championsById = championsById;
        _itemsById = itemsById;
        IsLoaded = isLoaded;

        _championsByName = new Dictionary<string, StaticChampion>(StringComparer.OrdinalIgnoreCase);
        _championsByKey = new Dictionary<string, StaticChampion>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in championsById.Values)
        {
            _championsByName[c.Name] = c;
            if (!string.IsNullOrEmpty(c.Key))
                _championsByKey[c.Key] = c;
        }

        var excluded = new HashSet<string>(config.ExcludeItemTags, StringComparer.OrdinalIgnoreCase);

        // From.Count > 0: los items de modos/perks (Espátula Dorada, prismáticos de
        // Arena, mercado negro…) vienen con purchasable=true en su mapa pero sin árbol
        // de construcción; todo item final comprable de verdad se arma de componentes.
        List<StaticItem> Completed(Func<StaticItem, bool> onMap) => itemsById.Values
            .Where(i => onMap(i) && i.Purchasable && !i.BuildsIntoSomething
                        && !i.Consumable && i.GoldTotal >= config.MinCompletedItemGold
                        && i.From.Count > 0
                        && !i.Tags.Any(excluded.Contains))
            .ToList();

        // Terminadas = construidas directo desde las Botas básicas (tier 2). Las tier 3
        // (mejoras de Hazañas de Fuerza) dependen de un logro de equipo, y como las
        // tier 2 ahora traen `into` hacia ellas, exigir "no construye hacia nada"
        // dejaría solo tier 3 en la Grieta y una lista vacía en ARAM.
        List<StaticItem> Boots(Func<StaticItem, bool> onMap) => itemsById.Values
            .Where(i => i.IsBoots && onMap(i) && i.Purchasable
                        && i.From.Contains(config.BasicBootsId)
                        && i.GoldTotal >= config.MinFinishedBootsGold)
            .OrderByDescending(i => i.GoldTotal)
            .ToList();

        _completedItems = Completed(i => i.OnSummonersRift);
        _completedItemsAram = Completed(i => i.OnAram);
        _finishedBoots = Boots(i => i.OnSummonersRift);
        _finishedBootsAram = Boots(i => i.OnAram);

        // Starters de ARAM: ddragon los marca con el tag Lane (Doran's + Guardian's).
        // GoldPer (quest de support) y Jungle no son aperturas de ARAM.
        _aramStarters = itemsById.Values
            .Where(i => i.OnAram && i.Purchasable && i.HasTag("Lane")
                        && !i.HasTag("GoldPer") && !i.HasTag("Jungle"))
            .OrderByDescending(i => i.GoldTotal)
            .ToList();
    }

    /// <summary>Catálogo vacío (no cargado): usado como valor por defecto seguro.</summary>
    public static DataDragonCatalog Empty { get; } =
        new("", new(), new(), isLoaded: false, new ItemsConfig());

    // --- Consultas ---

    public string? ChampionNameById(int id) => ChampionById(id)?.Name;

    public StaticChampion? ChampionById(int id) => _championsById.GetValueOrDefault(id);

    public StaticChampion? ChampionByName(string name) =>
        string.IsNullOrEmpty(name) ? null : _championsByName.GetValueOrDefault(name);

    public StaticChampion? ChampionByKey(string key) =>
        string.IsNullOrEmpty(key) ? null : _championsByKey.GetValueOrDefault(key);

    public StaticChampion? ResolveChampion(string? name, string? rawName)
    {
        // rawChampionName llega como "game_character_displayname_MonkeyKing": el sufijo
        // es el Key de ddragon, estable aunque cliente y catálogo estén en idiomas distintos.
        if (!string.IsNullOrEmpty(rawName))
        {
            var idx = rawName.LastIndexOf('_');
            var key = idx >= 0 ? rawName[(idx + 1)..] : rawName;
            var byKey = ChampionByKey(key);
            if (byKey is not null)
                return byKey;
        }
        return name is null ? null : ChampionByName(name);
    }

    public StaticItem? ItemById(int id) => _itemsById.GetValueOrDefault(id);

    public IReadOnlyList<StaticItem> CompletedItems => _completedItems;

    public IReadOnlyList<StaticItem> CompletedItemsFor(int mapNumber) =>
        mapNumber == 12 ? _completedItemsAram : _completedItems;

    public IReadOnlyList<StaticItem> FinishedBoots => _finishedBoots;

    public IReadOnlyList<StaticItem> AramStarterItems => _aramStarters;

    public IReadOnlyList<StaticItem> FinishedBootsFor(int mapNumber) =>
        mapNumber == 12 ? _finishedBootsAram : _finishedBoots;

    public IReadOnlyList<StaticItem> CompletedItemsByTag(string tag) =>
        _completedItems.Where(i => i.HasTag(tag)).OrderByDescending(i => i.GoldTotal).ToList();

    public IReadOnlyList<StaticItem> CompletedGrievousWoundsItems() =>
        _completedItems.Where(i => i.AppliesGrievousWounds).OrderBy(i => i.GoldTotal).ToList();

    // --- Construcción desde JSON ---

    public static DataDragonCatalog FromJson(string version, string championJson, string itemJson, ItemsConfig? config = null)
    {
        config ??= new ItemsConfig();
        var champFile = JsonSerializer.Deserialize<ChampionFile>(championJson, JsonOptions) ?? new();
        var itemFile = JsonSerializer.Deserialize<ItemFile>(itemJson, JsonOptions) ?? new();

        var champions = new Dictionary<int, StaticChampion>();
        foreach (var (textKey, entry) in champFile.Data)
        {
            if (!int.TryParse(entry.Key, out var id))
                continue;
            var partype = entry.Partype ?? "";
            champions[id] = new StaticChampion
            {
                Id = id,
                Key = string.IsNullOrEmpty(entry.Id) ? textKey : entry.Id,
                Name = entry.Name,
                Tags = entry.Tags.AsReadOnly(),
                Partype = partype,
                UsesMana = partype.Length == 0
                    || config.ManaResourceNames.Contains(partype, StringComparer.OrdinalIgnoreCase),
                Info = new ChampionInfo
                {
                    Attack = entry.Info?.Attack ?? 0,
                    Defense = entry.Info?.Defense ?? 0,
                    Magic = entry.Info?.Magic ?? 0,
                },
            };
        }

        var items = new Dictionary<int, StaticItem>();
        foreach (var (key, entry) in itemFile.Data)
        {
            if (!int.TryParse(key, out var id))
                continue;
            items[id] = new StaticItem
            {
                Id = id,
                Name = entry.Name,
                GoldTotal = entry.Gold.Total,
                SellGold = entry.Gold.Sell,
                Purchasable = entry.Gold.Purchasable,
                Tags = CanonicalTags(entry.Tags),
                OnSummonersRift = entry.Maps.TryGetValue("11", out var m11) && m11,
                OnAram = entry.Maps.TryGetValue("12", out var m12) && m12,
                BuildsIntoSomething = entry.Into is { Count: > 0 },
                Consumable = entry.Consumed == true || entry.Tags.Contains("Consumable"),
                AppliesGrievousWounds = MentionsAny(entry.Description, config.GrievousWoundsKeywords),
                RemovesCc = MentionsAny(entry.Description, config.CleanseKeywords),
                BreaksShields = MentionsAny(entry.Description, config.ShieldBreakerKeywords),
                ReducesCritDamage = MentionsAny(entry.Description, config.CritReductionKeywords),
                GrantsTenacity = MentionsAny(entry.Description, config.TenacityKeywords),
                HasLethality = MentionsAny(entry.Description, config.LethalityKeywords),
                PassiveNames = ParsePassiveNames(entry.Description),
                Depth = entry.Depth ?? 1,
                From = ParseIds(entry.From),
                Armor = Stat(entry.Stats, "FlatArmorMod"),
                SpellBlock = Stat(entry.Stats, "FlatSpellBlockMod"),
                Health = Stat(entry.Stats, "FlatHPPoolMod"),
                AttackDamage = Stat(entry.Stats, "FlatPhysicalDamageMod"),
                AbilityPower = Stat(entry.Stats, "FlatMagicDamageMod"),
                AttackSpeedPct = Stat(entry.Stats, "PercentAttackSpeedMod"),
                CritChance = Stat(entry.Stats, "FlatCritChanceMod"),
                Mana = Stat(entry.Stats, "FlatMPPoolMod"),
                MoveSpeed = Stat(entry.Stats, "FlatMovementSpeedMod"),
                LifeStealPct = Stat(entry.Stats, "PercentLifeStealMod"),
                HealthRegen = Stat(entry.Stats, "FlatHPRegenMod"),
            };
        }

        return new DataDragonCatalog(version, champions, items, isLoaded: true, config);
    }

    private static double Stat(Dictionary<string, double>? stats, string name) =>
        stats is not null && stats.TryGetValue(name, out var v) ? v : 0;

    /// <summary>
    /// Unifica tags duplicados por época: los items viejos traen CooldownReduction y los
    /// nuevos AbilityHaste (muchos traen ambos), y algunos usan MagicResist en vez de
    /// SpellBlock. Sin esto, un mismo stat contaría dos veces al puntuar.
    /// </summary>
    private static IReadOnlySet<string> CanonicalTags(List<string> tags)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
            set.Add(tag switch
            {
                "CooldownReduction" => "AbilityHaste",
                "MagicResist" => "SpellBlock",
                _ => tag,
            });
        return set;
    }

    private static IReadOnlyList<int> ParseIds(List<string>? raw)
    {
        if (raw is null || raw.Count == 0)
            return Array.Empty<int>();
        var result = new List<int>(raw.Count);
        foreach (var s in raw)
            if (int.TryParse(s, out var id))
                result.Add(id);
        return result;
    }

    private static readonly System.Text.RegularExpressions.Regex PassiveTag =
        new(@"<passive>([^<]+)</passive>", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Extrae los nombres de pasiva de la descripción. Son la única marca que queda en
    /// ddragon de los grupos "límite de 1" del juego (el texto de reglas ya no viene).
    /// </summary>
    private static IReadOnlySet<string> ParsePassiveNames(string? description)
    {
        if (string.IsNullOrEmpty(description))
            return new HashSet<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in PassiveTag.Matches(description))
        {
            var name = m.Groups[1].Value.Trim();
            if (name.Length > 0)
                names.Add(name);
        }
        return names;
    }

    /// <summary>Detecta si la descripción menciona alguna de las palabras clave (según config/idioma).</summary>
    private static bool MentionsAny(string? description, IEnumerable<string> keywords)
    {
        if (string.IsNullOrEmpty(description))
            return false;
        return keywords.Any(k => description.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    // --- DTOs de Data Dragon (privados) ---

    private sealed class ChampionFile
    {
        public Dictionary<string, ChampionEntry> Data { get; set; } = new();
    }

    private sealed class ChampionEntry
    {
        public string Id { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public string? Partype { get; set; }
        public InfoEntry? Info { get; set; }
    }

    private sealed class InfoEntry
    {
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int Magic { get; set; }
    }

    private sealed class ItemFile
    {
        public Dictionary<string, ItemEntry> Data { get; set; } = new();
    }

    private sealed class ItemEntry
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public Gold Gold { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public Dictionary<string, bool> Maps { get; set; } = new();
        public List<string>? Into { get; set; }
        public List<string>? From { get; set; }
        public int? Depth { get; set; }
        public bool? Consumed { get; set; }
        public Dictionary<string, double>? Stats { get; set; }
    }

    private sealed class Gold
    {
        public int Total { get; set; }
        public int Sell { get; set; }
        public bool Purchasable { get; set; }
    }
}
