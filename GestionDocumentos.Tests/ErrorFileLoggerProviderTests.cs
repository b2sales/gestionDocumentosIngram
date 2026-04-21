using GestionDocumentos.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Tests;

public sealed class ErrorFileLoggerProviderTests : IDisposable
{
    private readonly string _logFolder;

    public ErrorFileLoggerProviderTests()
    {
        _logFolder = Path.Combine(Path.GetTempPath(), "gd-log-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logFolder);
    }

    [Fact]
    public void LogError_writes_error_entry_to_daily_file()
    {
        var provider = CreateProvider(new ErrorFileLogOptions
        {
            Enabled = true,
            FolderPath = _logFolder,
            FileNamePrefix = "errors",
            RetentionDays = 30
        });

        var logger = provider.CreateLogger("GestionDocumentos.Tests.Sample");
        var ex = new InvalidOperationException("simulated failure");

        logger.LogError(ex, "fallo procesando archivo {File}", "demo.xml");

        var path = Path.Combine(_logFolder, $"errors-{DateTimeOffset.Now:yyyy-MM-dd}.log");
        Assert.True(File.Exists(path));

        var content = File.ReadAllText(path);
        Assert.Contains("GestionDocumentos.Tests.Sample", content);
        Assert.Contains("fallo procesando archivo demo.xml", content);
        Assert.Contains("simulated failure", content);
    }

    [Fact]
    public void LogError_deletes_old_files_when_retention_is_exceeded()
    {
        var oldPath = Path.Combine(_logFolder, "errors-2000-01-01.log");
        File.WriteAllText(oldPath, "old");
        File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-40));

        var provider = CreateProvider(new ErrorFileLogOptions
        {
            Enabled = true,
            FolderPath = _logFolder,
            FileNamePrefix = "errors",
            RetentionDays = 30
        });

        var logger = provider.CreateLogger("GestionDocumentos.Tests.Sample");
        logger.LogError("otro error");

        Assert.False(File.Exists(oldPath));
        var currentPath = Path.Combine(_logFolder, $"errors-{DateTimeOffset.Now:yyyy-MM-dd}.log");
        Assert.True(File.Exists(currentPath));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_logFolder))
            {
                Directory.Delete(_logFolder, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static ErrorFileLoggerProvider CreateProvider(ErrorFileLogOptions options) =>
        new(new TestOptionsMonitor<ErrorFileLogOptions>(options));

    private sealed class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    {
        public TestOptionsMonitor(TOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public TOptions CurrentValue { get; private set; }

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
