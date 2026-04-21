namespace GestionDocumentos.Core.Options;

public sealed class WatcherOptions
{
    public required string Path { get; init; }
    public required string Filter { get; init; }
    public int QueueCapacity { get; init; } = 2000;
    public int WorkerCount { get; init; } = 4;
    public int FileReadyRetries { get; init; } = 10;
    public int FileReadyDelayMs { get; init; } = 500;
    public bool RequireExclusiveReadinessLock { get; init; }

    /// <summary>Tamaño del buffer interno de <see cref="FileSystemWatcher"/> (bytes). Mayor valor reduce pérdida de eventos bajo carga.</summary>
    public int InternalBufferSize { get; init; } = 65536;

    /// <summary>
    /// Carpeta destino para archivos que fallan repetidamente (DLQ / cuarentena).
    /// Si vacía, los archivos fallidos quedan en la carpeta origen y solo se registra en log.
    /// El engine crea subcarpeta <c>yyyy-MM-dd/</c> automáticamente.
    /// </summary>
    public string FailedFolder { get; init; } = "";

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
