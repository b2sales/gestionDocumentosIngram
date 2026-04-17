using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace GestionDocumentos.Idoc;

public sealed class IdocRepository
{
    private const int NameFileInChunkSize = 500;
    private readonly string _connectionString;
    private readonly ILogger<IdocRepository> _logger;

    public IdocRepository(string connectionString, ILogger<IdocRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public Task<bool> ExistsByNameFileAsync(string nameFile, CancellationToken cancellationToken) =>
        SqlTransientRetry.ExecuteAsync(
            _logger,
            nameof(ExistsByNameFileAsync),
            async () =>
            {
                const string sql = "SELECT 1 FROM Documentos WHERE NameFile = @NameFile;";
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.Add("@NameFile", SqlDbType.NVarChar, 512).Value = nameFile;
                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
                return await reader.ReadAsync(cancellationToken);
            },
            cancellationToken);

    public Task<HashSet<string>> GetExistingNameFilesAsync(
        IReadOnlyList<string> nameFiles,
        CancellationToken cancellationToken) =>
        SqlTransientRetry.ExecuteAsync(
            _logger,
            nameof(GetExistingNameFilesAsync),
            () => GetExistingNameFilesCoreAsync(nameFiles, cancellationToken),
            cancellationToken);

    private async Task<HashSet<string>> GetExistingNameFilesCoreAsync(
        IReadOnlyList<string> nameFiles,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (nameFiles.Count == 0)
        {
            return result;
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        for (var offset = 0; offset < nameFiles.Count; offset += NameFileInChunkSize)
        {
            var count = Math.Min(NameFileInChunkSize, nameFiles.Count - offset);
            var sql = new StringBuilder("SELECT NameFile FROM Documentos WHERE NameFile IN (");
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sql.Append(',');
                }

                sql.Append("@p").Append(i);
            }

            sql.Append(')');

            await using var cmd = new SqlCommand(sql.ToString(), connection);
            for (var i = 0; i < count; i++)
            {
                cmd.Parameters.Add("@p" + i, SqlDbType.NVarChar, 512).Value = nameFiles[offset + i];
            }

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    result.Add(reader.GetString(0));
                }
            }
        }

        return result;
    }

    public Task<IdocInsertResult> TryInsertDocumentAsync(IdocDocument documento, CancellationToken cancellationToken) =>
        SqlTransientRetry.ExecuteAsync(
            _logger,
            nameof(TryInsertDocumentAsync),
            () => TryInsertDocumentCoreAsync(documento, cancellationToken),
            cancellationToken);

    private async Task<IdocInsertResult> TryInsertDocumentCoreAsync(IdocDocument documento, CancellationToken cancellationToken)
    {
        const string insertHeader =
            """
            INSERT INTO DOCUMENTOS (
                NameFile, TipDoc, Serie, Numero, Fecha, Cod_SAP, CodVen, Ruc, Cliente, Moneda, NumPed,
                FacInterno, Monto, FecVenci, ConPag, Contacto, Sunat, Estado, EstCobranza, Deli, Situacion,
                indNotificacion, paymentIssueTime, referenceDocNumber, orderReason, MontoDetraccion, PorcentajeDet, customerOrderNumber)
            VALUES (
                @NameFile, @TipDoc, @Serie, @Numero, @Fecha, @CodSap, @CodVen, @Ruc, @Cliente, @Moneda, @NumPed,
                @FacInterno, @Monto, @FecVenci, @ConPag, @Contacto, @Sunat, @Estado, @EstCobranza, @Deli, @Situacion,
                @IndNotificacion, @PaymentIssueTime, @ReferenceDocNumber, @OrderReason, @MontoDetraccion, @PorcentajeDet, @CustomerOrderNumber);
            """;

        const string insertDetail =
            """
            INSERT INTO DET_DOCUMENTOS (FacInterno, NumPed, Cod_Material, Descripcion, Cantidad)
            VALUES (@FacInterno, @NumPed, @CodMaterial, @Descripcion, @Cantidad);
            """;

        var fecha = NormalizeDate(documento.Fecha);
        var fecVen = NormalizeDate(documento.FechaVencimiento);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            const string checkExists = "SELECT COUNT(1) FROM Documentos WHERE NameFile = @NameFile;";
            await using (var checkCmd = new SqlCommand(checkExists, connection, tx))
            {
                checkCmd.Parameters.Add("@NameFile", SqlDbType.NVarChar, 512).Value = documento.ArchivoTibco;
                var countObj = await checkCmd.ExecuteScalarAsync(cancellationToken);
                var count = countObj is int i ? i : Convert.ToInt32(countObj, CultureInfo.InvariantCulture);
                if (count > 0)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return new IdocInsertResult(WasInserted: false, RowsAffected: 0);
                }
            }

            await using (var cmd = new SqlCommand(insertHeader, connection, tx))
            {
                cmd.Parameters.Add("@NameFile", SqlDbType.NVarChar, 512).Value = documento.ArchivoTibco;
                cmd.Parameters.Add("@TipDoc", SqlDbType.NVarChar, 16).Value = documento.TipoDoc;
                cmd.Parameters.Add("@Serie", SqlDbType.NVarChar, 32).Value = documento.Serie;
                cmd.Parameters.Add("@Numero", SqlDbType.NVarChar, 32).Value = documento.Numero;
                cmd.Parameters.Add("@Fecha", SqlDbType.NVarChar, 32).Value = fecha;
                cmd.Parameters.Add("@CodSap", SqlDbType.NVarChar, 64).Value = documento.CodSapCliente;
                cmd.Parameters.Add("@CodVen", SqlDbType.NVarChar, 64).Value = documento.CodVen;
                cmd.Parameters.Add("@Ruc", SqlDbType.NVarChar, 32).Value = documento.RucCliente;
                cmd.Parameters.Add("@Cliente", SqlDbType.NVarChar, 512).Value = documento.NomCliente;
                cmd.Parameters.Add("@Moneda", SqlDbType.NVarChar, 16).Value = documento.Moneda;
                cmd.Parameters.Add("@NumPed", SqlDbType.NVarChar, 64).Value = documento.Pedido;
                cmd.Parameters.Add("@FacInterno", SqlDbType.NVarChar, 64).Value = documento.DocumentoSap;
                cmd.Parameters.Add("@Monto", SqlDbType.NVarChar, 64).Value = documento.Monto;
                cmd.Parameters.Add("@FecVenci", SqlDbType.NVarChar, 32).Value = fecVen;
                cmd.Parameters.Add("@ConPag", SqlDbType.NVarChar, 64).Value = documento.PaymentMethod;
                cmd.Parameters.Add("@Contacto", SqlDbType.NVarChar, 256).Value = documento.Contacto;
                cmd.Parameters.Add("@Sunat", SqlDbType.NVarChar, 64).Value = "No procesado";
                cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 64).Value = "Pendiente";
                cmd.Parameters.Add("@EstCobranza", SqlDbType.NVarChar, 64).Value = "Sin Gestion";
                cmd.Parameters.Add("@Deli", SqlDbType.NVarChar, 64).Value = "1";
                cmd.Parameters.Add("@Situacion", SqlDbType.NVarChar, 64).Value = "Pendiente";
                cmd.Parameters.Add("@IndNotificacion", SqlDbType.Int).Value = 0;
                cmd.Parameters.Add("@PaymentIssueTime", SqlDbType.NVarChar, 64).Value = documento.PaymentIssueTime;
                cmd.Parameters.Add("@ReferenceDocNumber", SqlDbType.NVarChar, 128).Value = documento.ReferenceDocNumber;
                cmd.Parameters.Add("@OrderReason", SqlDbType.NVarChar, 256).Value = documento.OrderReason;
                cmd.Parameters.Add("@MontoDetraccion", SqlDbType.Decimal).Value = documento.MontoDetraccion;
                cmd.Parameters.Add("@PorcentajeDet", SqlDbType.Decimal).Value = documento.PorcentajeDet;
                cmd.Parameters.Add("@CustomerOrderNumber", SqlDbType.NVarChar, 128).Value = documento.CustomerOrderNumber;

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            var rows = 1;
            foreach (var item in documento.Detalle)
            {
                var codMaterial = IdocDetailNormalizer.NormalizePartNumber(item.PartNumber);
                var cantidad = IdocDetailNormalizer.NormalizeCantidad(item.Cantidad);

                await using var cmd = new SqlCommand(insertDetail, connection, tx);
                cmd.Parameters.Add("@FacInterno", SqlDbType.NVarChar, 64).Value = documento.DocumentoSap;
                cmd.Parameters.Add("@NumPed", SqlDbType.NVarChar, 64).Value = documento.Pedido;
                cmd.Parameters.Add("@CodMaterial", SqlDbType.NVarChar, 64).Value = codMaterial;
                cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 512).Value = item.Descripcion;
                cmd.Parameters.Add("@Cantidad", SqlDbType.NVarChar, 64).Value = cantidad;
                rows += await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return new IdocInsertResult(WasInserted: true, RowsAffected: rows);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error insertando documento {File}", documento.ArchivoTibco);
            throw;
        }
    }

    private static string NormalizeDate(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value.Replace("-", "", StringComparison.Ordinal);
}
