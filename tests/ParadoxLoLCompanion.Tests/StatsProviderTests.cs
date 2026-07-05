using ParadoxLoLCompanion.Core.Stats;

namespace ParadoxLoLCompanion.Tests;

public class StatsProviderTests
{
    private sealed class FakeClient : IOpggClient
    {
        public int Calls;
        public string? Response;
        public string? LastMode;
        public string? LastPosition;
        public int PositionCalls;
        public string? MainPosition;

        public Task<string?> GetChampionAnalysisTextAsync(
            string champion, string gameMode, string position, CancellationToken ct = default)
        {
            Calls++;
            LastMode = gameMode;
            LastPosition = position;
            return Task.FromResult(Response);
        }

        public Task<string?> GetChampionMainPositionAsync(string champion, CancellationToken ct = default)
        {
            PositionCalls++;
            return Task.FromResult(MainPosition);
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
    public async Task Aram_sends_a_valid_api_position_but_keeps_none_for_cache()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loladvisor-tests-" + Guid.NewGuid());
        try
        {
            var client = new FakeClient { Response = JayceToolText() };
            var provider = new StatsProvider(client, new StatsCache(dir));

            var stats = await provider.GetAsync("Ahri", "MIDDLE", 12, "16.13.1");
            Assert.NotNull(stats);
            // OP.GG exige un rol concreto aunque el modo sea aram (rechaza "none"/"all").
            Assert.Equal("aram", client.LastMode);
            Assert.Equal("mid", client.LastPosition);
            // Para caché y UI la posición sigue siendo "none" (ARAM no tiene roles).
            Assert.Equal("none", stats!.Position);

            var second = await provider.GetAsync("Ahri", "TOP", 12, "16.13.1");
            Assert.NotNull(second);
            Assert.Equal(1, client.Calls);   // mismo cache key aunque cambie la posición viva
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Unknown_rift_position_resolves_the_champions_main_role()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loladvisor-tests-" + Guid.NewGuid());
        try
        {
            var client = new FakeClient { Response = JayceToolText(), MainPosition = "adc" };
            var provider = new StatsProvider(client, new StatsCache(dir));

            var stats = await provider.GetAsync("Jinx", "", 11, "16.13.1");
            Assert.NotNull(stats);
            Assert.Equal(1, client.PositionCalls);
            Assert.Equal("adc", client.LastPosition);
            Assert.Equal("adc", stats!.Position);

            // Segunda consulta sin posición: cache hit vía alias, cero llamadas extra.
            var second = await provider.GetAsync("Jinx", null, 11, "16.13.1");
            Assert.NotNull(second);
            Assert.Equal(1, client.Calls);
            Assert.Equal(1, client.PositionCalls);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Unknown_position_with_failed_resolution_returns_null()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loladvisor-tests-" + Guid.NewGuid());
        try
        {
            var client = new FakeClient { Response = JayceToolText(), MainPosition = null };
            var provider = new StatsProvider(client, new StatsCache(dir));
            Assert.Null(await provider.GetAsync("Jinx", "", 11, "16.13.1"));
            Assert.Equal(0, client.Calls);   // sin rol no hay consulta de análisis válida
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
