using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GestionDocumentos.Core.Options;
using GestionDocumentos.Core.Services;
using GestionDocumentos.Idoc;

namespace GestionDocumentos.Host;

public sealed class IdocWatcherHostedService : BackgroundService
{
    private readonly IdocFileProcessor _processor;
    private readonly IOptionsMonitor<IdocOptions> _idocOptions;
    private readonly BackOfficeParameterReader _backOfficeReader;
    private readonly IdocBackOfficePaths _paths;
    private readonly ILogger<IdocWatcherHostedService> _logger;
    private readonly ILogger<FolderWatcherEngine> _watcherLogger;
    private FolderWatcherEngine? _engine;

    public IdocWatcherHostedService(
        IdocFileProcessor processor,
        IOptionsMonitor<IdocOptions> idocOptions,
        BackOfficeParameterReader backOfficeReader,
        IdocBackOfficePaths paths,
        ILogger<IdocWatcherHostedService> logger,
        ILogger<FolderWatcherEngine> watcherLogger)
    {
        _processor = processor;
        _idocOptions = idocOptions;
        _backOfficeReader = backOfficeReader;
        _paths = paths;
        _logger = logger;
        _watcherLogger = watcherLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var resolved = await TryResolveWatchFolderAsync(stoppingToken);
            if (resolved is not null)
            {
                _paths.Apply(
                    resolved.Value.WatchFolder,
                    resolved.Value.TibcoRoot,
                    resolved.Value.ResolvedFromDatabase);

                if (Directory.Exists(_paths.WatchFolder))
                {
                    var watcherOptions = BuildWatcherOptions(_idocOptions.CurrentValue, _paths.WatchFolder);
                    _engine = new FolderWatcherEngine(_processor, watcherOptions, _watcherLogger);
                    await _engine.StartAsync(stoppingToken);
                    _logger.LogInformation("IDOC watcher iniciado en {Path}", _paths.WatchFolder);
                    break;
                }

                _logger.LogWarning(
                    "IDOC pipeline pendiente: carpeta no existe: {Path}. Reintentando en 30s.",
                    _paths.WatchFolder);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (_engine is null)
        {
            return;
        }

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

    private async Task<(string WatchFolder, string TibcoRoot, bool ResolvedFromDatabase)?> TryResolveWatchFolderAsync(
        CancellationToken stoppingToken)
    {
        var o = _idocOptions.CurrentValue;
        if (!string.IsNullOrWhiteSpace(o.BackOfficeConnectionString))
        {
            var i2Carpeta = await _backOfficeReader.GetValorAsync("IDOC", "I2CARPETA", stoppingToken);
            if (string.IsNullOrWhiteSpace(i2Carpeta))
            {
                _logger.LogWarning(
                    "IDOC pipeline pendiente: no se pudo resolver IDOC/I2CARPETA desde backOfficeDB. Reintentando en 30s.");
                return null;
            }

            _logger.LogInformation("IDOC: carpeta desde seguridad.parametros (I2CARPETA): {Path}", i2Carpeta);
            return (i2Carpeta, i2Carpeta, true);
        }

        if (string.IsNullOrWhiteSpace(o.WatchFolder))
        {
            _logger.LogWarning(
                "IDOC pipeline pendiente: backOfficeContext vacío e idocFolder vacío. Reintentando en 30s.");
            return null;
        }

        _logger.LogWarning(
            "IDOC: usando rutas desde Parametros.json (idocFolder); en producción defina backOfficeContext.");
        return (o.WatchFolder, o.TibcoRoot, false);
    }

    private static WatcherOptions BuildWatcherOptions(IdocOptions options, string watchFolder) =>
        new()
        {
            Path = watchFolder,
            Filter = "*.xml",
            QueueCapacity = options.QueueCapacity,
            WorkerCount = options.ProcessingConcurrency,
            FileReadyRetries = options.FileReadyRetries,
            FileReadyDelayMs = options.FileReadyDelayMs,
            RequireExclusiveReadinessLock = true,
            InternalBufferSize = options.WatcherInternalBufferSize
        };
}
