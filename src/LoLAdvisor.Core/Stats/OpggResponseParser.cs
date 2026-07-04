namespace LoLAdvisor.Core.Stats;

/// <summary>
/// Convierte el texto compacto de <c>lol_get_champion_analysis</c> en
/// <see cref="ChampionBuildStats"/>. Navega por NOMBRE de campo (vía
/// <see cref="McpTextParser"/>), nunca por posición, y tolera campos ausentes:
/// cada sección es opcional según los desired_output_fields pedidos.
/// </summary>
public static class OpggResponseParser
{
    /// <summary><c>null</c> si el texto no parsea o no trae el bloque data.</summary>
    public static ChampionBuildStats? Parse(string toolText, string championKey, string gameMode, string position)
    {
        var root = McpTextParser.Parse(toolText);
        var data = root?.Obj("data");
        if (data is null)
            return null;

        return new ChampionBuildStats
        {
            ChampionKey = championKey,
            GameMode = gameMode,
            Position = position,
            CoreItems = ItemSet(data.Obj("core_items")),
            Boots = ItemSet(data.Obj("boots")),
            Starter = ItemSet(data.Obj("starter_items")),
            FourthItems = data.ObjList("fourth_items")
                .Select(ItemSet).Where(s => s is not null).Select(s => s!).ToList(),
            FifthItems = data.ObjList("fifth_items")
                .Select(ItemSet).Where(s => s is not null).Select(s => s!).ToList(),
            SixthItems = data.ObjList("sixth_items")
                .Select(ItemSet).Where(s => s is not null).Select(s => s!).ToList(),
            LateItems = data.ObjList("fourth_items").Concat(data.ObjList("fifth_items"))
                .Concat(data.ObjList("sixth_items"))
                .Select(ItemSet).Where(s => s is not null).Select(s => s!).ToList(),
            Runes = Runes(data.Obj("runes")),
            Skills = Skills(data.Obj("skills")),
            WinRate = data.Obj("summary")?.Obj("average_stats")?.Num("win_rate") ?? 0,
        };
    }

    private static ItemSetStats? ItemSet(McpObject? o) =>
        o is null || o.IntList("ids").Count == 0
            ? null
            : new ItemSetStats(o.IntList("ids"), o.Num("pick_rate"), (int)o.Num("play"), (int)o.Num("win"));

    private static RunePageStats? Runes(McpObject? o) =>
        o is null || o.IntList("primary_rune_ids").Count == 0
            ? null
            : new RunePageStats(
                (int)o.Num("primary_page_id"), o.Str("primary_page_name"),
                o.IntList("primary_rune_ids"), o.StrList("primary_rune_names"),
                (int)o.Num("secondary_page_id"), o.Str("secondary_page_name"),
                o.IntList("secondary_rune_ids"), o.StrList("secondary_rune_names"),
                o.IntList("stat_mod_ids"), o.Num("pick_rate"));

    private static SkillOrderStats? Skills(McpObject? o) =>
        o is null || o.StrList("order").Count == 0
            ? null
            : new SkillOrderStats(o.StrList("order"), o.Num("pick_rate"));
}
