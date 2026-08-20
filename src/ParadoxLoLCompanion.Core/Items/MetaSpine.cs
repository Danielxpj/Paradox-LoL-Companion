using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.DataDragon;
using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Core.Items;

/// <summary>
/// Un slot del espinazo meta: el item COMPRABLE al que apunta la build real del campeón,
/// con la etiqueta de la lista de la que salió y sus estadísticas crudas (para la razón).
/// </summary>
/// <param name="Rank">Posición en el espinazo, 0 = lo próximo que compra el meta.</param>
/// <param name="SlotLabel">De qué lista de op.gg vino: <c>core</c>, <c>4th</c>, <c>5th</c>, <c>6th</c>.</param>
public sealed record MetaSpineSlot(
    StaticItem Item, int Rank, string SlotLabel, double PickRate, double WinRate, int Play)
{
    public bool IsCore => SlotLabel == "core";
}

/// <summary>
/// Traduce las estadísticas de op.gg (<see cref="ChampionBuildStats"/>) al ESPINAZO de la
/// build: la lista ordenada de items comprables que los jugadores del campeón realmente
/// arman, ya descontando lo que llevás puesto.
///
/// Es la pieza que faltaba. El motor difuso puntuaba items sueltos contra el fit de
/// arquetipo, y ese fit es una suma de TAGS de ddragon — ciego a la magnitud de los stats
/// y, en items de tanque, correlacionado NEGATIVAMENTE con su valor real (rho = −0.26 sobre
/// los 22 items de tanque de ARAM). Por eso Heartsteel (900 de vida en un solo stat, tags
/// [Health, HealthRegen], fit 3.5) quedaba 19.º mientras un item con seis tags chicos
/// ganaba el slot. Ninguna magnitud del prior estadístico arreglaba eso: ni siquiera un
/// bono plano de +8 metía el core meta en el top-3. La conclusión es estructural — el meta
/// tiene que SEMBRAR el ranking, no sumarse a él.
///
/// Puro y sin estado: solo depende del catálogo y de la config.
/// </summary>
public static class MetaSpine
{
    /// <summary>
    /// El espinazo para este campeón y mapa, en orden de compra: primero el core (en el
    /// orden que lo lista op.gg), después los candidatos de 4.º, 5.º y 6.º item por pick
    /// rate. Vacío si no hay estadísticas — el llamador cae al camino difuso de siempre.
    /// </summary>
    /// <param name="ownedTree">
    /// Ids que ya llevás puestos, INCLUYENDO el árbol de construcción de cada uno: los
    /// slots ya comprados salen del espinazo y los rangos se compactan, de modo que
    /// <c>Rank 0</c> siempre es la próxima compra del meta.
    /// </param>
    public static IReadOnlyList<MetaSpineSlot> Build(
        IStaticData data, ItemsConfig config, ChampionBuildStats? stats, int mapNumber,
        IReadOnlySet<int>? ownedTree = null)
    {
        if (stats is null)
            return Array.Empty<MetaSpineSlot>();

        var excluded = new HashSet<string>(config.ExcludeItemTags, StringComparer.OrdinalIgnoreCase);
        var resolved = new List<(StaticItem Item, string Label, double Pick, double Win, int Play)>();

        void AddSet(ItemSetStats? set, string label)
        {
            if (set is null)
                return;
            foreach (var rawId in set.ItemIds)
                if (Resolve(data, config, rawId, mapNumber, excluded) is { } item)
                    resolved.Add((item, label, set.PickRate, set.WinRate, set.Play));
        }

        // El core primero y EN SU ORDEN: op.gg lista la build en orden de compra, y ese
        // orden es la señal. Después los slots tardíos, cada lista por pick rate
        // descendente (dentro de una lista el pick sí es comparable: item suelto vs item
        // suelto; entre el core y una lista tardía NO lo es — el del core es la
        // probabilidad de un combo de 3-4 items, ~0.08, contra ~0.29 de un item suelto).
        AddSet(stats.CoreItems, "core");
        foreach (var (sets, label) in new[]
                 {
                     (stats.FourthItems, "4th"), (stats.FifthItems, "5th"), (stats.SixthItems, "6th"),
                 })
            foreach (var set in sets.OrderByDescending(s => s.PickRate))
                AddSet(set, label);

        // Componentes absorbidos: op.gg lista la Lágrima Y su evolución en el mismo core
        // (Nautilus: [Tear, Heartsteel, Fimbulwinter, Unending Despair], y Winter's
        // Approach se construye DESDE la Lágrima). El slot es el item final; la Lágrima
        // aparece como la compra inmediata de esa carta, vía BuildPathPlanner.
        var absorbed = new HashSet<int>();
        foreach (var (candidate, _, _, _, _) in resolved)
            foreach (var (other, _, _, _, _) in resolved)
                if (other.Id != candidate.Id && TreeContains(data, other, candidate.Id))
                    absorbed.Add(candidate.Id);

        var slots = new List<MetaSpineSlot>();
        var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var takenIds = new HashSet<int>();
        foreach (var (item, label, pick, win, play) in resolved)
        {
            if (absorbed.Contains(item.Id))
                continue;
            // Un componente que NADIE del espinazo absorbe no es un slot de item: es una
            // apertura (va por StarterFor) o un resto de datos. Mostrar una pieza de 400
            // como una de las tres cartas sería peor que mostrar el item final.
            if (item.BuildsIntoSomething)
                continue;
            if (ownedTree is not null && ownedTree.Contains(item.Id))
                continue;
            if (!takenIds.Add(item.Id) || !takenNames.Add(NormalizedName(item.Name)))
                continue;
            slots.Add(new MetaSpineSlot(item, slots.Count, label, pick, win, play));
        }
        return slots;
    }

    /// <summary>
    /// Un id crudo de op.gg → el item comprable del catálogo, o <c>null</c> si no aplica.
    /// Salva las dos trampas de los datos reales: las EVOLUCIONES (Muramana 3042 y
    /// Fimbulwinter 3121 vienen con <c>purchasable=false</c> y <c>from=[]</c>; el puente
    /// de la config las lleva a Manamune 3004 y Winter's Approach 3119) y los ids
    /// POR MAPA (Hubris es 126697 en ARAM y 6697 en la Grieta).
    /// </summary>
    private static StaticItem? Resolve(
        IStaticData data, ItemsConfig config, int rawId, int mapNumber, HashSet<string> excluded)
    {
        var id = config.ItemEvolutions.TryGetValue(rawId, out var buyable) ? buyable : rawId;
        if (data.ItemById(id) is not { } item)
            return null;
        var onMap = mapNumber == 12 ? item.OnAram : item.OnSummonersRift;
        if (!onMap || !item.Purchasable || item.Consumable)
            return null;
        // Botas fuera: siguen decidiéndose en BootsFor (fuera del alcance de este cambio).
        if (item.IsBoots || item.Tags.Any(excluded.Contains))
            return null;
        return item;
    }

    /// <summary>El árbol de construcción de <paramref name="item"/> contiene <paramref name="componentId"/>.</summary>
    private static bool TreeContains(IStaticData data, StaticItem item, int componentId)
    {
        foreach (var fromId in item.From)
        {
            if (fromId == componentId)
                return true;
            if (data.ItemById(fromId) is { } component && TreeContains(data, component, componentId))
                return true;
        }
        return false;
    }

    /// <summary>
    /// El catálogo de ARAM trae 33 pares de nombre duplicado (las variantes 77xxxx), y uno
    /// de ellos se cuela por la puerta de nombres porque el gemelo se llama "The Black
    /// Cleaver" contra "Black Cleaver". Normalizar el artículo cierra ese agujero.
    /// </summary>
    internal static string NormalizedName(string name) =>
        name.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? name[4..].Trim() : name.Trim();
}
