namespace GestionDocumentos.Core.Options;

/// <summary>
/// Estrategia para decidir si un archivo terminó de escribirse y puede procesarse.
/// </summary>
public enum FileReadinessMode
{
    /// <summary>Abre el archivo permitiendo otros lectores. Detecta escritores activos.</summary>
    SharedReadProbe,

    /// <summary>Abre el archivo permitiendo lectores y escritores.</summary>
    ReadWriteProbe,

    /// <summary>
    /// No abre el archivo: exige tamaño y fecha de modificación estables entre dos muestras.
    /// Único modo que no puede bloquear a otros procesos sobre la carpeta vigilada.
    /// </summary>
    StableMetadata
}

public sealed class WatcherOptions
{
    public required string Path { get; init; }
    public required string Filter { get; init; }
    public int QueueCapacity { get; init; } = 2000;
    public int WorkerCount { get; init; } = 4;
    public int FileReadyRetries { get; init; } = 10;
    public int FileReadyDelayMs { get; init; } = 500;

    /// <summary>
    /// Estrategia de comprobación de "archivo listo". Usar
    /// <see cref="FileReadinessMode.StableMetadata"/> en carpetas compartidas con otros
    /// servicios: es la única que no abre el archivo y por tanto nunca los bloquea.
    /// </summary>
    public FileReadinessMode ReadinessMode { get; init; } = FileReadinessMode.SharedReadProbe;

    /// <summary>
    /// Antigüedad mínima (por <see cref="File.GetLastWriteTimeUtc"/>) que debe tener un archivo
    /// antes de procesarse. <see cref="TimeSpan.Zero"/> desactiva la espera. Los archivos más
    /// nuevos se reencolan automáticamente al cumplirse el plazo, sin ocupar workers.
    /// </summary>
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.Zero;

    /// <summary>Tamaño del buffer interno de <see cref="FileSystemWatcher"/> (bytes). Mayor valor reduce pérdida de eventos bajo carga.</summary>
    public int InternalBufferSize { get; init; } = 65536;

    /// <summary>
    /// Carpeta destino para archivos que fallan repetidamente (DLQ / cuarentena).
    /// Si vacía, los archivos fallidos quedan en la carpeta origen y solo se registra en log.
    /// El engine crea subcarpeta <c>yyyy-MM-dd/</c> automáticamente.
    /// </summary>
    public string FailedFolder { get; init; } = "";

    /// <summary>
    /// Si es <c>true</c> (por defecto), al llegar a cuarentena se mueve el archivo a
    /// <see cref="FailedFolder"/>. Si es <c>false</c>, el archivo permanece en origen y
    /// solo se escribe el <c>.log</c> de diagnóstico en la carpeta de fallidos.
    /// </summary>
    public bool MoveFailedFiles { get; init; } = true;

    /// <summary>
    /// Número máximo de intentos de procesamiento por archivo antes de mover a <see cref="FailedFolder"/>.
    /// Mínimo efectivo: 1 (sin reintentos).
    /// </summary>
    public int MaxProcessAttempts { get; init; } = 3;

    /// <summary>
    /// Tope superior de archivos a reencolar cuando el watcher se recupera de un error.
    /// Evita que tras <c>WatcherOnError</c> se dispare un reescaneo masivo sobre carpetas con miles
    /// de archivos viejos. Valor 0 o negativo desactiva el tope.
    /// </summary>
    public int RescanMaxFiles { get; init; } = 5000;

    /// <summary>
    /// Antigüedad máxima (por <see cref="File.GetLastWriteTimeUtc"/>) para que un archivo sea
    /// reencolado durante la recuperación del watcher. <see cref="TimeSpan.Zero"/> desactiva el filtro.
    /// </summary>
    public TimeSpan RescanMaxAge { get; init; } = TimeSpan.FromDays(7);
}
