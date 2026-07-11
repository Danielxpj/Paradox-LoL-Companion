namespace ParadoxLoLCompanion.Core.Items;

/// <summary>
/// Radiografía del equipo enemigo: qué daño hace (ponderado por lo "fed" que va cada
/// uno), cuánta curación/resistencias/burst/CC trae, y quiénes son las amenazas top
/// (para armar mensajes). Producido por <see cref="ThreatAnalyzer"/>.
/// </summary>
public sealed record TeamThreat
{
    public bool HasEnemies { get; init; }

    /// <summary>Fracción [0..1] de la amenaza total que es daño físico (Mixed cuenta mitad).</summary>
    public double PhysicalShare { get; init; }
    /// <summary>Fracción [0..1] de la amenaza total que es daño mágico.</summary>
    public double MagicalShare { get; init; }
    /// <summary>Fracción de la amenaza que viene de tiradores (autoataques → Acorazadas).</summary>
    public double AutoAttackShare { get; init; }

    /// <summary>El enemigo que más pesa en total, como "Jinx (7/1/3)".</summary>
    public string? TopThreatName { get; init; }
    public string? TopPhysicalName { get; init; }
    public string? TopMagicalName { get; init; }

    /// <summary>Fracción de la amenaza ponderada que trae curación/robo de vida.</summary>
    public double SustainScore { get; init; }
    public string? TopSustainName { get; init; }

    /// <summary>Armadura / RM totales compradas por el equipo enemigo (stats de sus items).</summary>
    public double EnemyBonusArmor { get; init; }
    public double EnemyBonusMr { get; init; }

    /// <summary>Peso del asesino más fed relativo al promedio del equipo (1 = promedio).</summary>
    public double BurstScore { get; init; }
    public DamageProfile BurstDamage { get; init; } = DamageProfile.Mixed;
    public string? TopBurstName { get; init; }

    public bool HasSuppression { get; init; }
    public string? SuppressionName { get; init; }
    public int HeavyCcCount { get; init; }
    public bool HasShields { get; init; }

    // --- Grados difusos (v3): perceptos continuos ∈ [0,1] que el scoring usa en lugar de
    // los umbrales duros. Los produce ThreatAnalyzer; el umbral de la v2 es el cruce μ≈0.5.

    /// <summary>Qué tan sesgado a daño físico va el enemigo (0 = parejo, 1 = casi todo físico).</summary>
    public double PhysicalSkew { get; init; }
    /// <summary>Qué tan sesgado a daño mágico va el enemigo.</summary>
    public double MagicalSkew { get; init; }
    /// <summary>Qué tan MIXTO es el daño enemigo (1 = 50/50): la vida cruda defiende ambos.</summary>
    public double MixedDamage { get; init; }
    /// <summary>Cuánto apila el enemigo la armadura que frena tu daño físico.</summary>
    public double ArmorStack { get; init; }
    /// <summary>Cuánto apila el enemigo la RM que frena tu daño mágico.</summary>
    public double MrStack { get; init; }
    /// <summary>Urgencia de anti-curación (relativa al umbral del mapa: más caliente en ARAM).</summary>
    public double Sustain { get; init; }
    /// <summary>Qué tan fed va el mejor asesino/amenaza de burst.</summary>
    public double Burst { get; init; }
    /// <summary>Riesgo de daño crítico (tiradores + items de crítico enemigos) → armadura anti-crit.</summary>
    public double CritThreat { get; init; }
    /// <summary>Daño que ignora resistencias (% de vida / verdadero): apilar un solo muro rinde menos.</summary>
    public double PercentHpTrue { get; init; }
    /// <summary>Enganche/pick duro que te obliga a items de supervivencia (GA/Zhonya/QSS).</summary>
    public double HardEngage { get; init; }
    /// <summary>El enemigo YA compró anti-curación contra vos → tu robo de vida vale menos.</summary>
    public double EnemyAntiHeal { get; init; }
    /// <summary>Cuánta vida+resistencias acumuló el enemigo → tu ofensiva quiere penetración/on-hit.</summary>
    public double EnemyTankiness { get; init; }
    /// <summary>Cuánto CC pesado apila el enemigo (wombo): tenacidad y limpieza suben para todos.</summary>
    public double CcThreat { get; init; }

    /// <summary>Peso promedio del equipo enemigo (escala relativa): base para ahead/behind.</summary>
    public double AvgEnemyWeight { get; init; }
    /// <summary>Peso del jugador activo en la MISMA escala relativa que los enemigos (comparable).</summary>
    public double MyWeight { get; init; }

    public static TeamThreat None { get; } = new();
}
