namespace GestionDocumentos.Idoc;

/// <summary>
/// Rutas efectivas de IDOC tras resolver <c>IDOC/I2CARPETA</c> desde backOfficeDB (o fallback JSON en dev).
/// </summary>
public sealed class IdocBackOfficePaths
{
    public string WatchFolder { get; private set; } = "";

    /// <summary>Prefijo para <see cref="IdocFileProcessor"/> (legacy Tibco root).</summary>
    public string TibcoRoot { get; private set; } = "";

    public bool ResolvedFromDatabase { get; private set; }

    public void Apply(string watchFolder, string? tibcoRoot, bool resolvedFromDatabase)
    {
        WatchFolder = NormalizeFolder(watchFolder);
        TibcoRoot = string.IsNullOrWhiteSpace(tibcoRoot)
            ? WatchFolder
            : NormalizeFolder(tibcoRoot);
        ResolvedFromDatabase = resolvedFromDatabase;
    }

    /// <summary>Misma lógica relativa que <see cref="IdocFileProcessor"/> para <c>NameFile</c> en BD.</summary>
    public string ToArchivoTibcoRelative(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(TibcoRoot))
        {
            return fullPath;
        }

        if (fullPath.StartsWith(TibcoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath[TibcoRoot.Length..];
        }

        return fullPath;
    }

    private static string NormalizeFolder(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
