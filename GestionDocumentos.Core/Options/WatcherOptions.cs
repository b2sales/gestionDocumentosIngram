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
}
