using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Idoc;

/// <summary>
/// Lee valores de <c>seguridad.parametros</c> en backOfficeDB (misma lógica que el daemon legacy).
/// </summary>
public sealed class BackOfficeParameterReader
{
    private readonly IOptionsMonitor<IdocOptions> _options;
    private readonly ILogger<BackOfficeParameterReader> _logger;

    public BackOfficeParameterReader(
        IOptionsMonitor<IdocOptions> options,
        ILogger<BackOfficeParameterReader> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<string?> GetValorAsync(string tabla, string parametro, CancellationToken cancellationToken)
    {
        var connectionString = _options.CurrentValue.BackOfficeConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        const string sql =
            "SELECT TOP 1 valor FROM seguridad.parametros WHERE tabla = @tabla AND parametro = @parametro;";

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(sql, connection);
            cmd.CommandTimeout = 15;
            cmd.Parameters.Add("@tabla", SqlDbType.NVarChar, 128).Value = tabla;
            cmd.Parameters.Add("@parametro", SqlDbType.NVarChar, 128).Value = parametro;
            var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
            return scalar?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo parametro {Tabla}/{Parametro} desde backOfficeDB", tabla, parametro);
            return null;
        }
    }
}
