using System.Collections.Concurrent;
using System.Threading.Channels;
using GestionDocumentos.Core.Abstractions;
using GestionDocumentos.Core.Options;
using Microsoft.Extensions.Logging;

namespace GestionDocumentos.Core.Services;

public sealed class FolderWatcherEngine : IAsyncDisposable
{
    private readonly IFileProcessor _processor;
    private readonly WatcherOptions _options;
    private readonly ILogger<FolderWatcherEngine> _logger;
    private readonly Channel<string> _queue;
    private readonly ConcurrentDictionary<string, byte> _pending;
    private readonly ConcurrentDictionary<string, int> _failureCounts;
    private readonly List<Task> _workers = [];
    private readonly SemaphoreSlim _watcherRecoveryLock = new(1, 1);
    private FileSystemWatcher? _watcher;
    private CancellationToken _runCancellationToken;
    private volatile bool _stopping;
    private int _recoveryInProgress;
    private long _lastProcessedTicksUtc;

    public FolderWatcherEngine(IFileProcessor processor, WatcherOptions options, ILogger<FolderWatcherEngine> logger)
    {
        _processor = processor;
        _options = options;
        _logger = logger;
        _pending = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        _failureCounts = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _runCancellationToken = cancellationToken;
        _stopping = false;
        if (!Directory.Exists(_options.Path))
        {
            throw new DirectoryNotFoundException($"Watch directory not found: {_options.Path}");
        }

        _watcher = CreateWatcher();

        for (var i = 0; i < _options.WorkerCount; i++)
        {
            _workers.Add(Task.Run(() => ConsumeLoopAsync(cancellationToken), cancellationToken));
        }

        // Reescaneo inicial al arranque para cubrir archivos existentes
        // (incluyendo ventanas en las que se pudo perder un evento del FSW).
        SafeFireAndForget(EnqueueExistingFilesAsync, nameof(EnqueueExistingFilesAsync));

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        DisposeWatcher();

        _queue.Writer.TryComplete();
        if (_workers.Count > 0)
        {
            try
            {
                await Task.WhenAll(_workers).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "FolderWatcherEngine: timeout/cancelación esperando workers en StopAsync para {Path}.",
                    _options.Path);
            }
        }
    }

    private void WatcherOnError(object? sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher error (buffer interno u OS) en {Path}", _options.Path);
        if (_stopping || Interlocked.Exchange(ref _recoveryInProgress, 1) == 1)
        {
            return;
        }

        SafeFireAndForget(RecoverWatcherAsync, nameof(RecoverWatcherAsync));
    }

    private void WatcherOnEvent(object? sender, FileSystemEventArgs e)
    {
        if (e.ChangeType is not (WatcherChangeTypes.Created or WatcherChangeTypes.Changed))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(e.FullPath))
        {
            return;
        }

        var fullPath = e.FullPath;
        SafeFireAndForget(() => EnqueuePathAsync(fullPath), nameof(EnqueuePathAsync));
    }

    /// <summary>
    /// Ejecuta una Task en segundo plano garantizando que ninguna excepción llegue al
    /// <see cref="TaskScheduler.UnobservedTaskException"/> (que podría matar el proceso según
    /// configuración). Todas las excepciones se loggean y se absorben.
    /// </summary>
    private void SafeFireAndForget(Func<Task> taskFactory, string operationName)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await taskFactory();
            }
            catch (OperationCanceledException) when (_stopping || _runCancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fire-and-forget falló en {Operation} (watcher {Path})", operationName, _options.Path);
            }
        });
    }

    private async Task EnqueuePathAsync(string fullPath)
    {
        if (!_pending.TryAdd(fullPath, 0))
        {
            return;
        }

        try
        {
            await _queue.Writer.WriteAsync(fullPath, _runCancellationToken);
        }
        catch (OperationCanceledException) when (_runCancellationToken.IsCancellationRequested || _stopping)
        {
            _pending.TryRemove(fullPath, out _);
        }
        catch (ChannelClosedException)
        {
            _pending.TryRemove(fullPath, out _);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(fullPath, out _);
            _logger.LogError(ex, "No se pudo encolar archivo {Path}", fullPath);
        }
    }

    private async Task ConsumeLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_queue.Reader.TryRead(out var fullPath))
                {
                    var success = false;
                    try
                    {
                        if (await WaitUntilReadyAsync(fullPath, cancellationToken))
                        {
                            await _processor.ProcessAsync(fullPath, cancellationToken);
                            success = true;
                            Interlocked.Exchange(ref _lastProcessedTicksUtc, DateTime.UtcNow.Ticks);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Archivo no disponible tras {Retries} reintentos ({DelayMs}ms): {Path}",
                                _options.FileReadyRetries,
                                _options.FileReadyDelayMs,
                                fullPath);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        _pending.TryRemove(fullPath, out _);
                        return;
                    }
                    catch (Exception ex)
                    {
                        HandleProcessingFailure(fullPath, ex);
                    }
                    finally
                    {
                        _pending.TryRemove(fullPath, out _);
                        if (success)
                        {
                            _failureCounts.TryRemove(fullPath, out _);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "FolderWatcherEngine: worker loop terminó por excepción inesperada en {Path}.", _options.Path);
        }
    }

    private void HandleProcessingFailure(string fullPath, Exception ex)
    {
        var maxAttempts = Math.Max(1, _options.MaxProcessAttempts);
        var attempts = _failureCounts.AddOrUpdate(fullPath, 1, (_, prev) => prev + 1);

        if (attempts >= maxAttempts)
        {
            _logger.LogError(
                ex,
                "Archivo falló {Attempts}/{Max} veces: {Path}. Moviendo a cuarentena.",
                attempts,
                maxAttempts,
                fullPath);
            TryQuarantine(fullPath, ex);
            _failureCounts.TryRemove(fullPath, out _);
        }
        else
        {
            _logger.LogWarning(
                ex,
                "Archivo falló {Attempts}/{Max}; se reintentará en próximo evento: {Path}",
                attempts,
                maxAttempts,
                fullPath);
        }
    }

    private void TryQuarantine(string sourcePath, Exception ex)
    {
        if (string.IsNullOrWhiteSpace(_options.FailedFolder))
        {
            return;
        }

        try
        {
            var dayFolder = Path.Combine(_options.FailedFolder, DateTime.Now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dayFolder);

            var baseName = Path.GetFileName(sourcePath);
            if (string.IsNullOrEmpty(baseName))
            {
                return;
            }

            var destPath = BuildUniquePath(Path.Combine(dayFolder, baseName));
            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, destPath);
            }

            var logPath = destPath + ".log";
            File.WriteAllText(
                logPath,
                $"Fecha: {DateTime.Now:O}\r\nArchivo origen: {sourcePath}\r\n\r\n--- Excepción ---\r\n{ex}\r\n");
        }
        catch (Exception moveEx)
        {
            _logger.LogError(
                moveEx,
                "No se pudo mover a cuarentena {Source} → {FailedFolder}. El archivo queda en origen.",
                sourcePath,
                _options.FailedFolder);
        }
    }

    private static string BuildUniquePath(string desired)
    {
        if (!File.Exists(desired))
        {
            return desired;
        }

        var dir = Path.GetDirectoryName(desired) ?? "";
        var name = Path.GetFileNameWithoutExtension(desired);
        var ext = Path.GetExtension(desired);
        var stamp = DateTime.Now.ToString("HHmmssfff");
        return Path.Combine(dir, $"{name}.{stamp}{ext}");
    }

    private async Task<bool> WaitUntilReadyAsync(string fullPath, CancellationToken cancellationToken)
    {
        for (var i = 0; i < _options.FileReadyRetries; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(fullPath))
                {
                    var share = _options.RequireExclusiveReadinessLock ? FileShare.None : FileShare.ReadWrite;
                    using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, share);
                    if (stream.Length > 0)
                    {
                        return true;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(_options.FileReadyDelayMs, cancellationToken);
        }

        return false;
    }

    private FileSystemWatcher CreateWatcher()
    {
        var watcher = new FileSystemWatcher(_options.Path)
        {
            NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.LastWrite,
            Filter = _options.Filter,
            IncludeSubdirectories = false,
            InternalBufferSize = _options.InternalBufferSize,
            EnableRaisingEvents = false
        };
        watcher.Created += WatcherOnEvent;
        watcher.Changed += WatcherOnEvent;
        watcher.Error += WatcherOnError;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void DisposeWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= WatcherOnEvent;
        _watcher.Changed -= WatcherOnEvent;
        _watcher.Error -= WatcherOnError;
        _watcher.Dispose();
        _watcher = null;
    }

    private async Task RecoverWatcherAsync()
    {
        try
        {
            await _watcherRecoveryLock.WaitAsync(_runCancellationToken);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Exchange(ref _recoveryInProgress, 0);
            return;
        }

        try
        {
            var attempt = 0;
            while (!_stopping)
            {
                attempt++;
                try
                {
                    if (!Directory.Exists(_options.Path))
                    {
                        throw new DirectoryNotFoundException($"Watch directory not found during recovery: {_options.Path}");
                    }

                    DisposeWatcher();
                    _watcher = CreateWatcher();

                    var enqueued = await EnqueueExistingFilesAsync();
                    _logger.LogWarning(
                        "Watcher recuperado para {Path} en intento {Attempt}. Reescaneo inicial encoló {Count} archivo(s).",
                        _options.Path,
                        attempt,
                        enqueued);
                    return;
                }
                catch (OperationCanceledException) when (_runCancellationToken.IsCancellationRequested || _stopping)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Falló recuperación del watcher para {Path} en intento {Attempt}. Reintentando en 30s.",
                        _options.Path,
                        attempt);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), _runCancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            // CRÍTICO: siempre reseteamos, aunque la recuperación fallara catastróficamente,
            // para que un siguiente Error event pueda disparar una nueva recuperación.
            Interlocked.Exchange(ref _recoveryInProgress, 0);
            _watcherRecoveryLock.Release();
        }
    }

    private async Task<int> EnqueueExistingFilesAsync()
    {
        var count = 0;
        var skippedOld = 0;
        var skippedByLimit = 0;
        var maxFiles = _options.RescanMaxFiles;
        var maxAge = _options.RescanMaxAge;
        var ageCutoffUtc = maxAge > TimeSpan.Zero ? DateTime.UtcNow - maxAge : (DateTime?)null;

        foreach (var path in Directory.EnumerateFiles(_options.Path, _options.Filter, SearchOption.TopDirectoryOnly))
        {
            if (maxFiles > 0 && count >= maxFiles)
            {
                skippedByLimit++;
                continue;
            }

            if (ageCutoffUtc is { } cutoff)
            {
                try
                {
                    var last = File.GetLastWriteTimeUtc(path);
                    if (last < cutoff)
                    {
                        skippedOld++;
                        continue;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            count++;
            await EnqueuePathAsync(path);
        }

        if (skippedOld > 0 || skippedByLimit > 0)
        {
            _logger.LogWarning(
                "Reescaneo en {Path}: encolados {Count}, descartados {Old} por antigüedad (>{Age}), {Limit} por tope {Max}.",
                _options.Path,
                count,
                skippedOld,
                maxAge,
                skippedByLimit,
                maxFiles);
        }

        return count;
    }

    public WatcherSnapshot GetSnapshot()
    {
        var ticks = Interlocked.Read(ref _lastProcessedTicksUtc);
        DateTimeOffset? lastProcessed = ticks > 0
            ? new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc))
            : null;

        return new WatcherSnapshot(
            IsActive: !_stopping && _watcher is { EnableRaisingEvents: true },
            PendingCount: _pending.Count,
            LastProcessedAtUtc: lastProcessed,
            Path: _options.Path);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }
}
