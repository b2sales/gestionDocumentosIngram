namespace GestionDocumentos.Idoc;

public sealed class IdocOptions
{
    /// <summary>Cadena hacia backOfficeDB (tabla <c>seguridad.parametros</c>). Vacía = no se consulta BD.</summary>
    public string BackOfficeConnectionString { get; set; } = "";

    /// <summary>Fallback cuando <see cref="BackOfficeConnectionString"/> está vacío (solo dev/local).</summary>
    public string WatchFolder { get; set; } = "";

    /// <summary>Fallback Tibco root; si vacío y sin BD, se usa <see cref="WatchFolder"/>.</summary>
    public string TibcoRoot { get; set; } = "";

    /// <summary>Cadena hacia la BD iDoc (inserciones en Documentos).</summary>
    public string ConnectionString { get; set; } = "";
    public int ProcessingConcurrency { get; set; } = 4;
    public int QueueCapacity { get; set; } = 2000;
    public int FileReadyRetries { get; set; } = 10;
    public int FileReadyDelayMs { get; set; } = 500;

    /// <summary>Buffer de <see cref="System.IO.FileSystemWatcher"/> (bytes).</summary>
    public int WatcherInternalBufferSize { get; set; } = 65536;
}
