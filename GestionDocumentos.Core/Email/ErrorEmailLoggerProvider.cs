using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Core.Email;

public sealed class ErrorEmailLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    /// <summary>
    /// Prefijo de categorías excluidas para evitar loops: si el envío SMTP falla y loggea un error,
    /// esa entrada NO debe volver a encolarse, porque provocaría feedback infinito.
    /// </summary>
    private const string SelfCategoryPrefix = "GestionDocumentos.Core.Email";

    private readonly ErrorEmailQueue _queue;
    private readonly IOptionsMonitor<SmtpErrorEmailOptions> _options;
    private IExternalScopeProvider? _scopeProvider;

    public ErrorEmailLoggerProvider(ErrorEmailQueue queue, IOptionsMonitor<SmtpErrorEmailOptions> options)
    {
        _queue = queue;
        _options = options;
    }

    public ILogger CreateLogger(string categoryName)
    {
        var isSelfCategory = categoryName.StartsWith(SelfCategoryPrefix, StringComparison.Ordinal);
        return new ErrorEmailLogger(categoryName, _queue, _options, _scopeProvider, isSelfCategory);
    }

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
        private readonly bool _isSelfCategory;

        public ErrorEmailLogger(
            string category,
            ErrorEmailQueue queue,
            IOptionsMonitor<SmtpErrorEmailOptions> options,
            IExternalScopeProvider? scopeProvider,
            bool isSelfCategory)
        {
            _category = category;
            _queue = queue;
            _options = options;
            _scopeProvider = scopeProvider;
            _isSelfCategory = isSelfCategory;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            _scopeProvider?.Push(state) ?? NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            !_isSelfCategory && _options.CurrentValue.Enabled && logLevel >= LogLevel.Error;

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
