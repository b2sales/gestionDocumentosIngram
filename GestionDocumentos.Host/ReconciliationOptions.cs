namespace GestionDocumentos.Host;

public sealed class ReconciliationOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Hora local diaria para ejecutar la conciliación (24 h, formato <c>HH:mm</c>).</summary>
    public string DailyTimeLocal { get; set; } = "02:00";

    /// <summary>Si es true, solo archivos con fecha de creación o última escritura del día actual (hora local).</summary>
    public bool OnlyTodaysFiles { get; set; }

    public bool GreEnabled { get; set; } = true;

    public bool IdocEnabled { get; set; } = true;

    /// <summary>Grado de paralelismo al invocar procesadores durante la conciliación (mínimo 1).</summary>
    public int MaxConcurrent { get; set; } = 2;

    /// <summary>
    /// Límite de archivos por origen y por corrida para evitar impactos de performance en carpetas masivas.
    /// </summary>
    public int MaxFilesPerSource { get; set; } = 10000;

    /// <summary>
    /// Si es true, antes de procesar se consulta la BD por lotes y se omiten archivos ya registrados (GRE: <c>greName</c>, IDOC: <c>NameFile</c>).
    /// </summary>
    public bool SkipAlreadyInDatabase { get; set; } = true;
}
