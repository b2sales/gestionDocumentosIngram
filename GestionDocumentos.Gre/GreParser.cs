namespace GestionDocumentos.Gre;

/// <summary>
/// Parses GRE companion .txt content (pipe / semicolon layout) produced for PDF guías.
/// </summary>
public sealed class GreParseResult
{
    public GreParseResult(string[]? lines)
    {
        Lines = lines ?? Array.Empty<string>();
        if (Lines.Any(line => line.Contains("DescripcionAdicsunat", StringComparison.Ordinal)))
        {
            Desc = Lines
                .First(line => line.Contains("DescripcionAdicsunat", StringComparison.Ordinal))
                .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToList();
        }
    }

    public IReadOnlyList<string> Lines { get; }
    public List<string>? Desc { get; }
    public string Errors { get; private set; } = "";

    public string GetAttributes(string nombreAtributo)
    {
        if (!Lines.Any(line => line.Contains(nombreAtributo, StringComparison.Ordinal)))
        {
            Errors += $"{nombreAtributo}:Attribute not found  ";
            return "";
        }

        var linea = Lines.First(line => line.Contains(nombreAtributo, StringComparison.Ordinal));
        return linea.Split(';').Last().Trim();
    }

    public string GetValue(string val)
    {
        if (Desc is null || !Desc.Any(d => d.Contains($"{val} ", StringComparison.Ordinal)))
        {
            Errors += $"{val}:Description not found  ";
            return "";
        }

        return Desc.First(d => d.Contains($"{val} ", StringComparison.Ordinal)).Replace($"{val} ", "", StringComparison.Ordinal);
    }
}

public static class GreParser
{
    public static GreParseResult ParseLines(string[] lines) => new(lines);
}
