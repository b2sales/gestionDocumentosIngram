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
    private readonly List<Task> _workers = [];
    private readonly SemaphoreSlim _watcherRecoveryLock = new(1, 1);
    private FileSystemWatcher? _watcher;
    private CancellationToken _runCancellationToken;
    private volatile bool _stopping;
    private int _recoveryInProgress;

    public FolderWatcherEngine(IFileProcessor processor, WatcherOptions options, ILogger<FolderWatcherEngine> logger)
    {
        _processor = processor;
        _options = options;
        _logger = logger;
        _pending = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
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

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        DisposeWatcher();

        _queue.Writer.TryComplete();
        if (_workers.Count > 0)
        {
            await Task.WhenAll(_workers).WaitAsync(cancellationToken);
        }
    }

    private void WatcherOnError(object? sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher error (buffer interno u OS) en {Path}", _options.Path);
        if (_stopping || Interlocked.Exchange(ref _recoveryInProgress, 1) == 1)
        {
            return;
        }

        _ = RecoverWatcherAsync();
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

        _ = EnqueuePathAsync(e.FullPath);
    }

    private async Task EnqueuePathAsync(string fullPath)
    {
        if (!_pending.TryAdd(fullPath, 0))
        {
            return;
        }

        try
        {
            await _queue.Writer.WriteAsync(fullPath);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(fullPath, out _);
            _logger.LogError(ex, "No se pudo encolar archivo {Path}", fullPath);
        }
    }

    private async Task ConsumeLoopAsync(CancellationToken cancellationToken)
    {
        while (await _queue.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_queue.Reader.TryRead(out var fullPath))
            {
                try
                {
                    if (await WaitUntilReadyAsync(fullPath, cancellationToken))
                    {
                        await _processor.ProcessAsync(fullPath, cancellationToken);
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
                finally
                {
                    _pending.TryRemove(fullPath, out _);
                }
            }
        }
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
        await _watcherRecoveryLock.WaitAsync();
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

                await Task.Delay(TimeSpan.FromSeconds(30), _runCancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _recoveryInProgress, 0);
            _watcherRecoveryLock.Release();
        }
    }

    private async Task<int> EnqueueExistingFilesAsync()
    {
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(_options.Path, _options.Filter, SearchOption.TopDirectoryOnly))
        {
            count++;
            await EnqueuePathAsync(path);
        }

        return count;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }
}
