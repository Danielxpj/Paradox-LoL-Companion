using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

public class OpggResponseParserTests
{
    private static ChampionBuildStats ParseFixture()
    {
        var text = OpggMcpClient.ExtractToolText(Fixtures.OpggJayce());
        Assert.NotNull(text);
        var stats = OpggResponseParser.Parse(text!, "Jayce", "ranked", "top");
        Assert.NotNull(stats);
        return stats!;
    }

    [Fact]
    public void Parses_core_items_boots_and_starter()
    {
        var stats = ParseFixture();
        Assert.Equal(new[] { 3070, 3142, 3042, 6699 }, stats.CoreItems!.ItemIds);
        Assert.Equal(0.17, stats.CoreItems.PickRate, 2);
        Assert.True(stats.CoreItems.WinRate is > 0.5 and < 0.6);   // 6120/11228
        Assert.Contains(3158, stats.Boots!.ItemIds);               // Lucidity
        Assert.Contains(1055, stats.Starter!.ItemIds);             // Doran's Blade
    }

    [Fact]
    public void Parses_late_item_candidates()
    {
        var stats = ParseFixture();
        // fourth_items: Serylda 6694, Edge of Night 3814, Voltaic 6699 — fifth: 3814, 6694, 6695
        Assert.Contains(stats.LateItems, s => s.ItemIds.Contains(6694));
        Assert.Contains(stats.LateItems, s => s.ItemIds.Contains(6695));
    }

    [Fact]
    public void Parses_runes_and_skills()
    {
        var stats = ParseFixture();
        var r = stats.Runes!;
        Assert.Equal(8200, r.PrimaryPageId);              // Sorcery
        Assert.Equal(4, r.PrimaryRuneIds.Count);
        Assert.Equal(2, r.SecondaryRuneIds.Count);
        Assert.Equal(3, r.StatModIds.Count);
        Assert.Equal("Sorcery", r.PrimaryPageName);
        Assert.Equal("Q", stats.Skills!.Order[0]);
        Assert.Equal(15, stats.Skills.Order.Count);
    }

    [Fact]
    public void ItemPriorFor_finds_core_and_late_items()
    {
        var stats = ParseFixture();
        var core = stats.ItemPriorFor(3142);   // Youmuu's — en el core set
        Assert.NotNull(core);
        Assert.Equal(0.17, core!.Value.PickRate, 2);
        Assert.True(core.Value.IsCore);
        Assert.True(core.Value.Play > 1000);
        var late = stats.ItemPriorFor(6694);   // Serylda — candidato de 4.º item
        Assert.NotNull(late);
        Assert.False(late!.Value.IsCore);
        Assert.True(late.Value.Play > 0);
        Assert.Null(stats.ItemPriorFor(9999));
    }

    [Fact]
    public void Parses_sixth_items_and_flattens_them_into_late()
    {
        const string text = """
            class LolGetChampionAnalysis: data
            class Data: sixth_items,starter_items
            class SixthItem: ids,ids_names,pick_rate,play,win

            LolGetChampionAnalysis(Data([SixthItem([3026],["Guardian Angel"],0.21,181,109),SixthItem([3142],["Youmuu's Ghostblade"],0.11,90,51),SixthItem([6695],["Serpent's Fang"],0.11,90,43)],SixthItem([1055,2003],["Doran's Blade","Health Potion"],0.91,90004,44040)))
            """;
        var stats = OpggResponseParser.Parse(text, "Jayce", "ranked", "top")!;
        Assert.Equal(3, stats.SixthItems.Count);
        Assert.Equal(new[] { 3026 }, stats.SixthItems[0].ItemIds);
        Assert.Equal(0.21, stats.SixthItems[0].PickRate, 2);
        // El prior también los ve: LateItems aplana 4.º+5.º+6.º.
        Assert.NotNull(stats.ItemPriorFor(3026));
        Assert.False(stats.ItemPriorFor(3026)!.Value.IsCore);
    }

    [Fact]
    public void Splits_late_items_by_slot()
    {
        var stats = ParseFixture();
        // El fixture trae 3 candidatos de 4.º y 3 de 5.º; sixth no estaba grabado.
        Assert.Equal(3, stats.FourthItems.Count);
        Assert.Equal(3, stats.FifthItems.Count);
        Assert.Empty(stats.SixthItems);
        Assert.Equal(stats.FourthItems.Count + stats.FifthItems.Count, stats.LateItems.Count);
    }

    [Fact]
    public void Envelope_extraction_handles_sse_and_errors()
    {
        var plain = """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"hola"}]}}""";
        Assert.Equal("hola", OpggMcpClient.ExtractToolText(plain));
        Assert.Equal("hola", OpggMcpClient.ExtractToolText("event: message\ndata: " + plain + "\n\n"));
        var err = """{"jsonrpc":"2.0","id":1,"result":{"isError":true,"content":[{"type":"text","text":"boom"}]}}""";
        Assert.Null(OpggMcpClient.ExtractToolText(err));
        Assert.Null(OpggMcpClient.ExtractToolText("not json"));
    }

    [Fact]
    public void Roundtrips_through_json()
    {
        var stats = ParseFixture();
        var json = System.Text.Json.JsonSerializer.Serialize(stats);
        var back = System.Text.Json.JsonSerializer.Deserialize<ChampionBuildStats>(json)!;
        Assert.Equal(stats.CoreItems!.ItemIds, back.CoreItems!.ItemIds);
        Assert.Equal(stats.CoreItems.WinRate, back.CoreItems.WinRate);
        Assert.Equal(stats.Runes!.PrimaryRuneNames, back.Runes!.PrimaryRuneNames);
        Assert.Equal(stats.Skills!.Order, back.Skills!.Order);
    }
}
