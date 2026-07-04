using System.Globalization;
using System.Text;

namespace LoLAdvisor.Core.Stats;

/// <summary>
/// Parser del formato compacto que devuelve el MCP de OP.GG: cabeceras
/// "class Nombre: campo1,campo2", una línea en blanco y una expresión de
/// constructores anidados. Las cabeceras definen el orden posicional de los
/// campos, así que el árbol resultante se consulta por NOMBRE de campo y es
/// robusto a reordenamientos y campos nuevos.
/// </summary>
public static class McpTextParser
{
    /// <summary><c>null</c> si el texto no tiene el formato esperado (nunca lanza).</summary>
    public static McpObject? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            var classes = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var body = new StringBuilder();
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.StartsWith("class ", StringComparison.Ordinal))
                {
                    var colon = trimmed.IndexOf(':');
                    if (colon < 0)
                        continue;
                    var name = trimmed[6..colon].Trim();
                    var fields = trimmed[(colon + 1)..].Split(',', StringSplitOptions.TrimEntries);
                    classes[name] = fields;
                }
                else
                {
                    body.Append(trimmed);
                }
            }

            var s = body.ToString();
            var pos = 0;
            var value = ParseValue(s, ref pos, classes);
            return value as McpObject;
        }
        catch
        {
            return null;
        }
    }

    private static object? ParseValue(string s, ref int i, Dictionary<string, string[]> classes)
    {
        SkipWs(s, ref i);
        if (i >= s.Length)
            return null;
        var c = s[i];
        if (c == '[')
            return ParseList(s, ref i, classes);
        if (c == '"')
            return ParseString(s, ref i);
        if (char.IsDigit(c) || c == '-' || c == '+')
            return ParseNumber(s, ref i);
        if (char.IsLetter(c) || c == '_')
            return ParseWordOrConstructor(s, ref i, classes);
        throw new FormatException($"unexpected '{c}' at {i}");
    }

    private static object? ParseWordOrConstructor(string s, ref int i, Dictionary<string, string[]> classes)
    {
        var start = i;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
            i++;
        var word = s[start..i];
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == '(')
        {
            i++; // '('
            var args = new List<object?>();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ')') { i++; }
            else
            {
                while (true)
                {
                    args.Add(ParseValue(s, ref i, classes));
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ')') { i++; break; }
                    throw new FormatException($"unterminated constructor at {i}");
                }
            }
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (classes.TryGetValue(word, out var names))
                for (var k = 0; k < names.Length; k++)
                    fields[names[k]] = k < args.Count ? args[k] : null;
            return new McpObject(word, fields);
        }
        return word switch
        {
            "None" or "null" => null,
            "True" or "true" => true,
            "False" or "false" => false,
            _ => word,
        };
    }

    private static List<object?> ParseList(string s, ref int i, Dictionary<string, string[]> classes)
    {
        i++; // '['
        var items = new List<object?>();
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return items; }
        while (true)
        {
            items.Add(ParseValue(s, ref i, classes));
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; break; }
            throw new FormatException($"unterminated list at {i}");
        }
        return items;
    }

    private static string ParseString(string s, ref int i)
    {
        i++; // '"'
        var sb = new StringBuilder();
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] });
            }
            else
            {
                sb.Append(s[i]);
            }
            i++;
        }
        i++; // '"' de cierre
        return sb.ToString();
    }

    private static double ParseNumber(string s, ref int i)
    {
        var start = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] is '.' or '-' or '+' or 'e' or 'E'))
            i++;
        return double.Parse(s[start..i], CultureInfo.InvariantCulture);
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
    }
}

/// <summary>Nodo del árbol parseado: acceso por nombre de campo con helpers tolerantes.</summary>
public sealed class McpObject
{
    public string ClassName { get; }
    private readonly Dictionary<string, object?> _fields;

    public McpObject(string className, Dictionary<string, object?> fields)
    {
        ClassName = className;
        _fields = fields;
    }

    private object? Raw(string field) => _fields.GetValueOrDefault(field);

    public McpObject? Obj(string field) => Raw(field) as McpObject;
    public double Num(string field) => Raw(field) is double d ? d : 0;
    public string Str(string field) => Raw(field) as string ?? "";

    public List<int> IntList(string field) =>
        Raw(field) is List<object?> list
            ? list.OfType<double>().Select(d => (int)d).ToList()
            : new List<int>();

    public List<string> StrList(string field) =>
        Raw(field) is List<object?> list
            ? list.OfType<string>().ToList()
            : new List<string>();

    public List<McpObject> ObjList(string field) =>
        Raw(field) is List<object?> list
            ? list.OfType<McpObject>().ToList()
            : new List<McpObject>();
}
