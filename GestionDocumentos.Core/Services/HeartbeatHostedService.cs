using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Core.Services;

/// <summary>
/// Emite un log <see cref="LogLevel.Information"/> periódico con el estado agregado de los
/// watchers registrados en <see cref="WatcherStatusRegistry"/>. Permite detectar watchers
/// detenidos o colas estancadas sin necesidad de inspeccionar métricas externas.
/// </summary>
public sealed class HeartbeatHostedService : BackgroundService
{
    private readonly WatcherStatusRegistry _registry;
    private readonly IOptionsMonitor<HeartbeatOptions> _options;
    private readonly ILogger<HeartbeatHostedService> _logger;

    public HeartbeatHostedService(
        WatcherStatusRegistry registry,
        IOptionsMonitor<HeartbeatOptions> options,
        ILogger<HeartbeatHostedService> logger)
    {
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var opts = _options.CurrentValue;
                if (opts.Enabled)
                {
                    EmitHeartbeat();
                }

                var interval = TimeSpan.FromMinutes(Math.Max(1, opts.IntervalMinutes));
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat: ciclo falló; reintentando en 1 min.");
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void EmitHeartbeat()
    {
        var snapshots = _registry.SnapshotAll();
        if (snapshots.Count == 0)
        {
            _logger.LogInformation("Heartbeat: sin watchers registrados.");
            return;
        }

        foreach (var (name, snap) in snapshots)
        {
            _logger.LogInformation(
                "Heartbeat: {Name} active={Active} pending={Pending} path={Path} lastProcessed={LastProcessed}",
                name,
                snap.IsActive,
                snap.PendingCount,
                snap.Path ?? "(n/a)",
                snap.LastProcessedAtUtc?.ToString("O") ?? "never");
        }
    }
}
