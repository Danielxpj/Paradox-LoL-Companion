using System.Text.Json;
using LoLAdvisor.Core.Connectors.Lcu;
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

public class LcuItemSetWriterTests
{
    private static ItemSetPage Page(string title) => new(title, new[]
    {
        new ItemSetBlock("Core build", new[] { 3070, 3142 }),
        new ItemSetBlock("4th item", new[] { 6694 }),
    });

    [Fact]
    public void Merge_preserves_foreign_sets_and_replaces_ours()
    {
        const string current = """
            {"accountId":42,"timestamp":123,"itemSets":[
              {"title":"Mi página","uid":"u1","blocks":[{"type":"x","items":[{"id":"1001","count":1}]}]},
              {"title":"Paradox: Jayce #1","uid":"u2","blocks":[]}
            ]}
            """;
        var merged = LcuItemSetWriter.MergeSets(current, new[] { Page("Paradox: Jayce #1") }, 126, 11)!;
        using var doc = JsonDocument.Parse(merged);
        var sets = doc.RootElement.GetProperty("itemSets").EnumerateArray().ToList();
        Assert.Equal(2, sets.Count);
        // La ajena sobrevive INTACTA (mismo uid); la vieja nuestra desapareció.
        Assert.Equal("Mi página", sets[0].GetProperty("title").GetString());
        Assert.Equal("u1", sets[0].GetProperty("uid").GetString());
        Assert.Equal("Paradox: Jayce #1", sets[1].GetProperty("title").GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("accountId").GetInt64());
    }

    [Fact]
    public void Set_node_has_the_lcu_shape()
    {
        var node = LcuItemSetWriter.BuildSetNode(Page("Paradox: Jayce #2"), 126, 12);
        using var doc = JsonDocument.Parse(node.ToJsonString());
        var root = doc.RootElement;
        Assert.Equal("Paradox: Jayce #2", root.GetProperty("title").GetString());
        Assert.Equal(126, root.GetProperty("associatedChampions")[0].GetInt32());
        Assert.Equal(12, root.GetProperty("associatedMaps")[0].GetInt32());
        var block = root.GetProperty("blocks")[0];
        Assert.Equal("Core build", block.GetProperty("type").GetString());
        // La LCU espera el id del item como STRING.
        Assert.Equal("3070", block.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(1, block.GetProperty("items")[0].GetProperty("count").GetInt32());
    }

    [Fact]
    public void Unparseable_document_returns_null_never_overwrites()
    {
        Assert.Null(LcuItemSetWriter.MergeSets("not json", new[] { Page("Paradox: X #1") }, 1, 11));
        Assert.Null(LcuItemSetWriter.MergeSets("[1,2,3]", new[] { Page("Paradox: X #1") }, 1, 11));
    }
}
