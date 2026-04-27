using System.Diagnostics;
using System.Reflection;
using GestionDocumentos.Idoc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace GestionDocumentos.Tests;

public sealed class SqlTransientRetryTests
{
    [Fact]
    public async Task ExecuteAsync_retries_transient_errors_until_success()
    {
        var logger = new CapturingLogger();
        var attempts = 0;
        var sw = Stopwatch.StartNew();

        var result = await SqlTransientRetry.ExecuteAsync(
            logger,
            operationName: "test-op",
            action: () =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException<int>(CreateSqlException(-2, "timeout"))
                    : Task.FromResult(42);
            },
            cancellationToken: CancellationToken.None,
            maxAttempts: 5);

        sw.Stop();

        Assert.Equal(42, result);
        Assert.Equal(3, attempts);
        Assert.Equal(2, logger.WarningCount);
        Assert.True(sw.ElapsedMilliseconds >= 550, $"elapsed={sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ExecuteAsync_throws_immediately_for_non_transient_sql_errors()
    {
        var logger = new CapturingLogger();
        var attempts = 0;

        await Assert.ThrowsAsync<SqlException>(async () =>
            await SqlTransientRetry.ExecuteAsync(
                logger,
                operationName: "test-op",
                action: () =>
                {
                    attempts++;
                    return Task.FromException<int>(CreateSqlException(2627, "duplicate key"));
                },
                cancellationToken: CancellationToken.None,
                maxAttempts: 5));

        Assert.Equal(1, attempts);
        Assert.Equal(0, logger.WarningCount);
    }

    [Fact]
    public async Task ExecuteAsync_throws_after_max_attempts_for_transient_errors()
    {
        var logger = new CapturingLogger();
        var attempts = 0;

        await Assert.ThrowsAsync<SqlException>(async () =>
            await SqlTransientRetry.ExecuteAsync(
                logger,
                operationName: "test-op",
                action: () =>
                {
                    attempts++;
                    return Task.FromException<int>(CreateSqlException(1205, "deadlock"));
                },
                cancellationToken: CancellationToken.None,
                maxAttempts: 3));

        Assert.Equal(3, attempts);
        Assert.Equal(2, logger.WarningCount);
    }

    [Theory]
    [InlineData(-2, true)]
    [InlineData(1205, true)]
    [InlineData(40501, true)]
    [InlineData(2627, false)]
    [InlineData(547, false)]
    public void IsTransient_detects_expected_sql_error_numbers(int number, bool expected)
    {
        var ex = CreateSqlException(number, "test");
        Assert.Equal(expected, SqlTransientRetry.IsTransient(ex));
    }

    private static SqlException CreateSqlException(int number, string message)
    {
        var sqlErrorCollectionType = typeof(SqlErrorCollection);
        var sqlErrorCollection = Activator.CreateInstance(sqlErrorCollectionType, nonPublic: true)
            ?? throw new InvalidOperationException("Could not create SqlErrorCollection.");

        var sqlError = CreateSqlError(number, message);
        var addMethod = sqlErrorCollectionType.GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find SqlErrorCollection.Add.");
        addMethod.Invoke(sqlErrorCollection, [sqlError]);

        var createExceptionMethod = typeof(SqlException)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .FirstOrDefault(m =>
                m.Name == "CreateException" &&
                m.ReturnType == typeof(SqlException) &&
                m.GetParameters().Length >= 2 &&
                m.GetParameters()[0].ParameterType == typeof(SqlErrorCollection))
            ?? throw new InvalidOperationException("Could not find SqlException.CreateException.");

        var parameters = createExceptionMethod.GetParameters()
            .Select(BuildCreateExceptionArgument(sqlErrorCollection))
            .ToArray();

        return (SqlException)(createExceptionMethod.Invoke(null, parameters)
            ?? throw new InvalidOperationException("Could not invoke SqlException.CreateException."));
    }

    private static Func<ParameterInfo, object?> BuildCreateExceptionArgument(object sqlErrorCollection) =>
        parameter =>
        {
            if (parameter.ParameterType == typeof(SqlErrorCollection))
            {
                return sqlErrorCollection;
            }

            if (parameter.ParameterType == typeof(string))
            {
                return "8.0.0";
            }

            if (parameter.ParameterType == typeof(Exception))
            {
                return null;
            }

            if (parameter.ParameterType == typeof(Guid))
            {
                return Guid.Empty;
            }

            if (parameter.ParameterType.IsValueType)
            {
                return Activator.CreateInstance(parameter.ParameterType);
            }

            return null;
        };

    private static SqlError CreateSqlError(int number, string message)
    {
        var ctor = typeof(SqlError)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault(c => c.GetParameters().Length >= 7)
            ?? throw new InvalidOperationException("Could not find SqlError constructor.");

        var args = ctor.GetParameters()
            .Select(p => BuildSqlErrorArgument(number, message, p.ParameterType))
            .ToArray();

        return (SqlError)(ctor.Invoke(args)
            ?? throw new InvalidOperationException("Could not invoke SqlError constructor."));
    }

    private static object? BuildSqlErrorArgument(int number, string message, Type parameterType)
    {
        if (parameterType == typeof(int))
        {
            return number;
        }

        if (parameterType == typeof(byte))
        {
            return (byte)0;
        }

        if (parameterType == typeof(string))
        {
            return message;
        }

        if (parameterType == typeof(uint))
        {
            return (uint)0;
        }

        if (parameterType == typeof(Exception))
        {
            return null;
        }

        return parameterType.IsValueType ? Activator.CreateInstance(parameterType) : null;
    }

    private sealed class CapturingLogger : ILogger
    {
        public int WarningCount { get; private set; }

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }
}
