using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Core.Email;

public sealed class ErrorEmailLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ErrorEmailQueue _queue;
    private readonly IOptionsMonitor<SmtpErrorEmailOptions> _options;
    private IExternalScopeProvider? _scopeProvider;

    public ErrorEmailLoggerProvider(ErrorEmailQueue queue, IOptionsMonitor<SmtpErrorEmailOptions> options)
    {
        _queue = queue;
        _options = options;
    }

    public ILogger CreateLogger(string categoryName) =>
        new ErrorEmailLogger(categoryName, _queue, _options, _scopeProvider);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class ErrorEmailLogger : ILogger
    {
        private readonly string _category;
        private readonly ErrorEmailQueue _queue;
        private readonly IOptionsMonitor<SmtpErrorEmailOptions> _options;
        private readonly IExternalScopeProvider? _scopeProvider;

        public ErrorEmailLogger(
            string category,
            ErrorEmailQueue queue,
            IOptionsMonitor<SmtpErrorEmailOptions> options,
            IExternalScopeProvider? scopeProvider)
        {
            _category = category;
            _queue = queue;
            _options = options;
            _scopeProvider = scopeProvider;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            _scopeProvider?.Push(state) ?? NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            _options.CurrentValue.Enabled && logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var detail = exception?.ToString();
            var item = new ErrorEmailItem(_category, logLevel, message, detail, DateTimeOffset.UtcNow);
            _queue.Writer.TryWrite(item);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
