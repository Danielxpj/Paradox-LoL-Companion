using System.Globalization;
using System.Text;

namespace ParadoxLoLCompanion.Core.Augments;

/// <summary>
/// Matchea líneas de OCR contra nombres de augments (con aliases localizados).
/// El OCR real nunca entrega el título limpio: viene incrustado en líneas con
/// timestamps, números y texto de HUD alrededor ("10:33 E: BONK!"). Por eso el
/// match es por VENTANA de tokens deslizante sobre la línea, con Levenshtein
/// acotado (≤ 1 + len/8) para los typos clásicos del OCR (l→i, o→0). Los nombres
/// cortos (&lt; 6 chars normalizados) solo matchean exactos: con fuzz colisionan
/// con cualquier cosa.
/// </summary>
public sealed class AugmentNameMatcher
{
    private readonly List<(string Joined, string[] Tokens, AugmentInfo Augment)> _entries = new();

    public AugmentNameMatcher(AugmentTierList list)
    {
        foreach (var augment in list.Augments)
            AddEntry(augment.Name, augment);
    }

    /// <summary>Nombre alternativo (p.ej. es_MX de cdragon) para un augment ya cargado.</summary>
    public void AddAlias(int augmentId, string alias)
    {
        var augment = _entries.FirstOrDefault(e => e.Augment.Id == augmentId).Augment;
        if (augment is not null)
            AddEntry(alias, augment);
    }

    private void AddEntry(string name, AugmentInfo augment)
    {
        var tokens = Tokenize(name);
        var joined = string.Concat(tokens);
        if (joined.Length >= 4)
            _entries.Add((joined, tokens, augment));
    }

    /// <summary>El augment cuyo nombre mejor matchea (dentro de) la línea, o null.</summary>
    public AugmentInfo? Match(string ocrLine)
    {
        var lineTokens = Tokenize(ocrLine);
        if (lineTokens.Length == 0)
            return null;

        AugmentInfo? best = null;
        var bestDist = int.MaxValue;
        foreach (var (joined, tokens, augment) in _entries)
        {
            var dist = WindowedDistance(lineTokens, joined, tokens.Length);
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

    /// <summary>
    /// Mejor distancia del nombre contra cada ventana contigua de tokens de la
    /// línea (tamaño k-1..k+1: el OCR parte y une palabras). -1 si ninguna entra
    /// en presupuesto. Nombres cortos: solo ventana exacta.
    /// </summary>
    private static int WindowedDistance(string[] lineTokens, string joined, int nameTokenCount)
    {
        var budget = joined.Length < 6 ? 0 : 1 + joined.Length / 8;
        var bestDist = -1;
        for (var size = Math.Max(1, nameTokenCount - 1);
             size <= Math.Min(lineTokens.Length, nameTokenCount + 1); size++)
        {
            for (var start = 0; start + size <= lineTokens.Length; start++)
            {
                var window = string.Concat(lineTokens.Skip(start).Take(size));
                if (Math.Abs(window.Length - joined.Length) > budget)
                    continue;
                var dist = BoundedLevenshtein(window, joined, budget);
                if (dist >= 0 && (bestDist < 0 || dist < bestDist))
                {
                    bestDist = dist;
                    if (dist == 0)
                        return 0;
                }
            }
        }
        return bestDist;
    }

    /// <summary>Tokens en minúsculas, sin diacríticos, solo alfanuméricos.</summary>
    internal static string[] Tokenize(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length > 0)
            tokens.Add(sb.ToString());
        return tokens.ToArray();
    }

    /// <summary>Compat: normalización a un solo string (usada por tests y aliases).</summary>
    internal static string Normalize(string text) => string.Concat(Tokenize(text));

    /// <summary>Distancia de edición, o -1 si supera el presupuesto (early-out por fila).</summary>
    internal static int BoundedLevenshtein(string a, string b, int budget)
    {
        if (budget == 0)
            return a == b ? 0 : -1;
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
