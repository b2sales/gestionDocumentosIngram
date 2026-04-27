using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Core.Email;

public sealed class ErrorEmailQueueProcessorHostedService : BackgroundService
{
    private readonly ErrorEmailQueue _queue;
    private readonly IErrorEmailSender _sender;
    private readonly IOptionsMonitor<SmtpErrorEmailOptions> _options;
    private readonly ILogger<ErrorEmailQueueProcessorHostedService> _logger;
    private readonly Func<DateTimeOffset> _utcNow;
    private DateTimeOffset _lastSentUtc = DateTimeOffset.MinValue;

    public ErrorEmailQueueProcessorHostedService(
        ErrorEmailQueue queue,
        IErrorEmailSender sender,
        IOptionsMonitor<SmtpErrorEmailOptions> options,
        ILogger<ErrorEmailQueueProcessorHostedService> logger,
        Func<DateTimeOffset>? utcNow = null)
    {
        _queue = queue;
        _sender = sender;
        _options = options;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[ErrorEmailQueueProcessor] Error inesperado; reintentando en 30s: {ex}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;
        while (await reader.WaitToReadAsync(stoppingToken))
        {
            await ProcessSingleBatchAsync(stoppingToken);
        }
    }

    internal async Task<bool> ProcessSingleBatchAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;
        if (!reader.TryRead(out var first))
        {
            return false;
        }

        var o = _options.CurrentValue;
        if (!o.Enabled)
        {
            // Drain cola para no hacer que un cambio Enabled=false bloquee consumidores.
            return true;
        }

        var throttle = TimeSpan.FromSeconds(Math.Max(0, o.ThrottleSeconds));
        var now = _utcNow();
        if (throttle > TimeSpan.Zero && now - _lastSentUtc < throttle)
        {
            _logger.LogDebug("Correo de error omitido por throttle ({Seconds}s)", o.ThrottleSeconds);
            return true;
        }

        // Agregamos durante AggregationWindowSeconds o hasta MaxBatchSize.
        var batch = new List<ErrorEmailItem>(capacity: Math.Max(1, o.MaxBatchSize)) { first };
        var windowSeconds = Math.Max(0, o.AggregationWindowSeconds);
        var maxBatch = Math.Max(1, o.MaxBatchSize);
        if (windowSeconds > 0)
        {
            var deadline = _utcNow() + TimeSpan.FromSeconds(windowSeconds);
            while (batch.Count < maxBatch)
            {
                var remaining = deadline - _utcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                windowCts.CancelAfter(remaining);
                try
                {
                    if (!await reader.WaitToReadAsync(windowCts.Token))
                    {
                        break;
                    }

                    while (batch.Count < maxBatch && reader.TryRead(out var next))
                    {
                        batch.Add(next);
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        try
        {
            await _sender.SendBatchAsync(batch, stoppingToken);
            _lastSentUtc = _utcNow();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[ErrorEmailQueueProcessor] Fallo enviando batch de {batch.Count} item(s) (se descartan): {ex}");
        }

        return true;
    }
}
