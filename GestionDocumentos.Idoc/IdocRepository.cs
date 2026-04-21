using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Idoc;

public sealed class IdocRepository
{
    private const int NameFileInChunkSize = 500;

    /// <summary>
    /// Códigos de error SQL Server para violaciones de clave única:
    /// 2601 = duplicate key en índice único, 2627 = violación PK/UK constraint.
    /// </summary>
    private static readonly HashSet<int> DuplicateKeyErrorNumbers = [2601, 2627];

    private readonly IOptionsMonitor<IdocOptions> _options;
    private readonly ILogger<IdocRepository> _logger;

    public IdocRepository(IOptionsMonitor<IdocOptions> options, ILogger<IdocRepository> logger)
    {
        _options = options;
        _logger = logger;
    }

    private string ConnectionString => _options.CurrentValue.ConnectionString;

    public Task<bool> ExistsByNameFileAsync(string nameFile, CancellationToken cancellationToken) =>
        SqlTransientRetry.ExecuteAsync(
            _logger,
            nameof(ExistsByNameFileAsync),
            async () =>
            {
                const string sql = "SELECT 1 FROM Documentos WHERE NameFile = @NameFile;";
                await using var connection = new SqlConnection(ConnectionString);
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

        await using var connection = new SqlConnection(ConnectionString);
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

    /// <remarks>
    /// <para>
    /// Idempotencia: se intenta el INSERT directamente (sin <c>SELECT COUNT(1)</c> previo, que con
    /// <see cref="IsolationLevel.Serializable"/> podía causar escalamiento de locks y deadlocks bajo carga).
    /// Si un segundo thread/instancia ya insertó el registro, SQL Server devolverá error 2601/2627 y
    /// respondemos con <c>WasInserted=false</c>.
    /// </para>
    /// <para>
    /// Requiere un índice único sobre <c>Documentos.NameFile</c>. Script separado (fuera de la app):
    /// <c>CREATE UNIQUE INDEX UX_Documentos_NameFile ON Documentos(NameFile);</c>
    /// </para>
    /// </remarks>
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

        var fecha = NormalizeDate(documento.Fecha);
        var fecVen = NormalizeDate(documento.FechaVencimiento);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
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

            var detailRows = await InsertDetailsBatchedAsync(connection, tx, documento, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new IdocInsertResult(WasInserted: true, RowsAffected: 1 + detailRows);
        }
        catch (SqlException sqlEx) when (IsDuplicateKeyError(sqlEx))
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogInformation(
                "IDOC: {File} ya existe (duplicate key detectado). Saltando.",
                documento.ArchivoTibco);
            return new IdocInsertResult(WasInserted: false, RowsAffected: 0);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error insertando documento {File}", documento.ArchivoTibco);
            throw;
        }
    }

    /// <summary>
    /// Inserta los detalles en tandas de <see cref="DetailBatchSize"/> filas por comando,
    /// reduciendo <c>N</c> round-trips a aproximadamente <c>N/DetailBatchSize</c>.
    /// Alternativa a un Table-Valued Parameter (TVP), que sería más eficiente pero requiere
    /// crear un <c>User-Defined Table Type</c> en la base (script DBA, fuera de la app).
    /// </summary>
    private const int DetailBatchSize = 50;

    private static async Task<int> InsertDetailsBatchedAsync(
        SqlConnection connection,
        SqlTransaction tx,
        IdocDocument documento,
        CancellationToken cancellationToken)
    {
        var detalle = documento.Detalle;
        if (detalle is null || detalle.Count == 0)
        {
            return 0;
        }

        var totalRows = 0;
        for (var offset = 0; offset < detalle.Count; offset += DetailBatchSize)
        {
            var count = Math.Min(DetailBatchSize, detalle.Count - offset);
            var sql = new StringBuilder(
                "INSERT INTO DET_DOCUMENTOS (FacInterno, NumPed, Cod_Material, Descripcion, Cantidad) VALUES ");

            await using var cmd = new SqlCommand { Connection = connection, Transaction = tx };

            cmd.Parameters.Add("@FacInterno", SqlDbType.NVarChar, 64).Value = documento.DocumentoSap;
            cmd.Parameters.Add("@NumPed", SqlDbType.NVarChar, 64).Value = documento.Pedido;

            for (var i = 0; i < count; i++)
            {
                var item = detalle[offset + i];
                if (i > 0)
                {
                    sql.Append(',');
                }

                sql.Append("(@FacInterno,@NumPed,@m").Append(i).Append(",@d").Append(i).Append(",@q").Append(i).Append(')');

                cmd.Parameters.Add("@m" + i, SqlDbType.NVarChar, 64).Value =
                    IdocDetailNormalizer.NormalizePartNumber(item.PartNumber);
                cmd.Parameters.Add("@d" + i, SqlDbType.NVarChar, 512).Value = item.Descripcion ?? string.Empty;
                cmd.Parameters.Add("@q" + i, SqlDbType.NVarChar, 64).Value =
                    IdocDetailNormalizer.NormalizeCantidad(item.Cantidad);
            }

            sql.Append(';');
            cmd.CommandText = sql.ToString();
            totalRows += await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        return totalRows;
    }

    private static bool IsDuplicateKeyError(SqlException ex)
    {
        foreach (SqlError error in ex.Errors)
        {
            if (DuplicateKeyErrorNumbers.Contains(error.Number))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeDate(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value.Replace("-", "", StringComparison.Ordinal);
}
