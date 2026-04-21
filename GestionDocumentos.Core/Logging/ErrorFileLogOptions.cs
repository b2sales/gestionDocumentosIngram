namespace GestionDocumentos.Core.Logging;

public sealed class ErrorFileLogOptions
{
    public bool Enabled { get; set; }

    /// <summary>Carpeta donde se escriben los archivos de log de error.</summary>
    public string FolderPath { get; set; } = "";

    /// <summary>
    /// Prefijo del archivo. El nombre final queda: {FileNamePrefix}-yyyy-MM-dd.log
    /// </summary>
    public string FileNamePrefix { get; set; } = "errors";

    /// <summary>
    /// Cantidad de días a retener. Si es 0 o negativo, no se elimina historial.
    /// </summary>
    public int RetentionDays { get; set; } = 30;
}
