using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace GestionDocumentos.Idoc;

/// <summary>Errores SQL considerados transitorios para reintentos (timeouts, red, throttling, deadlocks).</summary>
internal static class SqlTransientRetry
{
    private static readonly HashSet<int> TransientErrorNumbers =
    [
        -2,
        20, 64, 233, 947, 921, 926,
        1204, 1205, 1222,
        4060, 40613, 40197, 40143, 40501, 40540, 40544, 40642, 40648, 40652, 40671,
        41839, 41840, 49918, 49919, 49920,
        10053, 10054, 10060
    ];

    public static bool IsTransient(SqlException ex) => TransientErrorNumbers.Contains(ex.Number);

    public static async Task<T> ExecuteAsync<T>(
        ILogger logger,
        string operationName,
        Func<Task<T>> action,
        CancellationToken cancellationToken,
        int maxAttempts = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action();
            }
            catch (SqlException ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                logger.LogWarning(
                    ex,
                    "SQL transitorio en {Operation} intento {Attempt}/{Max}; reintento en {Delay}ms (número {Number})",
                    operationName,
                    attempt,
                    maxAttempts,
                    delay.TotalMilliseconds,
                    ex.Number);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new UnreachableException($"{operationName}");
    }
}
