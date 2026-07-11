using System.Text.Json;

namespace ParadoxLoLCompanion.Core.Config;

/// <summary>
/// Parámetros de las reglas de consejo, externalizados a JSON para poder ajustarlos
/// sin recompilar. Los valores por defecto (aquí) se usan si no hay archivo o falla la lectura.
/// </summary>
public sealed class AdvisorConfig
{
    public GoldConfig Gold { get; set; } = new();
    public CsConfig CsPerMinute { get; set; } = new();
    public ItemsConfig Items { get; set; } = new();
    public MayhemConfig Mayhem { get; set; } = new();

    public static AdvisorConfig Default => new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>Carga la config del archivo; devuelve la por defecto si no existe o está corrupto.</summary>
    public static AdvisorConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return Default;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AdvisorConfig>(json, Options) ?? Default;
        }
        catch
        {
            return Default;
        }
    }

    /// <summary>Serializa la config (para generar el archivo de ejemplo).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);
}

public sealed class GoldConfig
{
    public int ComponentGold { get; set; } = 1300;
    public int LegendaryGold { get; set; } = 3000;
}

/// <summary>
/// ARAM: Mayhem (augments). Las opciones de augment NO se exponen por API; lo que sí es
/// determinista: los picks se desbloquean al inicio y en ciertos niveles, y se eligen
/// estando muerto.
/// </summary>
public sealed class MayhemConfig
{
    /// <summary>Colas que corresponden a Mayhem (la LCU las reporta; 2400 = ARAM: Mayhem).</summary>
    public List<int> QueueIds { get; set; } = new() { 2400 };
    /// <summary>Niveles donde se desbloquea un pick adicional (además del inicial).</summary>
    public List<int> PickLevels { get; set; } = new() { 7, 11, 15 };
}

public sealed class CsConfig
{
    public double Target { get; set; } = 7.0;
    public double MinMinutes { get; set; } = 2.0;
}

public sealed class ItemsConfig
{
    public int MinCompletedItemGold { get; set; } = 1100;
    public int MinFinishedBootsGold { get; set; } = 900;
    /// <summary>Id de las Botas básicas: "botas terminadas" = las que se construyen directo desde ellas.</summary>
    public int BasicBootsId { get; set; } = 1001;
    public int MaxRecommendations { get; set; } = 3;

    public List<string> SustainTags { get; set; } = new() { "LifeSteal", "SpellVamp", "Omnivamp" };
    public List<string> ExcludeItemTags { get; set; } = new() { "Boots", "Trinket", "GoldPer" };

    // Detección por descripción (localizable: se incluyen es + en). Nota: los items
    // nuevos ya no dicen "Grievous"/"Heridas graves": en_US dice "40% Wounds" (Quimpunk,
    // Cota de Espinas) y es_MX "40% de Heridas" — por eso las variantes cortas.
    public List<string> GrievousWoundsKeywords { get; set; } = new()
        { "Grievous", "Wounds", "Heridas graves", "% de Heridas" };
    public List<string> CleanseKeywords { get; set; } = new()
        { "crowd control debuffs", "debilitaciones de control", "pérdida de control" };
    public List<string> ShieldBreakerKeywords { get; set; } = new() { "Shield Reaver", "Rompeescudos" };
    // Anti-crítico (Presagio de Randuin). Se usa la construcción "daño DE los golpes
    // críticos" / "from critical strikes" para no marcar items que OTORGAN crítico
    // (Filo Infinito dice "tus golpes críticos infligen…", que no contiene estas frases).
    public List<string> CritReductionKeywords { get; set; } = new()
        { "from critical strikes", "de los golpes críticos", "de golpes críticos" };
    /// <summary>Detecta items de letalidad (pen plana: mata squishies, no tanques).</summary>
    public List<string> LethalityKeywords { get; set; } = new() { "Lethality", "letalidad" };

    /// <summary>
    /// Items de dupla/soporte (escudan o potencian a un aliado): solo tienen sentido
    /// en el soporte — fuera del pool para laners. Zeke's, Knight's Vow, Locket, Redemption.
    /// </summary>
    public List<int> SupportOnlyItemIds { get; set; } = new() { 3050, 3109, 3190, 3107 };

    /// <summary>
    /// Grupos "límite de 1" del juego que ddragon no expone de NINGUNA forma (ni pasiva
    /// compartida ni componente común): pen mágica del Vacío (Void Staff / Cryptbloom /
    /// Bloodletter's Curse) y Last Whisper (Lord Dominik's / Mortal Reminder / Serylda's).
    /// Teniendo uno, comprar otro del grupo es ilegal en la tienda. La membresía también
    /// se resuelve por nombre, para cubrir las variantes con id duplicado del catálogo.
    /// </summary>
    public List<List<int>> ExclusiveItemGroups { get; set; } = new()
    {
        new() { 3135, 3137, 8010 },   // Void Staff, Cryptbloom, Bloodletter's Curse
        new() { 3036, 3033, 6694 },   // Lord Dominik's, Mortal Reminder, Serylda's Grudge
    };

    /// <summary>Items de stacks que se pierden al morir (Mejai's + Dark Seal).</summary>
    public List<int> SnowballItemIds { get; set; } = new() { 3041, 1082 };
    /// <summary>Kills+asistencias mínimas para recomendar un item de stacks.</summary>
    public int SnowballMinTakedowns { get; set; } = 3;
    /// <summary>Muertes máximas para recomendar un item de stacks.</summary>
    public int SnowballMaxDeaths { get; set; } = 3;
    /// <summary>Muertes desde las que se sugiere vender el item de stacks (si K &lt; D).</summary>
    public int SnowballSellDeaths { get; set; } = 4;

    /// <summary>Nombres localizados del recurso maná en champion.json (partype). en + es.</summary>
    public List<string> ManaResourceNames { get; set; } = new() { "Mana", "Maná" };

    // Consejos de late game. Ids estables de ddragon: elixires por perfil y Control Ward.
    public int ElixirOfIronId { get; set; } = 2138;
    public int ElixirOfSorceryId { get; set; } = 2139;
    public int ElixirOfWrathId { get; set; } = 2140;
    public int ControlWardId { get; set; } = 2055;
    /// <summary>Desde cuándo (segundos) insistir con la Control Ward en la Grieta.</summary>
    public double ControlWardAdviceSeconds { get; set; } = 480;

    /// <summary>
    /// Evolución → forma comprable. OP.GG registra la evolución (Muramana) pero el
    /// catálogo recomienda lo que se puede comprar (Manamune); ddragon NO enlaza
    /// ambas por from/into, así que el puente es explícito. Tear + Winter's Approach.
    /// </summary>
    public Dictionary<int, int> ItemEvolutions { get; set; } = new()
    {
        [3042] = 3004,   // Muramana → Manamune
        [3040] = 3003,   // Seraph's Embrace → Archangel's Staff
        [3121] = 3119,   // Fimbulwinter → Winter's Approach
    };

    // Conocimiento de campeones (claves = id textual de ddragon, p.ej. "MonkeyKing").
    public List<string> HealerChampions { get; set; } = new()
    {
        "Aatrox", "Briar", "DrMundo", "Fiora", "Illaoi", "Kayn", "Maokai", "Nami",
        "Seraphine", "Sona", "Soraka", "Swain", "Sylas", "Vladimir", "Warwick", "Yuumi", "Zac",
    };
    public List<string> ShieldChampions { get; set; } = new()
        { "Camille", "Janna", "Karma", "Lulu", "Orianna", "Riven", "Sett", "Shen" };
    public List<string> HeavyCcChampions { get; set; } = new()
    {
        "Alistar", "Amumu", "Ashe", "Leona", "Lissandra", "Malzahar", "Maokai", "Morgana",
        "Nautilus", "Rakan", "Rell", "Sejuani", "Skarner", "Thresh", "Zoe",
    };
    public List<string> SuppressionChampions { get; set; } = new()
        { "Malzahar", "Skarner", "Urgot", "Warwick" };
    /// <summary>
    /// Campeones cuyo kit hace daño por % de vida máxima o daño verdadero: apilar una
    /// sola resistencia (o HP puro) rinde menos contra ellos.
    /// </summary>
    public List<string> PercentHpTrueDamageChampions { get; set; } = new()
    {
        "Vayne", "Fiora", "KogMaw", "Gwen", "Belveth", "Kayle", "Kalista", "Varus",
    };
    /// <summary>
    /// Campeones con enganche/pick duro o lockdown puntual que te obligan a comprar
    /// supervivencia (Ángel de la Guarda, Zhonya, QSS, Banshee).
    /// </summary>
    public List<string> HardEngageChampions { get; set; } = new()
    {
        "Malphite", "Amumu", "Leona", "Nautilus", "Rell", "Sejuani", "Zac", "JarvanIV",
        "Vi", "Kennen", "MonkeyKing", "Sett", "Skarner", "Ornn", "Rammus",
    };

    /// <summary>Correcciones al perfil de daño inferido de `info` (valores: Physical/Magical/Mixed).</summary>
    public Dictionary<string, string> DamageProfileOverrides { get; set; } = new()
    {
        ["Gwen"] = "Magical",
        ["Diana"] = "Magical",
        ["Fizz"] = "Magical",
        ["Sylas"] = "Magical",
        ["Kennen"] = "Magical",
        ["Kaisa"] = "Mixed",
        // AP con info.attack−magic en (−3, 0]: sin esto caen en AdAssassin/AdFighter
        // y el asesor les ofrece items AD (Terminus a Ekko).
        ["Ekko"] = "Magical",
        ["Nidalee"] = "Magical",
        ["Elise"] = "Magical",
        ["Gragas"] = "Magical",
    };

    /// <summary>Correcciones al arquetipo de build inferido de los tags.</summary>
    public Dictionary<string, string> ArchetypeOverrides { get; set; } = new();

    // Detección de build por inventario (Garen tanque vs. daño, Janna support vs. AP).
    /// <summary>Inferir el arquetipo desde los items que el jugador realmente compró.</summary>
    public bool DetectBuildFromItems { get; set; } = true;
    /// <summary>"Oro" de ventaja del arquetipo por defecto: más alto = detección más conservadora.</summary>
    public double BuildDetectionPriorGold { get; set; } = 1200;

    /// <summary>Pesos por tag de item para cada arquetipo; si falta uno, se usan los del código.</summary>
    public Dictionary<string, Dictionary<string, double>>? ArchetypeTagWeights { get; set; }

    // Umbrales del análisis de amenaza.
    /// <summary>Fracción de la amenaza ponderada con curación/sustain que dispara anti-heal.</summary>
    public double SustainThreshold { get; set; } = 0.25;
    /// <summary>Oro en items con sustain (starters/botas excluidos) para μ=0 / μ=1 del grado por enemigo: un Doran's/Vampiric barato no cuenta como robo de vida real, una build de lifesteal sí.</summary>
    public double SustainGoldFoot { get; set; } = 800;
    public double SustainGoldShoulder { get; set; } = 3000;
    /// <summary>Con GW aliado presente, el bono anti-heal propio se amortigua salvo sustain extremo (foot..shoulder del grado Sustain).</summary>
    public double AllyGwDampFoot { get; set; } = 0.55;
    public double AllyGwDampShoulder { get; set; } = 0.9;
    /// <summary>Armadura total comprada por el equipo enemigo que dispara penetración.</summary>
    public double ArmorStackThreshold { get; set; } = 150;
    /// <summary>RM total comprada por el equipo enemigo que dispara penetración mágica.</summary>
    public double MrStackThreshold { get; set; } = 120;
    /// <summary>Share de daño (físico o mágico) desde el cual se considera sesgado.</summary>
    public double SkewedDamageShare { get; set; } = 0.62;
    /// <summary>
    /// Fit relativo (mi arquetipo vs. el arquetipo que mejor explica el item) bajo el
    /// cual un item ya comprado se sugiere vender (si no cumple un rol situacional).
    /// </summary>
    public double SellFitRatioThreshold { get; set; } = 0.5;
    /// <summary>Peso relativo al promedio desde el cual un asesino se considera "fed".</summary>
    public double BurstThreshold { get; set; } = 1.6;
    /// <summary>Cantidad de campeones con CC pesado que sugiere botas Mercurio.</summary>
    public int CcCountForMercs { get; set; } = 2;

    // ARAM (mapa 12): peleas constantes y compras al reaparecer.
    /// <summary>Segundos de partida durante los que se sugiere la compra inicial (ARAM).</summary>
    public double StarterWindowSeconds { get; set; } = 90;
    /// <summary>Factor sobre SustainThreshold en ARAM (anti-curación más temprana).</summary>
    public double AramSustainThresholdFactor { get; set; } = 0.6;
    /// <summary>Multiplicador de "te alcanza ya" en ARAM (no hay recall: compras al morir).</summary>
    public double AramAffordabilityBoost { get; set; } = 1.25;
}
