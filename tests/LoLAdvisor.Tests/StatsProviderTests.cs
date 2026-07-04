using LoLAdvisor.Core.Stats;

namespace LoLAdvisor.Tests;

public class StatsProviderTests
{
    private sealed class FakeClient : IOpggClient
    {
        public int Calls;
        public string? Response;
        public Task<string?> GetChampionAnalysisTextAsync(
            string champion, string gameMode, string position, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Response);
        }
    }

    private static string JayceToolText() =>
        OpggMcpClient.ExtractToolText(Fixtures.OpggJayce())!;

    [Fact]
    public void Maps_positions_and_modes()
    {
        Assert.Equal(("aram", "none"), StatsProvider.MapModeAndPosition("TOP", mapNumber: 12));
        Assert.Equal(("ranked", "top"), StatsProvider.MapModeAndPosition("TOP", 11));
        Assert.Equal(("ranked", "mid"), StatsProvider.MapModeAndPosition("MIDDLE", 11));
        Assert.Equal(("ranked", "adc"), StatsProvider.MapModeAndPosition("BOTTOM", 11));
        Assert.Equal(("ranked", "support"), StatsProvider.MapModeAndPosition("UTILITY", 11));
        Assert.Equal(("ranked", "all"), StatsProvider.MapModeAndPosition("", 11));
        Assert.Equal(("ranked", "all"), StatsProvider.MapModeAndPosition(null, 11));
    }

    [Fact]
    public void Converts_ddragon_key_to_upper_snake()
    {
        Assert.Equal("JAYCE", StatsProvider.ToOpggName("Jayce"));
        Assert.Equal("MONKEY_KING", StatsProvider.ToOpggName("MonkeyKing"));
        Assert.Equal("DR_MUNDO", StatsProvider.ToOpggName("DrMundo"));
        Assert.Equal("JARVAN_IV", StatsProvider.ToOpggName("JarvanIV"));
        // KSante no tiene frontera minúscula→mayúscula: queda KSANTE, que OP.GG
        // también acepta (el matching del servidor tolera ambas formas).
        Assert.Equal("KSANTE", StatsProvider.ToOpggName("KSante"));
    }

    [Fact]
    public async Task Fetches_parses_and_caches()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loladvisor-tests-" + Guid.NewGuid());
        try
        {
            var client = new FakeClient { Response = JayceToolText() };
            var provider = new StatsProvider(client, new StatsCache(dir));

            var first = await provider.GetAsync("Jayce", "TOP", 11, "16.13.1");
            Assert.NotNull(first);
            Assert.Equal(1, client.Calls);

            var second = await provider.GetAsync("Jayce", "TOP", 11, "16.13.1");
            Assert.NotNull(second);
            Assert.Equal(1, client.Calls);   // cache hit: sin segunda llamada
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Client_failure_returns_null()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loladvisor-tests-" + Guid.NewGuid());
        try
        {
            var provider = new StatsProvider(new FakeClient { Response = null }, new StatsCache(dir));
            Assert.Null(await provider.GetAsync("Jayce", "TOP", 11, "16.13.1"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
