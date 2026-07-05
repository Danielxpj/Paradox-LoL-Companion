using System.Globalization;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Core.Advice.Rules;

/// <summary>
/// Consejo por hitos de oro. La v1 no tiene la tienda/builds, así que usa umbrales simples
/// (configurables): avisa cuando tienes oro suficiente para considerar un recall y comprar.
/// </summary>
public sealed class GoldForItemRule : IAdviceRule
{
    private readonly GoldConfig _config;

    public GoldForItemRule(GoldConfig? config = null) => _config = config ?? new GoldConfig();

    public IEnumerable<AdviceItem> Evaluate(GameState state)
    {
        var gold = state.ActivePlayer?.CurrentGold ?? 0;
        var g = ((int)gold).ToString("N0", CultureInfo.InvariantCulture);

        // En ARAM no hay recall: el oro se gasta al reaparecer.
        var isAram = string.Equals(state.GameData.GameMode, "ARAM", StringComparison.OrdinalIgnoreCase)
            || state.GameData.MapNumber == 12;

        if (gold >= _config.LegendaryGold)
        {
            yield return new AdviceItem(
                AdviceCategory.Gold, AdviceSeverity.Important, "gold-milestone",
                isAram
                    ? $"You have {g} gold: enough for a completed item on your next death."
                    : $"You have {g} gold: enough for a completed item. Consider backing.");
        }
        else if (gold >= _config.ComponentGold)
        {
            yield return new AdviceItem(
                AdviceCategory.Gold, AdviceSeverity.Info, "gold-milestone",
                isAram
                    ? $"You have {g} gold: enough for a component or boots when you respawn."
                    : $"You have {g} gold: enough for a component or boots. Think about recalling.");
        }
    }
}
