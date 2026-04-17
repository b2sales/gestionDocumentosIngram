using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GestionDocumentos.Core.Options;
using GestionDocumentos.Core.Services;
using GestionDocumentos.Gre;

namespace GestionDocumentos.Host;

public sealed class GreWatcherHostedService : BackgroundService
{
    private readonly GreFileProcessor _processor;
    private readonly IOptions<GreOptions> _greOptions;
    private readonly ILogger<GreWatcherHostedService> _logger;
    private readonly ILogger<FolderWatcherEngine> _watcherLogger;
    private FolderWatcherEngine? _engine;

    public GreWatcherHostedService(
        GreFileProcessor processor,
        IOptions<GreOptions> greOptions,
        ILogger<GreWatcherHostedService> logger,
        ILogger<FolderWatcherEngine> watcherLogger)
    {
        _processor = processor;
        _greOptions = greOptions;
        _logger = logger;
        _watcherLogger = watcherLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = _greOptions.Value;
        if (string.IsNullOrWhiteSpace(o.GrePdf))
        {
            _logger.LogWarning("GRE pipeline deshabilitado: grePDF vacío.");
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }

            return;
        }

        if (!Directory.Exists(o.GrePdf))
        {
            _logger.LogWarning("GRE pipeline: directorio grePDF no existe: {Path}", o.GrePdf);
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }

            return;
        }

        var watcherOptions = new WatcherOptions
        {
            Path = o.GrePdf,
            Filter = "*.pdf",
            QueueCapacity = o.QueueCapacity,
            WorkerCount = o.ProcessingConcurrency,
            FileReadyRetries = o.FileReadyRetries,
            FileReadyDelayMs = o.FileReadyDelayMs,
            RequireExclusiveReadinessLock = true,
            InternalBufferSize = o.WatcherInternalBufferSize
        };

        _engine = new FolderWatcherEngine(_processor, watcherOptions, _watcherLogger);
        await _engine.StartAsync(stoppingToken);
        _logger.LogInformation("GRE watcher iniciado en {Path}", o.GrePdf);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_engine is not null)
            {
                await _engine.StopAsync(CancellationToken.None);
            }
        }
    }
}
