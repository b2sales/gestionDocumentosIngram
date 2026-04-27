using System.Reflection;
using System.Runtime.CompilerServices;
using GestionDocumentos.Host;
using Microsoft.Extensions.Logging.Abstractions;

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
}
