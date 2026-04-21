using GestionDocumentos.Core.Abstractions;
using GestionDocumentos.Core.Options;
using GestionDocumentos.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionDocumentos.Tests;

public sealed class FolderWatcherEngineResilienceTests : IAsyncDisposable
{
    private readonly string _watchDir;

    public FolderWatcherEngineResilienceTests()
    {
        _watchDir = Path.Combine(Path.GetTempPath(), "gd-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_watchDir);
    }

    [Fact(Timeout = 15_000)]
    public async Task Quarantine_moves_file_after_max_attempts()
    {
        var failedFolder = Path.Combine(Path.GetTempPath(), "gd-tests-failed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var processor = new ThrowingProcessor();
            var options = new WatcherOptions
            {
                Path = _watchDir,
                Filter = "*.dat",
                WorkerCount = 1,
                QueueCapacity = 8,
                FileReadyRetries = 2,
                FileReadyDelayMs = 50,
                RequireExclusiveReadinessLock = false,
                InternalBufferSize = 8192,
                FailedFolder = failedFolder,
                MaxProcessAttempts = 2
            };

            await using var engine = new FolderWatcherEngine(processor, options, NullLogger<FolderWatcherEngine>.Instance);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await engine.StartAsync(cts.Token);

            var fileName = "boom.dat";
            var sourcePath = Path.Combine(_watchDir, fileName);
            await File.WriteAllTextAsync(sourcePath, "payload", cts.Token);

            // Primer intento: el FSW lo detecta. Tras fallar (1/2) queda en origen con contador=1.
            await Task.Delay(500, cts.Token);

            // Reemitimos "cambio" modificando el archivo para que el watcher lo reenvíe al worker.
            if (File.Exists(sourcePath))
            {
                File.SetLastWriteTime(sourcePath, DateTime.Now);
                // y sobrescribimos para forzar evento Changed
                await File.WriteAllTextAsync(sourcePath, "payload2", cts.Token);
            }

            // Esperamos a que se active la cuarentena.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            string? movedTo = null;
            while (movedTo is null && DateTime.UtcNow < deadline)
            {
                if (Directory.Exists(failedFolder))
                {
                    var match = Directory
                        .EnumerateFiles(failedFolder, fileName, SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (match is not null)
                    {
                        movedTo = match;
                        break;
                    }
                }

                await Task.Delay(100, cts.Token);
            }

            Assert.NotNull(movedTo);
            Assert.False(File.Exists(sourcePath), "archivo original debe haber sido movido");
            Assert.True(File.Exists(movedTo + ".log"), "archivo .log con stack trace debe existir");

            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await engine.StopAsync(stopCts.Token);
        }
        finally
        {
            if (Directory.Exists(failedFolder))
            {
                Directory.Delete(failedFolder, recursive: true);
            }
        }
    }

    [Fact(Timeout = 10_000)]
    public async Task Engine_keeps_running_when_processor_always_throws()
    {
        var processor = new ThrowingProcessor();
        var options = new WatcherOptions
        {
            Path = _watchDir,
            Filter = "*.dat",
            WorkerCount = 2,
            QueueCapacity = 16,
            FileReadyRetries = 2,
            FileReadyDelayMs = 50,
            RequireExclusiveReadinessLock = false,
            InternalBufferSize = 8192
        };

        await using var engine = new FolderWatcherEngine(processor, options, NullLogger<FolderWatcherEngine>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await engine.StartAsync(cts.Token);

        for (var i = 0; i < 5; i++)
        {
            var path = Path.Combine(_watchDir, $"f{i}.dat");
            await File.WriteAllTextAsync(path, "payload", cts.Token);
            await Task.Delay(120, cts.Token);
        }

        // Esperar a que el engine drene (o al menos intente) los 5 archivos.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (processor.CallCount < 5 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, cts.Token);
        }

        Assert.True(processor.CallCount >= 5, $"processor debe haber sido invocado >=5 veces; fue {processor.CallCount}");

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await engine.StopAsync(stopCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_watchDir))
            {
                Directory.Delete(_watchDir, recursive: true);
            }
        }
        catch
        {
        }

        await Task.CompletedTask;
    }

    private sealed class ThrowingProcessor : IFileProcessor
    {
        private int _count;
        public int CallCount => _count;

        public Task ProcessAsync(string fullPath, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            throw new InvalidOperationException("simulated processor failure");
        }
    }
}
