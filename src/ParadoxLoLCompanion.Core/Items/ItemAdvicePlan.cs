using ParadoxLoLCompanion.Core.DataDragon;

namespace ParadoxLoLCompanion.Core.Items;

/// <summary>
/// Por qué un item está donde está en el orden: da la coherencia de la priorización.
/// <c>Core</c> = pega con tu arquetipo; <c>Counter</c> = contrarresta una amenaza
/// ofensiva (penetración/anti-heal/anti-crit); <c>Defense</c> = te mantiene vivo
/// (resistencia/supervivencia); <c>Spike</c> = power spike que ya te alcanza.
/// </summary>
public enum RecommendationCategory
{
    Core,
    Counter,
    Defense,
    Spike,
}

/// <summary>Un item recomendado, con su puntaje, las razones y el plan de compra.</summary>
/// <param name="MissingGold">Oro que falta juntar para terminarlo (0 si ya alcanza).</param>
public sealed record ItemRecommendation(
    StaticItem Item,
    double Score,
    IReadOnlyList<string> Reasons,
    PurchasePlan Purchase,
    int MissingGold)
{
    /// <summary>Fuerza relativa de la recomendación: 1.0 = la principal; el resto, su fracción.</summary>
    public double Priority { get; init; } = 1.0;
    /// <summary>Por qué está en este lugar del orden (coherencia de la priorización).</summary>
    public RecommendationCategory Category { get; init; } = RecommendationCategory.Core;

    /// <summary>El oro actual alcanza para terminarlo ya.</summary>
    public bool Affordable => Purchase.CanFinishNow;
    /// <summary>Oro que falta gastar para terminarlo (0 si alcanza).</summary>
    public int RemainingCost => Purchase.RemainingCost;
    /// <summary>
    /// Inventario lleno y comprar este item no consume ningún componente que ya
    /// tengas: literalmente no hay slot donde ponerlo — hay que vender antes.
    /// </summary>
    public bool BlockedByFullInventory { get; init; }
}

/// <summary>Consejo de botas (solo cuando aún no hay botas terminadas), con su plan de compra.</summary>
/// <param name="MissingGold">Oro que falta juntar para terminarlas (0 si ya alcanza).</param>
public sealed record BootsAdvice(StaticItem Boots, string Reason, PurchasePlan Purchase, int MissingGold);

/// <summary>Sugerencia de venta: un item ya comprado que dejó de ser coherente con la build.</summary>
public sealed record SellSuggestion(StaticItem Item, int SellGold, string Reason);

/// <summary>Compra inicial sugerida (solo al arranque de ARAM, con el inventario vacío).</summary>
public sealed record StarterAdvice(StaticItem Item, string Reason);

/// <summary>
/// Pieza urgente a comprar YA (la de ~800 de Heridas Graves) cuando el enemigo cura mucho
/// y ningún item completo de GW llegó al top: el 100% del efecto vive en la pieza barata.
/// </summary>
public sealed record UrgentPickup(StaticItem Item, string Reason);

/// <summary>
/// El plan completo que produce <see cref="ItemAdvisor"/> en cada tick: la radiografía
/// del enemigo, el top de items recomendados (con razones), las botas sugeridas, los
/// items que conviene vender, la compra inicial y el aviso de tienda abierta (ARAM).
/// </summary>
public sealed record ItemAdvicePlan(
    string ThreatSummary,
    ChampionProfile MyProfile,
    TeamThreat Threat,
    IReadOnlyList<ItemRecommendation> Recommendations,
    BootsAdvice? Boots,
    IReadOnlyList<SellSuggestion> Sells,
    StarterAdvice? Starter,
    string? ShopAlert)
{
    /// <summary>La recomendación principal (la de mayor puntaje), si hay alguna.</summary>
    public ItemRecommendation? Top => Recommendations.Count > 0 ? Recommendations[0] : null;

    /// <summary>Los 6 slots de items están ocupados: comprar exige vender o fusionar componentes.</summary>
    public bool InventoryFull { get; init; }

    /// <summary>Consejos de late game (elixir con build completa, Control Ward en la Grieta).</summary>
    public IReadOnlyList<string> LateTips { get; init; } = Array.Empty<string>();

    /// <summary>Pieza urgente de Heridas Graves a comprar ya (o <c>null</c> si no aplica).</summary>
    public UrgentPickup? UrgentPickup { get; init; }
}
