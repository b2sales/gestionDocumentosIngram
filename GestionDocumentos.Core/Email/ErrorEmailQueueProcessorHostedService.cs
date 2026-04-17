using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Core.Email;

public sealed class ErrorEmailQueueProcessorHostedService : BackgroundService
{
    private readonly ErrorEmailQueue _queue;
    private readonly SmtpErrorEmailSender _sender;
    private readonly IOptionsMonitor<SmtpErrorEmailOptions> _options;
    private readonly ILogger<ErrorEmailQueueProcessorHostedService> _logger;
    private DateTimeOffset _lastSentUtc = DateTimeOffset.MinValue;

    public ErrorEmailQueueProcessorHostedService(
        ErrorEmailQueue queue,
        SmtpErrorEmailSender sender,
        IOptionsMonitor<SmtpErrorEmailOptions> options,
        ILogger<ErrorEmailQueueProcessorHostedService> logger)
    {
        _queue = queue;
        _sender = sender;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var o = _options.CurrentValue;
            if (!o.Enabled)
            {
                continue;
            }

            var throttle = TimeSpan.FromSeconds(Math.Max(0, o.ThrottleSeconds));
            var now = DateTimeOffset.UtcNow;
            if (throttle > TimeSpan.Zero && now - _lastSentUtc < throttle)
            {
                _logger.LogDebug("Correo de error omitido por throttle ({Seconds}s)", o.ThrottleSeconds);
                continue;
            }

            await _sender.SendAsync(item, stoppingToken);
            _lastSentUtc = DateTimeOffset.UtcNow;
        }
    }
}
