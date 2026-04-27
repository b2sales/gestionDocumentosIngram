using System.Reflection;
using System.Runtime.CompilerServices;
using GestionDocumentos.Core.Services;
using GestionDocumentos.Gre;
using GestionDocumentos.Host;
using GestionDocumentos.Idoc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Tests;

public sealed class HostWatcherHostedServiceTests
{
    [Fact]
    public async Task GreWatcher_RunOnceAsync_returns_when_grepdf_is_empty()
    {
        var service = (GreWatcherHostedService)RuntimeHelpers.GetUninitializedObject(typeof(GreWatcherHostedService));
        SetField(service, "_greOptions", new TestOptionsMonitor<GreOptions>(new GreOptions { GrePdf = "" }));
        SetField(service, "_logger", NullLogger<GreWatcherHostedService>.Instance);

        var runOnce = typeof(GreWatcherHostedService).GetMethod("RunOnceAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(runOnce);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var task = runOnce!.Invoke(service, [cts.Token]) as Task;
        Assert.NotNull(task);
        await task!;
    }

    [Fact]
    public async Task GreTxtWatcher_RunOnceAsync_returns_when_gretxt_is_empty()
    {
        var service = (GreTxtWatcherHostedService)RuntimeHelpers.GetUninitializedObject(typeof(GreTxtWatcherHostedService));
        SetField(service, "_greOptions", new TestOptionsMonitor<GreOptions>(new GreOptions { GreTxt = "" }));
        SetField(service, "_logger", NullLogger<GreTxtWatcherHostedService>.Instance);

        var runOnce = typeof(GreTxtWatcherHostedService).GetMethod("RunOnceAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(runOnce);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var task = runOnce!.Invoke(service, [cts.Token]) as Task;
        Assert.NotNull(task);
        await task!;
    }

    [Fact]
    public void IdocWatcher_BuildWatcherOptions_maps_values_correctly()
    {
        var method = typeof(IdocWatcherHostedService).GetMethod(
            "BuildWatcherOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var options = new IdocOptions
        {
            QueueCapacity = 123,
            ProcessingConcurrency = 7,
            FileReadyRetries = 9,
            FileReadyDelayMs = 77,
            WatcherInternalBufferSize = 9999,
            FailedFolder = "/tmp/failed",
            MaxProcessAttempts = 4
        };

        var result = method!.Invoke(null, [options, "/tmp/watch"])!;
        var watcherOptions = Assert.IsType<GestionDocumentos.Core.Options.WatcherOptions>(result);

        Assert.Equal("/tmp/watch", watcherOptions.Path);
        Assert.Equal("*.xml", watcherOptions.Filter);
        Assert.Equal(123, watcherOptions.QueueCapacity);
        Assert.Equal(7, watcherOptions.WorkerCount);
        Assert.Equal(9, watcherOptions.FileReadyRetries);
        Assert.Equal(77, watcherOptions.FileReadyDelayMs);
        Assert.True(watcherOptions.RequireExclusiveReadinessLock);
        Assert.Equal(9999, watcherOptions.InternalBufferSize);
        Assert.Equal("/tmp/failed", watcherOptions.FailedFolder);
        Assert.Equal(4, watcherOptions.MaxProcessAttempts);
    }

    [Fact]
    public async Task IdocWatcher_TryResolveWatchFolderAsync_uses_fallback_json_paths()
    {
        var idocOptions = new IdocOptions
        {
            BackOfficeConnectionString = "",
            WatchFolder = "/tmp/idoc-watch",
            TibcoRoot = "/tmp/idoc-tibco"
        };
        var service = (IdocWatcherHostedService)RuntimeHelpers.GetUninitializedObject(typeof(IdocWatcherHostedService));
        SetField(service, "_idocOptions", new TestOptionsMonitor<IdocOptions>(idocOptions));
        SetField(service, "_backOfficeReader", new BackOfficeParameterReader(
            new TestOptionsMonitor<IdocOptions>(idocOptions),
            NullLogger<BackOfficeParameterReader>.Instance));
        SetField(service, "_logger", NullLogger<IdocWatcherHostedService>.Instance);
        SetField(service, "_paths", new IdocBackOfficePaths());
        SetField(service, "_statusRegistry", new WatcherStatusRegistry());

        var method = typeof(IdocWatcherHostedService).GetMethod(
            "TryResolveWatchFolderAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<(string WatchFolder, string TibcoRoot, bool ResolvedFromDatabase)?>?)method!.Invoke(service, [CancellationToken.None]);
        Assert.NotNull(task);
        var result = await task!;

        Assert.NotNull(result);
        Assert.Equal("/tmp/idoc-watch", result!.Value.WatchFolder);
        Assert.Equal("/tmp/idoc-tibco", result.Value.TibcoRoot);
        Assert.False(result.Value.ResolvedFromDatabase);
    }

    [Fact]
    public async Task IdocWatcher_TryResolveWatchFolderAsync_returns_null_when_backoffice_is_configured_but_unreachable()
    {
        var idocOpts = new IdocOptions
        {
            BackOfficeConnectionString = "Server=invalid;Database=backoffice;User Id=u;Password=p;",
            WatchFolder = "",
            TibcoRoot = ""
        };
        var service = (IdocWatcherHostedService)RuntimeHelpers.GetUninitializedObject(typeof(IdocWatcherHostedService));
        SetField(service, "_idocOptions", new TestOptionsMonitor<IdocOptions>(idocOpts));
        SetField(service, "_backOfficeReader", new BackOfficeParameterReader(
            new TestOptionsMonitor<IdocOptions>(idocOpts),
            NullLogger<BackOfficeParameterReader>.Instance));
        SetField(service, "_logger", NullLogger<IdocWatcherHostedService>.Instance);

        var method = typeof(IdocWatcherHostedService).GetMethod(
            "TryResolveWatchFolderAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var task = (Task<(string WatchFolder, string TibcoRoot, bool ResolvedFromDatabase)?>?)method!.Invoke(service, [cts.Token]);
        Assert.NotNull(task);
        var result = await task!;

        Assert.Null(result);
    }

    [Fact]
    public async Task IdocWatcher_TryResolveWatchFolderAsync_returns_null_when_no_backoffice_and_no_watchfolder()
    {
        var idocOpts = new IdocOptions
        {
            BackOfficeConnectionString = "",
            WatchFolder = "",
            TibcoRoot = ""
        };
        var service = (IdocWatcherHostedService)RuntimeHelpers.GetUninitializedObject(typeof(IdocWatcherHostedService));
        SetField(service, "_idocOptions", new TestOptionsMonitor<IdocOptions>(idocOpts));
        SetField(service, "_backOfficeReader", new BackOfficeParameterReader(
            new TestOptionsMonitor<IdocOptions>(idocOpts),
            NullLogger<BackOfficeParameterReader>.Instance));
        SetField(service, "_logger", NullLogger<IdocWatcherHostedService>.Instance);

        var method = typeof(IdocWatcherHostedService).GetMethod(
            "TryResolveWatchFolderAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<(string WatchFolder, string TibcoRoot, bool ResolvedFromDatabase)?>?)method!.Invoke(service, [CancellationToken.None]);
        Assert.NotNull(task);
        var result = await task!;

        Assert.Null(result);
    }

    [Fact]
    public async Task GreWatcher_RunOnceAsync_returns_when_folder_does_not_exist_and_token_cancelled()
    {
        var service = (GreWatcherHostedService)RuntimeHelpers.GetUninitializedObject(typeof(GreWatcherHostedService));
        SetField(service, "_greOptions", new TestOptionsMonitor<GreOptions>(new GreOptions
        {
            GrePdf = Path.Combine(Path.GetTempPath(), "missing-gre-" + Guid.NewGuid().ToString("N"))
        }));
        SetField(service, "_logger", NullLogger<GreWatcherHostedService>.Instance);

        var runOnce = typeof(GreWatcherHostedService).GetMethod("RunOnceAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(runOnce);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var task = runOnce!.Invoke(service, [cts.Token]) as Task;
        Assert.NotNull(task);
        await task!;
    }

    private static void SetField<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

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
