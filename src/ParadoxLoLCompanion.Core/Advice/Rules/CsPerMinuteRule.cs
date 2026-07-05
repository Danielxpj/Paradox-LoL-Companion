using System.Globalization;
using ParadoxLoLCompanion.Core.Config;
using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Core.Advice.Rules;

/// <summary>
/// Compara el CS/min del jugador activo con una referencia (configurable) y avisa si va
/// por debajo. Solo se activa tras un mínimo de tiempo para evitar ruido al inicio.
/// </summary>
public sealed class CsPerMinuteRule : IAdviceRule
{
    private readonly CsConfig _config;

    public CsPerMinuteRule(CsConfig? config = null) => _config = config ?? new CsConfig();

    public IEnumerable<AdviceItem> Evaluate(GameState state)
    {
        // La referencia de CS/min solo tiene sentido en la Grieta (en ARAM el farmeo
        // es compartido y las peleas mandan).
        if (!string.Equals(state.GameData.GameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase))
            yield break;

        var minutes = state.GameData.GameTimeMinutes;
        var player = state.ActivePlayerEntry;
        if (player is null || minutes < _config.MinMinutes)
            yield break;

        var csPerMin = player.Scores.CreepScore / minutes;
        if (csPerMin < _config.Target)
        {
            var cur = csPerMin.ToString("0.0", CultureInfo.InvariantCulture);
            var tgt = _config.Target.ToString("0.0", CultureInfo.InvariantCulture);
            yield return new AdviceItem(
                AdviceCategory.Farming, AdviceSeverity.Warning, "cs-per-min",
                $"Your CS/min is {cur} (target ~{tgt}). You are missing farm.");
        }
    }
}
