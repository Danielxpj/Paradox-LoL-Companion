using System.Text;
using System.Text.Json;
using ParadoxLoLCompanion.Core.Items;
using ParadoxLoLCompanion.Core.Models;
using Xunit;

namespace ParadoxLoLCompanion.Tests;

/// <summary>
/// Vara de medición <b>agreement@3</b> sin corpus: escenarios ARAM etiquetados a mano en
/// <c>Fixtures/golden-scenarios.json</c> (qué item "debería" estar en el top-3). Una
/// recalibración se mide por cuántos escenarios siguen de acuerdo, no por si compila. El
/// umbral es deliberadamente &lt; 1: algunos escenarios son opinables; un desacuerdo aislado
/// es información, tres a la vez es una regresión. Los fallos se listan en el mensaje.
/// </summary>
public class GoldenScenarioTests
{
    private const double MinAgreement = 0.85;

    private sealed record Scenario(string Name, bool Aram, double Gold, string Me,
        int[]? MyItems, JsonElement[][]? Allies, JsonElement[][] Enemies,
        string[]? ExpectAny, string[]? ExpectNone);

    [Fact]
    public void AgreementAt3_OverGoldenScenarios()
    {
        var json = File.ReadAllText(Path.Combine("Fixtures", "golden-scenarios.json"));
        var scenarios = JsonSerializer.Deserialize<Scenario[]>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var advisor = new ItemAdvisor(TestCatalog.Catalog());
        var misses = new StringBuilder();
        var agreed = 0;

        foreach (var s in scenarios)
        {
            var players = new List<(string, string, int, int[])> { (s.Me, "ORDER", 0, s.MyItems ?? Array.Empty<int>()) };
            foreach (var a in s.Allies ?? Array.Empty<JsonElement[]>())
                players.Add((a[0].GetString()!, "ORDER", 0, Ids(a[1])));
            foreach (var e in s.Enemies)
                players.Add((e[0].GetString()!, "CHAOS", e[1].GetInt32(), Ids(e[2])));
            var state = s.Aram
                ? TestCatalog.AramState(s.Gold, players.ToArray())
                : TestCatalog.State(s.Gold, players.ToArray());

            var top = advisor.Advise(state)?.Recommendations.Select(r => r.Item.Name).ToList() ?? new();
            var ok = (s.ExpectAny is null || s.ExpectAny.Any(top.Contains))
                  && (s.ExpectNone is null || !s.ExpectNone.Any(top.Contains));
            if (ok)
                agreed++;
            else
                misses.AppendLine($"  ✗ {s.Name}: top-3 = [{string.Join(", ", top)}]"
                    + (s.ExpectAny is null ? "" : $" expected any of [{string.Join(", ", s.ExpectAny)}]")
                    + (s.ExpectNone is null ? "" : $" expected none of [{string.Join(", ", s.ExpectNone)}]"));
        }

        var agreement = (double)agreed / scenarios.Length;
        Assert.True(agreement >= MinAgreement,
            $"agreement@3 = {agreed}/{scenarios.Length} = {agreement:P0} (min {MinAgreement:P0})\n{misses}");
    }

    private static int[] Ids(JsonElement arr) => arr.EnumerateArray().Select(x => x.GetInt32()).ToArray();
}
