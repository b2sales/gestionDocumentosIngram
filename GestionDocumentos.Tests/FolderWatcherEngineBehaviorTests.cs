using System.Collections.Concurrent;
using System.Reflection;
using GestionDocumentos.Core.Abstractions;
using GestionDocumentos.Core.Options;
using GestionDocumentos.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionDocumentos.Tests;

public sealed class FolderWatcherEngineBehaviorTests : IAsyncDisposable
{
    private readonly string _watchDir;

    public FolderWatcherEngineBehaviorTests()
    {
        _watchDir = Path.Combine(Path.GetTempPath(), "gd-fw-behavior-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_watchDir);
    }

    [Fact(Timeout = 15_000)]
    public async Task StartAsync_requeues_existing_files_on_startup()
    {
        var existing = Path.Combine(_watchDir, "existing.dat");
        await File.WriteAllTextAsync(existing, "payload");

        var processor = new RecordingProcessor();
        await using var engine = new FolderWatcherEngine(
            processor,
            CreateOptions(queueCapacity: 8, workerCount: 1),
            NullLogger<FolderWatcherEngine>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await engine.StartAsync(cts.Token);
        await WaitUntilAsync(() => processor.ProcessedPaths.Count >= 1, cts.Token);

        Assert.Contains(existing, processor.ProcessedPaths);
    }

    [Fact(Timeout = 15_000)]
    public async Task Watcher_event_created_enqueues_and_processes_new_file()
    {
        var processor = new RecordingProcessor();
        await using var engine = new FolderWatcherEngine(
            processor,
            CreateOptions(queueCapacity: 8, workerCount: 1),
            NullLogger<FolderWatcherEngine>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await engine.StartAsync(cts.Token);

        var filePath = Path.Combine(_watchDir, "from-event.dat");
        await File.WriteAllTextAsync(filePath, "payload");

        await WaitUntilAsync(() => processor.ProcessedPaths.Contains(filePath), cts.Token);
        Assert.Contains(filePath, processor.ProcessedPaths);
    }

    [Fact(Timeout = 15_000)]
    public async Task WaitUntilReady_with_shared_probe_allows_concurrent_readers()
    {
        var existing = Path.Combine(_watchDir, "shared-read.dat");
        await File.WriteAllTextAsync(existing, "payload");

        var processor = new RecordingProcessor();
        await using var engine = new FolderWatcherEngine(
            processor,
            CreateOptions(queueCapacity: 8, workerCount: 1, readinessMode: FileReadinessMode.SharedReadProbe),
            NullLogger<FolderWatcherEngine>.Instance);

        // Simula otro lector (p. ej. impresión) con el archivo abierto en modo lectura compartida.
        await using var reader = new FileStream(existing, FileMode.Open, FileAccess.Read, FileShare.Read);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await engine.StartAsync(cts.Token);
        await WaitUntilAsync(() => processor.ProcessedPaths.Contains(existing), cts.Token);

        Assert.Contains(existing, processor.ProcessedPaths);
    }

    [Fact(Timeout = 15_000)]
    public async Task StableMetadata_processes_file_held_exclusively_by_another_process()
    {
        var existing = Path.Combine(_watchDir, "exclusive.dat");
        await File.WriteAllTextAsync(existing, "payload");

        var processor = new RecordingProcessor();
        await using var engine = new FolderWatcherEngine(
            processor,
            CreateOptions(queueCapacity: 8, workerCount: 1, readinessMode: FileReadinessMode.StableMetadata),
            NullLogger<FolderWatcherEngine>.Instance);

        // El servicio de impresión abre el PDF sin compartirlo con nadie.
        await using var exclusive = new FileStream(existing, FileMode.Open, FileAccess.Read, FileShare.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await engine.StartAsync(cts.Token);
        await WaitUntilAsync(() => processor.ProcessedPaths.Contains(existing), cts.Token);

        Assert.Contains(existing, processor.ProcessedPaths);
    }

    [Fact(Timeout = 20_000)]
    public async Task StableMetadata_waits_until_file_stops_growing()
    {
        var processor = new RecordingProcessor();
        await using var engine = new FolderWatcherEngine(
            processor,
            CreateOptions(
                queueCapacity: 8,
                workerCount: 1,
                readinessMode: FileReadinessMode.StableMetadata,
                fileReadyRetries: 40,
                fileReadyDelayMs: 100),
            NullLogger<FolderWatcherEngine>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        await engine.StartAsync(cts.Token);

        var growing = Path.Combine(_watchDir, "growing.dat");
        await using (var writer = new FileStream(growing, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            // Escritura continua: los bloques van mucho más seguidos que el muestreo del engine,
            // así que nunca debe verse como estable mientras el archivo siga creciendo.
            for (var i = 0; i < 40; i++)
            {
                await writer.WriteAsync(new byte[4096].AsMemory(), cts.Token);
                await writer.FlushAsync(cts.Token);
                File.SetLastWriteTimeUtc(growing, DateTime.UtcNow);
                await Task.Delay(30, cts.Token);
            }

            Assert.DoesNotContain(growing, processor.ProcessedPaths);
        }

        await WaitUntilAsync(() => processor.ProcessedPaths.Contains(growing), cts.Token);
    }

    [Fact(Timeout = 20_000)]
    public async Task QuietPeriod_delays_processing_until_file_is_old_enough()
    {
        var processor = new RecordingProcessor();
        var quietPeriod = TimeSpan.FromSeconds(3);
        await using var engine = new FolderWatcherEngine(
            processor,
            CreateOptions(queueCapacity: 8, workerCount: 1, quietPeriod: quietPeriod),
            NullLogger<FolderWatcherEngine>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        await engine.StartAsync(cts.Token);

        var filePath = Path.Combine(_watchDir, "quiet.dat");
        var createdAt = DateTime.UtcNow;
        await File.WriteAllTextAsync(filePath, "payload", cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
        Assert.DoesNotContain(filePath, processor.ProcessedPaths);

        await WaitUntilAsync(() => processor.ProcessedPaths.Contains(filePath), cts.Token, timeoutSeconds: 15);
        Assert.True(
            DateTime.UtcNow - createdAt >= quietPeriod,
            "el archivo no debe procesarse antes de cumplirse el periodo de espera");
    }

    [Fact(Timeout = 20_000)]
    public async Task Queue_full_does_not_drop_files_under_burst()
    {
        const int fileCount = 12;
        var processor = new DelayedRecordingProcessor(delayMs: 120);
        await using var engine = new FolderWatcherEngine(
            processor,
            CreateOptions(queueCapacity: 1, workerCount: 1),
            NullLogger<FolderWatcherEngine>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        await engine.StartAsync(cts.Token);

        var created = new List<string>(fileCount);
        for (var i = 0; i < fileCount; i++)
        {
            var path = Path.Combine(_watchDir, $"burst-{i}.dat");
            created.Add(path);
            await File.WriteAllTextAsync(path, "payload", cts.Token);
        }

        await WaitUntilAsync(() => processor.ProcessedPaths.Count >= fileCount, cts.Token);
        foreach (var path in created)
        {
            Assert.Contains(path, processor.ProcessedPaths);
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task RecoverWatcherAsync_recreates_watcher_and_requeues_existing_files()
    {
        var existing = Path.Combine(_watchDir, "recover-existing.dat");
        await File.WriteAllTextAsync(existing, "payload");

        var processor = new RecordingProcessor();
        await using var engine = new FolderWatcherEngine(
            processor,
            CreateOptions(queueCapacity: 8, workerCount: 1),
            NullLogger<FolderWatcherEngine>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await engine.StartAsync(cts.Token);

        await using (var clearDb = new FileStream(existing, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            // no-op, solo para asegurar que el archivo sigue presente durante la prueba
        }

        processor.ProcessedPaths.Clear();

        var recoverMethod = typeof(FolderWatcherEngine).GetMethod(
            "RecoverWatcherAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(recoverMethod);

        var recoverTask = recoverMethod!.Invoke(engine, null) as Task;
        Assert.NotNull(recoverTask);
        await recoverTask!;

        await WaitUntilAsync(() => processor.ProcessedPaths.Contains(existing), cts.Token);
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

    private WatcherOptions CreateOptions(
        int queueCapacity,
        int workerCount,
        FileReadinessMode readinessMode = FileReadinessMode.ReadWriteProbe,
        int fileReadyRetries = 5,
        int fileReadyDelayMs = 20,
        TimeSpan quietPeriod = default) =>
        new()
        {
            Path = _watchDir,
            Filter = "*.dat",
            WorkerCount = workerCount,
            QueueCapacity = queueCapacity,
            FileReadyRetries = fileReadyRetries,
            FileReadyDelayMs = fileReadyDelayMs,
            ReadinessMode = readinessMode,
            QuietPeriod = quietPeriod,
            InternalBufferSize = 8192,
            RescanMaxFiles = 5000,
            RescanMaxAge = TimeSpan.FromDays(7)
        };

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken,
        int timeoutSeconds = 8)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition not met within timeout.");
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private class RecordingProcessor : IFileProcessor
    {
        public ConcurrentBag<string> ProcessedPaths { get; } = new();

        public virtual Task ProcessAsync(string fullPath, CancellationToken cancellationToken)
        {
            ProcessedPaths.Add(fullPath);
            return Task.CompletedTask;
        }
    }

    private sealed class DelayedRecordingProcessor : RecordingProcessor
    {
        private readonly int _delayMs;

        public DelayedRecordingProcessor(int delayMs)
        {
            _delayMs = delayMs;
        }

        public override async Task ProcessAsync(string fullPath, CancellationToken cancellationToken)
        {
            await Task.Delay(_delayMs, cancellationToken);
            await base.ProcessAsync(fullPath, cancellationToken);
        }
    }
}
