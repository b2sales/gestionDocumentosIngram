using System.Reflection;
using GestionDocumentos.Idoc;
using Microsoft.Data.SqlClient;

namespace GestionDocumentos.Tests;

public sealed class IdocRepositoryLogicTests
{
    [Theory]
    [InlineData(2601, true)]
    [InlineData(2627, true)]
    [InlineData(1205, false)]
    [InlineData(547, false)]
    public void IsDuplicateKeyError_detects_expected_sql_numbers(int sqlNumber, bool expected)
    {
        var ex = CreateSqlException(sqlNumber, "sql");
        var method = typeof(IdocRepository).GetMethod(
            "IsDuplicateKeyError",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var actual = (bool)(method.Invoke(null, [ex]) ?? false);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsDuplicateKeyError_returns_true_when_any_error_in_collection_is_duplicate()
    {
        var ex = CreateSqlExceptionWithErrors([1205, 2627, 547]);
        var method = typeof(IdocRepository).GetMethod(
            "IsDuplicateKeyError",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var actual = (bool)(method.Invoke(null, [ex]) ?? false);
        Assert.True(actual);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("2026-04-27", "20260427")]
    [InlineData("20260427", "20260427")]
    public void NormalizeDate_behaves_as_expected(string? input, string expected)
    {
        var method = typeof(IdocRepository).GetMethod(
            "NormalizeDate",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var actual = method.Invoke(null, [input]) as string;
        Assert.Equal(expected, actual);
    }

    private static SqlException CreateSqlException(int number, string message) =>
        CreateSqlExceptionWithErrors([number], message);

    private static SqlException CreateSqlExceptionWithErrors(IReadOnlyList<int> numbers, string message = "sql")
    {
        var sqlErrorCollectionType = typeof(SqlErrorCollection);
        var sqlErrorCollection = Activator.CreateInstance(sqlErrorCollectionType, nonPublic: true)
            ?? throw new InvalidOperationException("Could not create SqlErrorCollection.");

        var addMethod = sqlErrorCollectionType.GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find SqlErrorCollection.Add.");

        foreach (var number in numbers)
        {
            addMethod.Invoke(sqlErrorCollection, [CreateSqlError(number, message)]);
        }

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

            return parameter.ParameterType.IsValueType
                ? Activator.CreateInstance(parameter.ParameterType)
                : null;
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
}
