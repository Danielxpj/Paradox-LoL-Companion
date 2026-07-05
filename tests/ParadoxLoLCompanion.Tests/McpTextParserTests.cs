using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

public class McpTextParserTests
{
    private const string Sample = """
        class Root: name,child,ids,rate
        class Child: play,win

        Root("Jayce (\"top\")",Child(100,55),[3070,3042],0.17)
        """;

    [Fact]
    public void Parses_fields_by_header_position()
    {
        var root = McpTextParser.Parse(Sample)!;
        Assert.Equal("Root", root.ClassName);
        Assert.Equal("Jayce (\"top\")", root.Str("name"));   // escapes dentro de strings
        Assert.Equal(0.17, root.Num("rate"), 3);
        Assert.Equal(new List<int> { 3070, 3042 }, root.IntList("ids"));
        var child = root.Obj("child")!;
        Assert.Equal(100, child.Num("play"));
        Assert.Equal(55, child.Num("win"));
    }

    [Fact]
    public void Missing_field_is_null_and_helpers_degrade()
    {
        var root = McpTextParser.Parse("class R: a,b\n\nR(1)")!;   // b no llega
        Assert.Equal(1, root.Num("a"));
        Assert.Null(root.Obj("b"));
        Assert.Equal("", root.Str("b"));
        Assert.Empty(root.IntList("b"));
    }

    [Fact]
    public void List_of_objects_and_nulls()
    {
        var root = McpTextParser.Parse("class R: items\nclass I: v\n\nR([I(1),I(2),None])")!;
        var items = root.ObjList("items");
        Assert.Equal(2, items.Count);              // None se descarta en ObjList
        Assert.Equal(2, items[1].Num("v"));
    }

    [Fact]
    public void Garbage_returns_null_not_throw()
    {
        Assert.Null(McpTextParser.Parse("not the format"));
        Assert.Null(McpTextParser.Parse(""));
    }
}
