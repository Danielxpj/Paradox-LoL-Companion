using System.Text.Json;
using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

public class OpggMcpClientTests
{
    [Fact]
    public void Builds_a_valid_tools_call_payload()
    {
        var json = OpggMcpClient.BuildToolCallJson("JAYCE", "ranked", "top");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal("tools/call", root.GetProperty("method").GetString());
        var p = root.GetProperty("params");
        Assert.Equal("lol_get_champion_analysis", p.GetProperty("name").GetString());
        var args = p.GetProperty("arguments");
        Assert.Equal("JAYCE", args.GetProperty("champion").GetString());
        Assert.Equal("ranked", args.GetProperty("game_mode").GetString());
        Assert.Equal("top", args.GetProperty("position").GetString());
        // Los campos pedidos incluyen las secciones que consume el parser.
        var fields = args.GetProperty("desired_output_fields").EnumerateArray()
            .Select(f => f.GetString()!).ToList();
        Assert.Contains(fields, f => f.StartsWith("data.core_items"));
        Assert.Contains(fields, f => f.StartsWith("data.runes"));
        Assert.Contains(fields, f => f.StartsWith("data.skills"));
    }
}
