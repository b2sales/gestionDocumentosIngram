using GestionDocumentos.Core.Options;
using GestionDocumentos.Core.Services;
using GestionDocumentos.Gre;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Host;

public sealed class GreTxtWatcherHostedService : BackgroundService
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollWhenDisabled = TimeSpan.FromMinutes(5);

    private const string RegistryName = "GRE-TXT";

    private readonly GreTxtTriggerProcessor _processor;
    private readonly IOptionsMonitor<GreOptions> _greOptions;
    private readonly ILogger<GreTxtWatcherHostedService> _logger;
    private readonly ILogger<FolderWatcherEngine> _watcherLogger;
    private readonly WatcherStatusRegistry _statusRegistry;
    private FolderWatcherEngine? _engine;

    public GreTxtWatcherHostedService(
        GreTxtTriggerProcessor processor,
        IOptionsMonitor<GreOptions> greOptions,
        ILogger<GreTxtWatcherHostedService> logger,
        ILogger<FolderWatcherEngine> watcherLogger,
        WatcherStatusRegistry statusRegistry)
    {
        _processor = processor;
        _greOptions = greOptions;
        _logger = logger;
        _watcherLogger = watcherLogger;
        _statusRegistry = statusRegistry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "GRE TXT watcher falló; reintentando en {Delay}.",
                    RetryAfterFailure);
                await SafeDelayAsync(RetryAfterFailure, stoppingToken);
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var o = _greOptions.CurrentValue;
        if (string.IsNullOrWhiteSpace(o.GreTxt))
        {
            _logger.LogWarning("GRE TXT trigger deshabilitado: greTXT vacío.");
            await SafeDelayAsync(PollWhenDisabled, stoppingToken);
            return;
        }

        if (!Directory.Exists(o.GreTxt))
        {
            _logger.LogWarning("GRE TXT trigger: directorio greTXT no existe: {Path}", o.GreTxt);
            await SafeDelayAsync(RetryAfterFailure, stoppingToken);
            return;
        }

        var watcherOptions = new WatcherOptions
        {
            Path = o.GreTxt,
            Filter = "*.txt",
            QueueCapacity = o.QueueCapacity,
            WorkerCount = Math.Max(1, Math.Min(o.ProcessingConcurrency, 4)),
            FileReadyRetries = o.FileReadyRetries,
            FileReadyDelayMs = o.FileReadyDelayMs,
            RequireExclusiveReadinessLock = true,
            InternalBufferSize = o.WatcherInternalBufferSize,
            FailedFolder = "",
            MaxProcessAttempts = o.MaxProcessAttempts
        };

        var engine = new FolderWatcherEngine(_processor, watcherOptions, _watcherLogger);
        _engine = engine;
        _statusRegistry.Register(RegistryName, () => engine.GetSnapshot());
        try
        {
            await engine.StartAsync(stoppingToken);
            _logger.LogInformation("GRE TXT watcher iniciado en {Path}", o.GreTxt);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            _statusRegistry.Unregister(RegistryName);
            try
            {
                await engine.StopAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GRE TXT watcher: error deteniendo engine.");
            }

            _engine = null;
        }
    }

    private static async Task SafeDelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
