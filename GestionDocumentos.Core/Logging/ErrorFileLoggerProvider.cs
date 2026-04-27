using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Core.Logging;

public sealed class ErrorFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly IOptionsMonitor<ErrorFileLogOptions> _options;
    private readonly object _sync = new();
    private IExternalScopeProvider? _scopeProvider;
    private DateOnly _lastCleanupUtcDate;
    private string _lastCleanupFolder = "";

    public ErrorFileLoggerProvider(IOptionsMonitor<ErrorFileLogOptions> options)
    {
        _options = options;
    }

    public ILogger CreateLogger(string categoryName) => new ErrorFileLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    public void Dispose()
    {
    }

    internal bool IsEnabled(LogLevel logLevel)
    {
        var options = _options.CurrentValue;
        return options.Enabled &&
               !string.IsNullOrWhiteSpace(options.FolderPath) &&
               logLevel >= LogLevel.Warning;
    }

    internal void WriteEntry(
        string category,
        LogLevel logLevel,
        EventId eventId,
        string message,
        Exception? exception)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.FolderPath) || logLevel < LogLevel.Warning)
        {
            return;
        }

        var folderPath = options.FolderPath.Trim();
        var fileNamePrefix = string.IsNullOrWhiteSpace(options.FileNamePrefix)
            ? "errors"
            : options.FileNamePrefix.Trim();

        var entry = BuildEntry(category, logLevel, eventId, message, exception);
        var logPath = Path.Combine(folderPath, $"{fileNamePrefix}-{DateTimeOffset.Now:yyyy-MM-dd}.log");

        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(folderPath);
                CleanupIfNeeded(folderPath, fileNamePrefix, options.RetentionDays);
                File.AppendAllText(logPath, entry, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[ErrorFileLoggerProvider] Fallo escribiendo log en '{folderPath}': {ex.Message}");
            }
        }
    }

    private string BuildEntry(
        string category,
        LogLevel logLevel,
        EventId eventId,
        string message,
        Exception? exception)
    {
        var sb = new StringBuilder();
        sb.Append('[')
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append("] ")
            .Append(logLevel)
            .Append(" | ")
            .Append(category);

        if (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name))
        {
            sb.Append(" | EventId=").Append(eventId.Id);
            if (!string.IsNullOrWhiteSpace(eventId.Name))
            {
                sb.Append(" (").Append(eventId.Name).Append(')');
            }
        }

        sb.AppendLine();
        sb.AppendLine(message);

        if (_scopeProvider is not null)
        {
            var scopes = new List<string>();
            _scopeProvider.ForEachScope(
                static (scope, state) =>
                {
                    state.Add(scope?.ToString() ?? "<null>");
                },
                scopes);

            if (scopes.Count > 0)
            {
                sb.AppendLine("Scopes:");
                foreach (var scope in scopes)
                {
                    sb.Append("  - ").AppendLine(scope);
                }
            }
        }

        if (exception is not null)
        {
            sb.AppendLine(exception.ToString());
        }

        sb.AppendLine(new string('-', 80));
        return sb.ToString();
    }

    private void CleanupIfNeeded(string folderPath, string fileNamePrefix, int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return;
        }

        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        if (todayUtc == _lastCleanupUtcDate && string.Equals(folderPath, _lastCleanupFolder, StringComparison.Ordinal))
        {
            return;
        }

        var cutoffUtc = DateTime.UtcNow.Date.AddDays(-retentionDays);
        foreach (var path in Directory.EnumerateFiles(folderPath, $"{fileNamePrefix}-*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var lastWriteUtc = File.GetLastWriteTimeUtc(path);
                if (lastWriteUtc < cutoffUtc)
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[ErrorFileLoggerProvider] Fallo eliminando log antiguo '{path}': {ex.Message}");
            }
        }

        _lastCleanupUtcDate = todayUtc;
        _lastCleanupFolder = folderPath;
    }

    private sealed class ErrorFileLogger : ILogger
    {
        private readonly string _category;
        private readonly ErrorFileLoggerProvider _provider;

        public ErrorFileLogger(string category, ErrorFileLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            _provider._scopeProvider?.Push(state) ?? NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

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
            _provider.WriteEntry(_category, logLevel, eventId, message, exception);
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
