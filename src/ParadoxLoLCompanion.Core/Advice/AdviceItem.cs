namespace ParadoxLoLCompanion.Core.Advice;

/// <summary>Qué tan urgente es un consejo (define también el color en la UI).</summary>
public enum AdviceSeverity
{
    Info,
    Warning,
    Important,
}

/// <summary>Categoría temática del consejo.</summary>
public enum AdviceCategory
{
    Gold,
    Farming,
    Draft,
    Items,
    General,
}

/// <summary>
/// Un consejo emitido por una regla. <see cref="Key"/> identifica el consejo de forma
/// estable para poder deduplicar entre reglas y entre ticks.
/// </summary>
public sealed record AdviceItem(
    AdviceCategory Category,
    AdviceSeverity Severity,
    string Key,
    string Message);
