using System.Text.Json;
using LoLAdvisor.Core.Connectors.Lcu;
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

public class LcuRuneWriterTests
{
    [Fact]
    public void Builds_the_lcu_page_payload()
    {
        var runes = new RunePageStats(
            8200, "Sorcery",
            new[] { 8230, 8226, 8233, 8236 }, new[] { "A", "B", "C", "D" },
            8300, "Inspiration",
            new[] { 8304, 8345 }, new[] { "E", "F" },
            new[] { 5008, 5008, 5001 }, 0.19);

        var json = LcuRuneWriter.BuildPagePayload("LoLAdvisor: Jayce", runes);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("LoLAdvisor: Jayce", root.GetProperty("name").GetString());
        Assert.Equal(8200, root.GetProperty("primaryStyleId").GetInt32());
        Assert.Equal(8300, root.GetProperty("subStyleId").GetInt32());
        Assert.Equal(
            new[] { 8230, 8226, 8233, 8236, 8304, 8345, 5008, 5008, 5001 },
            root.GetProperty("selectedPerkIds").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        Assert.True(root.GetProperty("current").GetBoolean());
    }
}
