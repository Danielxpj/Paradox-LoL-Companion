using ParadoxLoLCompanion.Core.Models;

namespace ParadoxLoLCompanion.Core.Advice;

/// <summary>
/// Una regla de consejo. Recibe el estado de la partida y devuelve cero o más consejos.
/// Cada regla es independiente y testeable por separado.
/// </summary>
public interface IAdviceRule
{
    IEnumerable<AdviceItem> Evaluate(GameState state);
}
