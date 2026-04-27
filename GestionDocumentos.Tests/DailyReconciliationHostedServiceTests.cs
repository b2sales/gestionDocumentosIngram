using System.Reflection;
using System.Runtime.CompilerServices;
using GestionDocumentos.Gre;
using GestionDocumentos.Host;
using GestionDocumentos.Idoc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Tests;

public sealed class DailyReconciliationHostedServiceTests : IDisposable
{
    private readonly string _folder;

    public DailyReconciliationHostedServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "gd-reconcile-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [Theory]
    [InlineData("00:00", true)]
    [InlineData("23:59", true)]
    [InlineData("7:30", true)]
    [InlineData("24:00", false)]
    [InlineData("12:60", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void TryParseLocalTime_validates_expected_formats(string input, bool expected)
    {
        var method = typeof(DailyReconciliationHostedService).GetMethod(
            "TryParseLocalTime",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var args = new object?[] { input, null };
        var ok = (bool)(method!.Invoke(null, args) ?? false);

        Assert.Equal(expected, ok);
        if (expected)
        {
            Assert.IsType<TimeSpan>(args[1]);
        }
    }

    [Fact]
    public void GetNextLocalRunTime_returns_today_or_tomorrow_correctly()
    {
        var parseMethod = typeof(DailyReconciliationHostedService).GetMethod(
            "TryParseLocalTime",
            BindingFlags.NonPublic | BindingFlags.Static);
        var nextMethod = typeof(DailyReconciliationHostedService).GetMethod(
            "GetNextLocalRunTime",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(parseMethod);
        Assert.NotNull(nextMethod);

        var now = DateTimeOffset.Now;
        var plusOneMinute = now.AddMinutes(1);
        var minusOneMinute = now.AddMinutes(-1);

        var todayTime = ParseTime(parseMethod!, $"{plusOneMinute:HH:mm}");
        var tomorrowTime = ParseTime(parseMethod!, $"{minusOneMinute:HH:mm}");

        var nextToday = (DateTimeOffset)(nextMethod!.Invoke(null, [todayTime]) ?? default(DateTimeOffset));
        var nextTomorrow = (DateTimeOffset)(nextMethod.Invoke(null, [tomorrowTime]) ?? default(DateTimeOffset));

        Assert.Equal(now.Date, nextToday.Date);
        Assert.Equal(now.Date.AddDays(1), nextTomorrow.Date);
    }

    [Fact]
    public void GetCandidateFiles_respects_only_today_and_max_files_ordering()
    {
        var service = CreateUninitializedService();
        var getCandidates = typeof(DailyReconciliationHostedService).GetMethod(
            "GetCandidateFiles",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(getCandidates);

        var oldPath = Path.Combine(_folder, "old.xml");
        var todayEarly = Path.Combine(_folder, "today-1.xml");
        var todayLate = Path.Combine(_folder, "today-2.xml");
        var todayNewest = Path.Combine(_folder, "today-3.xml");

        File.WriteAllText(oldPath, "old");
        File.WriteAllText(todayEarly, "t1");
        File.WriteAllText(todayLate, "t2");
        File.WriteAllText(todayNewest, "t3");

        var today = DateTime.Today;
        File.SetLastWriteTime(oldPath, today.AddDays(-1).AddHours(9));
        File.SetCreationTime(oldPath, today.AddDays(-1).AddHours(9));

        File.SetLastWriteTime(todayEarly, today.AddHours(8));
        File.SetCreationTime(todayEarly, today.AddHours(8));
        File.SetLastWriteTime(todayLate, today.AddHours(10));
        File.SetCreationTime(todayLate, today.AddHours(10));
        File.SetLastWriteTime(todayNewest, today.AddHours(12));
        File.SetCreationTime(todayNewest, today.AddHours(12));

        var onlyTodayLimited = (List<string>)(getCandidates!.Invoke(service, [_folder, "*.xml", true, 2]) ?? new List<string>());
        var allLimited = (List<string>)(getCandidates.Invoke(service, [_folder, "*.xml", false, 2]) ?? new List<string>());

        Assert.Equal(2, onlyTodayLimited.Count);
        Assert.DoesNotContain(oldPath, onlyTodayLimited);
        Assert.Equal(todayEarly, onlyTodayLimited[0]);
        Assert.Equal(todayLate, onlyTodayLimited[1]);

        Assert.Equal(2, allLimited.Count);
        Assert.Equal(oldPath, allLimited[0]);
        Assert.Equal(todayEarly, allLimited[1]);
    }

    [Fact]
    public void Run_lock_allows_single_entry_until_released()
    {
        var service = CreateUninitializedService();
        var tryEnter = typeof(DailyReconciliationHostedService).GetMethod(
            "TryEnterRunLock",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var exit = typeof(DailyReconciliationHostedService).GetMethod(
            "ExitRunLock",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(tryEnter);
        Assert.NotNull(exit);

        var first = (bool)(tryEnter!.Invoke(service, null) ?? false);
        var second = (bool)(tryEnter.Invoke(service, null) ?? false);

        Assert.True(first);
        Assert.False(second);

        exit!.Invoke(service, null);

        var third = (bool)(tryEnter.Invoke(service, null) ?? false);
        Assert.True(third);
    }

    [Fact]
    public async Task ReconcileGreAsync_returns_early_when_gre_folder_is_empty()
    {
        var service = CreateUninitializedService();
        SetField(service, "_greOptions", new TestOptionsMonitor<GreOptions>(new GreOptions { GrePdf = "" }));

        var method = typeof(DailyReconciliationHostedService).GetMethod(
            "ReconcileGreAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(service, [false, false, 1, 10, CancellationToken.None]);
        Assert.NotNull(task);
        await task!;
    }

    [Fact]
    public async Task ReconcileGreAsync_returns_early_when_gre_folder_does_not_exist()
    {
        var service = CreateUninitializedService();
        SetField(service, "_greOptions", new TestOptionsMonitor<GreOptions>(new GreOptions
        {
            GrePdf = Path.Combine(_folder, "missing-gre")
        }));

        var method = typeof(DailyReconciliationHostedService).GetMethod(
            "ReconcileGreAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(service, [false, false, 1, 10, CancellationToken.None]);
        Assert.NotNull(task);
        await task!;
    }

    [Fact]
    public async Task ReconcileIdocAsync_returns_early_when_watch_folder_cannot_be_resolved()
    {
        var idocOpts = new IdocOptions
        {
            BackOfficeConnectionString = "",
            WatchFolder = "",
            TibcoRoot = ""
        };

        var service = CreateUninitializedService();
        SetField(service, "_idocOptions", new TestOptionsMonitor<IdocOptions>(idocOpts));
        SetField(service, "_idocPaths", new IdocBackOfficePaths());
        SetField(service, "_backOfficeReader", new BackOfficeParameterReader(
            new TestOptionsMonitor<IdocOptions>(idocOpts),
            NullLogger<BackOfficeParameterReader>.Instance));

        var method = typeof(DailyReconciliationHostedService).GetMethod(
            "ReconcileIdocAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(service, [false, false, 1, 10, CancellationToken.None]);
        Assert.NotNull(task);
        await task!;
    }

    [Fact]
    public async Task RunReconciliationAsync_completes_when_both_pipelines_are_disabled()
    {
        var service = CreateUninitializedService();
        SetField(service, "_reconcileOptions", new TestOptionsMonitor<ReconciliationOptions>(new ReconciliationOptions
        {
            Enabled = true,
            GreEnabled = false,
            IdocEnabled = false,
            MaxConcurrent = 2,
            MaxFilesPerSource = 10,
            SkipAlreadyInDatabase = true,
            OnlyTodaysFiles = true
        }));

        var method = typeof(DailyReconciliationHostedService).GetMethod(
            "RunReconciliationAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(service, [CancellationToken.None]);
        Assert.NotNull(task);
        await task!;
    }

    [Fact]
    public async Task ScheduleAndRunAsync_returns_fast_when_disabled_and_token_is_cancelled()
    {
        var service = CreateUninitializedService();
        SetField(service, "_reconcileOptions", new TestOptionsMonitor<ReconciliationOptions>(new ReconciliationOptions
        {
            Enabled = false
        }));

        var method = typeof(DailyReconciliationHostedService).GetMethod(
            "ScheduleAndRunAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var task = (Task?)method!.Invoke(service, [cts.Token]);
        Assert.NotNull(task);
        await task!;
    }

    [Fact]
    public async Task ScheduleAndRunAsync_returns_fast_on_invalid_time_and_cancelled_token()
    {
        var service = CreateUninitializedService();
        SetField(service, "_reconcileOptions", new TestOptionsMonitor<ReconciliationOptions>(new ReconciliationOptions
        {
            Enabled = true,
            DailyTimeLocal = "invalid-time"
        }));

        var method = typeof(DailyReconciliationHostedService).GetMethod(
            "ScheduleAndRunAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var task = (Task?)method!.Invoke(service, [cts.Token]);
        Assert.NotNull(task);
        await task!;
    }

    [Fact]
    public async Task ResolveIdocFolderAsync_prefers_idocpaths_when_already_resolved()
    {
        var idocPaths = new IdocBackOfficePaths();
        idocPaths.Apply("/tmp/from-paths", "/tmp/from-paths", resolvedFromDatabase: true);

        var idocOpts = new IdocOptions
        {
            BackOfficeConnectionString = "",
            WatchFolder = "/tmp/from-options",
            TibcoRoot = "/tmp/from-options"
        };

        var service = CreateUninitializedService();
        SetField(service, "_idocPaths", idocPaths);
        SetField(service, "_idocOptions", new TestOptionsMonitor<IdocOptions>(idocOpts));
        SetField(service, "_backOfficeReader", new BackOfficeParameterReader(
            new TestOptionsMonitor<IdocOptions>(idocOpts),
            NullLogger<BackOfficeParameterReader>.Instance));

        var method = typeof(DailyReconciliationHostedService).GetMethod(
            "ResolveIdocFolderAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<(string? Path, bool ResolvedFromDatabase)>?)method!.Invoke(service, [CancellationToken.None]);
        Assert.NotNull(task);
        var result = await task!;

        Assert.Equal("/tmp/from-paths", result.Path);
        Assert.True(result.ResolvedFromDatabase);
    }

    [Fact]
    public async Task SafeDelayAsync_swallows_cancellation()
    {
        var method = typeof(DailyReconciliationHostedService).GetMethod(
            "SafeDelayAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var task = (Task?)method!.Invoke(null, [TimeSpan.FromMinutes(5), cts.Token]);
        Assert.NotNull(task);
        await task!;
    }

    [Fact]
    public async Task ReconcileIdocAsync_returns_early_when_resolved_folder_does_not_exist()
    {
        var resolvedMissing = Path.Combine(_folder, "missing-idoc-folder");
        var idocPaths = new IdocBackOfficePaths();
        idocPaths.Apply(resolvedMissing, resolvedMissing, resolvedFromDatabase: true);

        var idocOpts = new IdocOptions
        {
            BackOfficeConnectionString = "",
            WatchFolder = "/tmp/unused",
            TibcoRoot = "/tmp/unused"
        };

        var service = CreateUninitializedService();
        SetField(service, "_idocPaths", idocPaths);
        SetField(service, "_idocOptions", new TestOptionsMonitor<IdocOptions>(idocOpts));
        SetField(service, "_backOfficeReader", new BackOfficeParameterReader(
            new TestOptionsMonitor<IdocOptions>(idocOpts),
            NullLogger<BackOfficeParameterReader>.Instance));

        var method = typeof(DailyReconciliationHostedService).GetMethod(
            "ReconcileIdocAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(service, [false, false, 1, 10, CancellationToken.None]);
        Assert.NotNull(task);
        await task!;
    }

    [Fact]
    public async Task ReconcileIdocAsync_completes_when_folder_exists_and_has_no_xml_candidates()
    {
        var existingFolder = Path.Combine(_folder, "idoc-empty");
        Directory.CreateDirectory(existingFolder);

        var idocPaths = new IdocBackOfficePaths();
        idocPaths.Apply(existingFolder, existingFolder, resolvedFromDatabase: false);

        var idocOpts = new IdocOptions
        {
            BackOfficeConnectionString = "",
            WatchFolder = existingFolder,
            TibcoRoot = existingFolder
        };

        var service = CreateUninitializedService();
        SetField(service, "_idocPaths", idocPaths);
        SetField(service, "_idocOptions", new TestOptionsMonitor<IdocOptions>(idocOpts));
        SetField(service, "_backOfficeReader", new BackOfficeParameterReader(
            new TestOptionsMonitor<IdocOptions>(idocOpts),
            NullLogger<BackOfficeParameterReader>.Instance));

        var method = typeof(DailyReconciliationHostedService).GetMethod(
            "ReconcileIdocAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(service, [true, false, 1, 10, CancellationToken.None]);
        Assert.NotNull(task);
        await task!;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static TimeSpan ParseTime(MethodInfo parseMethod, string hhmm)
    {
        var args = new object?[] { hhmm, null };
        var ok = (bool)(parseMethod.Invoke(null, args) ?? false);
        Assert.True(ok, $"No se pudo parsear hora '{hhmm}'.");
        return (TimeSpan)(args[1] ?? default(TimeSpan));
    }

    private static DailyReconciliationHostedService CreateUninitializedService()
    {
        var service = (DailyReconciliationHostedService)RuntimeHelpers.GetUninitializedObject(typeof(DailyReconciliationHostedService));
        var loggerField = typeof(DailyReconciliationHostedService).GetField(
            "_logger",
            BindingFlags.NonPublic | BindingFlags.Instance);
        loggerField?.SetValue(service, NullLogger<DailyReconciliationHostedService>.Instance);
        return service;
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
