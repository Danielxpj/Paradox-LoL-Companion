using System.Globalization;
using System.Text;

namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>
/// Matchea líneas de OCR contra nombres de augments (con aliases localizados).
/// Tolerancia: Levenshtein ≤ 1 + len/8 sobre texto normalizado — el OCR
/// confunde l/i/1 y o/0 pero no inventa palabras enteras.
/// </summary>
public sealed class AugmentNameMatcher
{
    private readonly List<(string Normalized, AugmentInfo Augment)> _entries = new();

    public AugmentNameMatcher(AugmentTierList list)
    {
        foreach (var augment in list.Augments)
            if (augment.Name.Length > 0)
                _entries.Add((Normalize(augment.Name), augment));
    }

    /// <summary>Nombre alternativo (p.ej. es_MX de cdragon) para un augment ya cargado.</summary>
    public void AddAlias(int augmentId, string alias)
    {
        var augment = _entries.FirstOrDefault(e => e.Augment.Id == augmentId).Augment;
        var normalized = Normalize(alias);
        if (augment is not null && normalized.Length >= 4)
            _entries.Add((normalized, augment));
    }

    /// <summary>El augment cuyo nombre mejor matchea la línea, o null.</summary>
    public AugmentInfo? Match(string ocrLine)
    {
        var line = Normalize(ocrLine);
        if (line.Length < 4)
            return null;   // "HP", "12s"…: debajo de 4 todo colisiona
        AugmentInfo? best = null;
        var bestDist = int.MaxValue;
        foreach (var (name, augment) in _entries)
        {
            var budget = 1 + name.Length / 8;
            if (Math.Abs(line.Length - name.Length) > budget)
                continue;   // ni borrando/insertando el presupuesto entero alcanza
            var dist = BoundedLevenshtein(line, name, budget);
            if (dist >= 0 && dist < bestDist)
            {
                bestDist = dist;
                best = augment;
                if (dist == 0)
                    break;
            }
        }
        return best;
    }

    /// <summary>Minúsculas, sin diacríticos y solo alfanuméricos (el OCR pierde puntuación).</summary>
    internal static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>Distancia de edición, o -1 si supera el presupuesto (early-out por fila).</summary>
    internal static int BoundedLevenshtein(string a, string b, int budget)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, curr[j]);
            }
            if (rowMin > budget)
                return -1;
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length] <= budget ? prev[b.Length] : -1;
    }
}
