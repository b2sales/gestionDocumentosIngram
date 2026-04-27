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

    private WatcherOptions CreateOptions(int queueCapacity, int workerCount) =>
        new()
        {
            Path = _watchDir,
            Filter = "*.dat",
            WorkerCount = workerCount,
            QueueCapacity = queueCapacity,
            FileReadyRetries = 5,
            FileReadyDelayMs = 20,
            RequireExclusiveReadinessLock = false,
            InternalBufferSize = 8192,
            RescanMaxFiles = 5000,
            RescanMaxAge = TimeSpan.FromDays(7)
        };

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
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
