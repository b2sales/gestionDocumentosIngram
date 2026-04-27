using GestionDocumentos.Gre;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Tests;

public sealed class GreFileProcessorIntegrationTests : IDisposable
{
    private readonly string _rootDir;
    private readonly string _greTxtDir;
    private readonly string _dirPdfs;
    private readonly string _dirEcommerce;
    private readonly string _dirHpmps;
    private readonly string _dbName;

    public GreFileProcessorIntegrationTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "gd-gre-tests-" + Guid.NewGuid().ToString("N"));
        _greTxtDir = Path.Combine(_rootDir, "txt");
        _dirPdfs = Path.Combine(_rootDir, "out-pdfs");
        _dirEcommerce = Path.Combine(_rootDir, "out-ecommerce");
        _dirHpmps = Path.Combine(_rootDir, "out-hpmps");
        _dbName = "gre-db-" + Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(_rootDir);
        Directory.CreateDirectory(_greTxtDir);
        Directory.CreateDirectory(_dirPdfs);
        Directory.CreateDirectory(_dirEcommerce);
        Directory.CreateDirectory(_dirHpmps);
    }

    [Fact]
    public async Task ProcessAsync_happy_path_persists_and_copies_pdf()
    {
        var processor = CreateProcessor(CreateOptions());
        const string pdfFileName = "20267163228-09-T001-00118915.pdf";
        var pdfPath = Path.Combine(_rootDir, pdfFileName);
        var txtPath = Path.Combine(_greTxtDir, "20267163228-T001-0118915.txt");

        await File.WriteAllTextAsync(pdfPath, "pdf-content");
        await File.WriteAllLinesAsync(txtPath, BuildGreTxtLines(serie: "T001", correlativo: "0118915", motivoTraslado: "1"));

        await processor.ProcessAsync(pdfPath, CancellationToken.None);

        await using var verifyDb = CreateContext();
        var row = await verifyDb.GreInfos.SingleAsync();
        Assert.Equal("GR-0T001-0118915", row.greName);
        Assert.False(row.Auditoria_Deleted);
        Assert.Equal(MotivoTraslado.venta, row.motivoTraslado);

        var copiedPath = Path.Combine(_dirPdfs, pdfFileName);
        Assert.True(File.Exists(copiedPath));
    }

    [Fact]
    public async Task ProcessAsync_when_pdf_copy_fails_does_not_persist_in_database()
    {
        var badOutputDir = Path.Combine(_rootDir, "does-not-exist", "nested");
        var processor = CreateProcessor(CreateOptions(dirPdfs: badOutputDir));
        const string pdfFileName = "20267163228-09-T001-00118915.pdf";
        var pdfPath = Path.Combine(_rootDir, pdfFileName);
        var txtPath = Path.Combine(_greTxtDir, "20267163228-T001-0118915.txt");

        await File.WriteAllTextAsync(pdfPath, "pdf-content");
        await File.WriteAllLinesAsync(txtPath, BuildGreTxtLines(serie: "T001", correlativo: "0118915", motivoTraslado: "1"));

        await processor.ProcessAsync(pdfPath, CancellationToken.None);

        await using var verifyDb = CreateContext();
        Assert.Empty(await verifyDb.GreInfos.ToListAsync());
    }

    [Fact]
    public async Task ProcessAsync_short_circuits_when_quick_guia_already_exists()
    {
        const string pdfFileName = "20267163228-09-T001-00118915.pdf";
        var pdfPath = Path.Combine(_rootDir, pdfFileName);
        await File.WriteAllTextAsync(pdfPath, "pdf-content");

        await using (var seedDb = CreateContext())
        {
            seedDb.GreInfos.Add(new GreInfo
            {
                greName = "GR-0T001-0118915",
                delivery = "D-1",
                destinationPostCode = "150101",
                city = Departamentos.LIMA,
                stateCode = "LI",
                motivoTraslado = MotivoTraslado.venta,
                fechaInicioTraslado = DateTime.Today,
                Auditoria_CreatedAt = DateTime.Now,
                Auditoria_UpdatedAt = DateTime.Now
            });
            await seedDb.SaveChangesAsync();
        }

        var processor = CreateProcessor(CreateOptions());
        await processor.ProcessAsync(pdfPath, CancellationToken.None);

        await using var verifyDb = CreateContext();
        Assert.Single(await verifyDb.GreInfos.ToListAsync());
        Assert.False(File.Exists(Path.Combine(_dirPdfs, pdfFileName)));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootDir))
            {
                Directory.Delete(_rootDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    private GreFileProcessor CreateProcessor(GreOptions options) =>
        new(
            new TestGreDbContextFactory(() =>
                new GreDbContext(new DbContextOptionsBuilder<GreDbContext>()
                    .UseInMemoryDatabase(_dbName)
                    .Options)),
            new TestOptionsMonitor<GreOptions>(options),
            NullLogger<GreFileProcessor>.Instance,
            new GreProcessingGate());

    private GreOptions CreateOptions(string? dirPdfs = null) =>
        new()
        {
            GreTxt = _greTxtDir,
            DirPdfs = dirPdfs ?? _dirPdfs,
            DirEcommerce = _dirEcommerce,
            DirHpmps = _dirHpmps,
            FileReadyRetries = 2,
            FileReadyDelayMs = 5
        };

    private GreDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<GreDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options);

    private static string[] BuildGreTxtLines(string serie, string correlativo, string motivoTraslado) =>
    [
        $"A;Serie;1;{serie}",
        $"A;Correlativo;1;{correlativo}",
        "A;RazoTrans;1;TRANSPORTES SAC",
        "A;RUCTranspor;1;20556821438",
        "A;FechInicioTraslado;1;2026-04-27",
        "A;DirLlegUbiGeo;1;150101",
        $"A;MotivoTraslado;1;{motivoTraslado}",
        "E;DescripcionAdicsunat;1;Orden de Compra Cliente 123 | Nota de Venta SAP 456 | Documento Despacho 789 | Factura Sistema 987 | Documento Ref F001-123"
    ];

    private sealed class TestGreDbContextFactory : IDbContextFactory<GreDbContext>
    {
        private readonly Func<GreDbContext> _factory;

        public TestGreDbContextFactory(Func<GreDbContext> factory)
        {
            _factory = factory;
        }

        public GreDbContext CreateDbContext() => _factory();

        public Task<GreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_factory());
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
