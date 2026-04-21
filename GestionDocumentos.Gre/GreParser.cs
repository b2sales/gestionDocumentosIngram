namespace GestionDocumentos.Gre;

/// <summary>
/// Parses GRE companion .txt content (pipe / semicolon layout) produced for PDF guías.
/// </summary>
/// <remarks>
/// <para>
/// Formato por línea: <c>Section;Key;Index;Value</c> (4 columnas separadas por <c>;</c>).
/// La <c>Value</c> puede contener separadores <c>;</c> adicionales; tomamos el contenido desde
/// la cuarta columna hasta el final.
/// </para>
/// <para>
/// La versión anterior usaba <c>Contains</c> + <c>First</c>, lo que producía falsos positivos
/// cuando dos claves compartían prefijo (p. ej. <c>RUCTranspor</c> y <c>RUCTransporExtra</c>).
/// La nueva implementación construye un <see cref="Dictionary{TKey,TValue}"/> de clave exacta
/// durante la construcción (O(n)) y hace lookups O(1).
/// </para>
/// </remarks>
public sealed class GreParseResult
{
    private readonly Dictionary<string, string> _attributes;
    private readonly List<string> _descFields;

    public GreParseResult(string[]? lines)
    {
        Lines = lines ?? Array.Empty<string>();
        _attributes = BuildAttributeIndex(Lines);
        _descFields = BuildDescFields(Lines);
    }

    public IReadOnlyList<string> Lines { get; }

    /// <summary>Campos del registro <c>DescripcionAdicsunat</c> separados por <c>|</c>, ya trimmeados.</summary>
    public IReadOnlyList<string> Desc => _descFields;

    public string Errors { get; private set; } = "";

    public string GetAttributes(string nombreAtributo)
    {
        if (_attributes.TryGetValue(nombreAtributo, out var value))
        {
            return value;
        }

        Errors += $"{nombreAtributo}:Attribute not found  ";
        return "";
    }

    public string GetValue(string prefix)
    {
        if (_descFields.Count == 0)
        {
            Errors += $"{prefix}:Description not found  ";
            return "";
        }

        var needle = prefix + " ";
        foreach (var field in _descFields)
        {
            if (field.StartsWith(needle, StringComparison.Ordinal))
            {
                return field.Substring(needle.Length);
            }
        }

        Errors += $"{prefix}:Description not found  ";
        return "";
    }

    private static Dictionary<string, string> BuildAttributeIndex(IReadOnlyList<string> lines)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            // Esperamos 4 columnas. Si falta la segunda (clave), saltamos.
            var parts = line.Split(';', 4);
            if (parts.Length < 2)
            {
                continue;
            }

            var key = parts[1];
            if (string.IsNullOrEmpty(key) || dict.ContainsKey(key))
            {
                // Primera ocurrencia gana (comportamiento histórico con .First()).
                continue;
            }

            var value = parts.Length >= 4 ? parts[3] : "";
            dict[key] = value.Trim();
        }

        return dict;
    }

    private static List<string> BuildDescFields(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.Contains("DescripcionAdicsunat", StringComparison.Ordinal))
            {
                return line
                    .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
            }
        }

        return [];
    }
}

public static class GreParser
{
    public static GreParseResult ParseLines(string[] lines) => new(lines);
}
