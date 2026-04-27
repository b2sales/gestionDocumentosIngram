using GestionDocumentos.Idoc;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;

namespace GestionDocumentos.Tests;

public sealed class IdocRepositoryIntegrationTests : IAsyncLifetime
{
    private MsSqlContainer? _sql;
    private string? _skipReason;
    private IdocRepository? _repository;

    public async Task InitializeAsync()
    {
        try
        {
            _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();

            await _sql.StartAsync();
            await EnsureSchemaAsync(_sql.GetConnectionString());
            _repository = CreateRepository(_sql.GetConnectionString());
        }
        catch (DockerUnavailableException ex)
        {
            _skipReason = $"Docker no disponible para integration tests: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_sql is not null)
        {
            await _sql.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryInsertDocumentAsync_inserts_header_and_details_in_batches()
    {
        if (SkipIfDockerUnavailable())
        {
            return;
        }
        var repository = AssertRepository();
        var doc = BuildDocument("file-batch-1.xml", detailCount: 120);

        var result = await repository.TryInsertDocumentAsync(doc, CancellationToken.None);

        Assert.True(result.WasInserted);
        Assert.Equal(121, result.RowsAffected);

        await using var con = new SqlConnection(AssertSql().GetConnectionString());
        await con.OpenAsync();

        var headerCount = await ExecuteScalarIntAsync(
            con,
            "SELECT COUNT(1) FROM Documentos WHERE NameFile = @name;",
            ("@name", doc.ArchivoTibco));
        Assert.Equal(1, headerCount);

        var detailCount = await ExecuteScalarIntAsync(
            con,
            "SELECT COUNT(1) FROM DET_DOCUMENTOS WHERE FacInterno = @fac;",
            ("@fac", doc.DocumentoSap));
        Assert.Equal(120, detailCount);
    }

    [Fact]
    public async Task TryInsertDocumentAsync_returns_not_inserted_for_duplicate_namefile()
    {
        if (SkipIfDockerUnavailable())
        {
            return;
        }
        var repository = AssertRepository();
        var doc = BuildDocument("file-dup.xml", detailCount: 2);

        var first = await repository.TryInsertDocumentAsync(doc, CancellationToken.None);
        var second = await repository.TryInsertDocumentAsync(doc, CancellationToken.None);

        Assert.True(first.WasInserted);
        Assert.False(second.WasInserted);
        Assert.Equal(0, second.RowsAffected);

        await using var con = new SqlConnection(AssertSql().GetConnectionString());
        await con.OpenAsync();
        var headerCount = await ExecuteScalarIntAsync(
            con,
            "SELECT COUNT(1) FROM Documentos WHERE NameFile = @name;",
            ("@name", doc.ArchivoTibco));
        Assert.Equal(1, headerCount);
    }

    [Fact]
    public async Task GetExistingNameFilesAsync_handles_input_larger_than_500_chunk_size()
    {
        if (SkipIfDockerUnavailable())
        {
            return;
        }
        var repository = AssertRepository();

        var existing = new[] { "exists-1.xml", "exists-2.xml", "exists-3.xml" };
        foreach (var name in existing)
        {
            var inserted = await repository.TryInsertDocumentAsync(BuildDocument(name, detailCount: 1), CancellationToken.None);
            Assert.True(inserted.WasInserted);
        }

        var candidates = Enumerable.Range(0, 1002)
            .Select(i => $"candidate-{i}.xml")
            .ToList();
        candidates[123] = "exists-1.xml";
        candidates[500] = "exists-2.xml";
        candidates[1001] = "exists-3.xml";

        var found = await repository.GetExistingNameFilesAsync(candidates, CancellationToken.None);

        Assert.Equal(3, found.Count);
        Assert.Contains("exists-1.xml", found);
        Assert.Contains("exists-2.xml", found);
        Assert.Contains("exists-3.xml", found);
    }

    private static IdocRepository CreateRepository(string connectionString)
    {
        var options = new IdocOptions { ConnectionString = connectionString };
        return new IdocRepository(
            new TestOptionsMonitor<IdocOptions>(options),
            NullLogger<IdocRepository>.Instance);
    }

    private static async Task EnsureSchemaAsync(string connectionString)
    {
        const string sql =
            """
            IF OBJECT_ID('dbo.Documentos', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Documentos(
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    NameFile NVARCHAR(512) NOT NULL,
                    TipDoc NVARCHAR(16) NOT NULL,
                    Serie NVARCHAR(32) NOT NULL,
                    Numero NVARCHAR(32) NOT NULL,
                    Fecha NVARCHAR(32) NOT NULL,
                    Cod_SAP NVARCHAR(64) NOT NULL,
                    CodVen NVARCHAR(64) NOT NULL,
                    Ruc NVARCHAR(32) NOT NULL,
                    Cliente NVARCHAR(512) NOT NULL,
                    Moneda NVARCHAR(16) NOT NULL,
                    NumPed NVARCHAR(64) NOT NULL,
                    FacInterno NVARCHAR(64) NOT NULL,
                    Monto NVARCHAR(64) NOT NULL,
                    FecVenci NVARCHAR(32) NOT NULL,
                    ConPag NVARCHAR(64) NOT NULL,
                    Contacto NVARCHAR(256) NOT NULL,
                    Sunat NVARCHAR(64) NOT NULL,
                    Estado NVARCHAR(64) NOT NULL,
                    EstCobranza NVARCHAR(64) NOT NULL,
                    Deli NVARCHAR(64) NOT NULL,
                    Situacion NVARCHAR(64) NOT NULL,
                    indNotificacion INT NOT NULL,
                    paymentIssueTime NVARCHAR(64) NOT NULL,
                    referenceDocNumber NVARCHAR(128) NOT NULL,
                    orderReason NVARCHAR(256) NOT NULL,
                    MontoDetraccion DECIMAL(18,2) NOT NULL,
                    PorcentajeDet DECIMAL(18,2) NOT NULL,
                    customerOrderNumber NVARCHAR(128) NOT NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Documentos_NameFile' AND object_id = OBJECT_ID('dbo.Documentos'))
            BEGIN
                CREATE UNIQUE INDEX UX_Documentos_NameFile ON dbo.Documentos(NameFile);
            END;

            IF OBJECT_ID('dbo.DET_DOCUMENTOS', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.DET_DOCUMENTOS(
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    FacInterno NVARCHAR(64) NOT NULL,
                    NumPed NVARCHAR(64) NOT NULL,
                    Cod_Material NVARCHAR(64) NOT NULL,
                    Descripcion NVARCHAR(512) NOT NULL,
                    Cantidad NVARCHAR(64) NOT NULL
                );
            END;
            """;

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync();
        await using var cmd = new SqlCommand(sql, con);
        await cmd.ExecuteNonQueryAsync();
    }

    private IdocRepository AssertRepository() =>
        _repository ?? throw new InvalidOperationException("Repository not initialized.");

    private MsSqlContainer AssertSql() =>
        _sql ?? throw new InvalidOperationException("SQL container not initialized.");

    private bool SkipIfDockerUnavailable()
    {
        if (!string.IsNullOrWhiteSpace(_skipReason))
        {
            return true;
        }

        return false;
    }

    private static IdocDocument BuildDocument(string fileName, int detailCount)
    {
        var doc = new IdocDocument
        {
            ArchivoTibco = fileName,
            TipoDoc = "01",
            Serie = "F001",
            Numero = Guid.NewGuid().ToString("N")[..8],
            Fecha = "2026-04-27",
            CodSapCliente = "C001",
            RucCliente = "20123456789",
            NomCliente = "Cliente Test",
            Moneda = "PEN",
            Monto = "100.00",
            DocumentoSap = "SAP-" + Guid.NewGuid().ToString("N")[..8],
            FechaVencimiento = "2026-05-27",
            PaymentMethod = "TRANSFER",
            Contacto = "test@example.com",
            Pedido = "PO-" + Guid.NewGuid().ToString("N")[..8],
            PaymentIssueTime = "10:00",
            CodVen = "V001",
            OrderReason = "R1",
            CustomerOrderNumber = "CO-1",
            MontoDetraccion = 0m,
            PorcentajeDet = 0m,
            ReferenceDocNumber = string.Empty
        };

        for (var i = 0; i < detailCount; i++)
        {
            doc.Detalle.Add(new IdocLineDetail
            {
                PartNumber = $"{6684000 + i:D7}",
                Descripcion = "Item " + i,
                Cantidad = "1.000"
            });
        }

        return doc;
    }

    private static async Task<int> ExecuteScalarIntAsync(
        SqlConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private sealed class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    {
        public TestOptionsMonitor(TOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public TOptions CurrentValue { get; }

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
