using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GestionDocumentos.Core.Abstractions;

namespace GestionDocumentos.Gre;

public sealed class GreFileProcessor : IFileProcessor
{
    private readonly IDbContextFactory<GreDbContext> _dbFactory;
    private readonly IOptions<GreOptions> _options;
    private readonly ILogger<GreFileProcessor> _logger;

    public GreFileProcessor(
        IDbContextFactory<GreDbContext> dbFactory,
        IOptions<GreOptions> options,
        ILogger<GreFileProcessor> logger)
    {
        _dbFactory = dbFactory;
        _options = options;
        _logger = logger;
    }

    public async Task ProcessAsync(string fullPath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var o = _options.Value;
        var guia = "";
        try
        {
            if (GrePathUtility.TryGetGreNameFromPdfFileName(fileName, out var quickGuia))
            {
                await using var dbQuick = await _dbFactory.CreateDbContextAsync(cancellationToken);
                if (await dbQuick.GreInfos.AnyAsync(d => d.greName == quickGuia, cancellationToken))
                {
                    _logger.LogInformation("GRE: {Guia} - Ya registrado", quickGuia);
                    return;
                }
            }

            var txtPath = GrePathUtility.GetTxtPathForPdf(fileName, o.GreTxt);
            if (!await WaitUntilFileReadyAsync(
                    txtPath,
                    o.FileReadyRetries,
                    o.FileReadyDelayMs,
                    requireExclusiveLock: true,
                    cancellationToken))
            {
                _logger.LogWarning("TXT no disponible para {Pdf}. Esperado: {Txt}", fileName, txtPath);
                return;
            }

            var linesGre = await File.ReadAllLinesAsync(txtPath, cancellationToken);
            var data = GreParser.ParseLines(linesGre);

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var serie = data.GetAttributes("Serie");
            var correlativo = data.GetAttributes("Correlativo");
            guia = $"GR-0{serie}-{correlativo}";

            if (await db.GreInfos.AnyAsync(d => d.greName == guia, cancellationToken))
            {
                _logger.LogInformation("GRE: {Guia} - Ya registrado", guia);
                return;
            }

            var razoTrans = data.GetAttributes("RazoTrans");
            var rucTranspor = data.GetAttributes("RUCTranspor");
            var fechaInicioTraslado = data.GetAttributes("FechInicioTraslado");
            var dirLlegUbiGeo = data.GetAttributes("DirLlegUbiGeo");
            var mt = data.GetAttributes("MotivoTraslado");

            if (!int.TryParse(mt, out var motivoInt))
            {
                _logger.LogWarning("Motivo traslado invalido para {Guia}. Valor: {Mt}", guia, mt);
                return;
            }

            var motivoTraslado = (MotivoTraslado)motivoInt;
            var ordenCompra = data.GetValue("Orden de Compra Cliente");
            var notaVenta = data.GetValue("Nota de Venta SAP");
            var delivery = data.GetValue("Documento Despacho");
            var facturaSap = data.GetValue("Factura Sistema");
            var facturaSunat = data.GetValue("Documento Ref");

            if (string.IsNullOrWhiteSpace(dirLlegUbiGeo) || dirLlegUbiGeo.Length < 2 ||
                !int.TryParse(dirLlegUbiGeo[..2], out var cityCode))
            {
                _logger.LogWarning("DirLlegUbiGeo invalido para {Guia}. Valor: {Dir}", guia, dirLlegUbiGeo);
                return;
            }

            var city = (Departamentos)cityCode;
            var stateCode = ((StateCode)city).ToString();

            var anularPendientes = await db.GreInfos
                .Where(d => d.delivery == delivery && d.Auditoria_Deleted == false)
                .ToListAsync(cancellationToken);

            foreach (var row in anularPendientes)
            {
                row.Auditoria_Deleted = true;
                row.Auditoria_DeletedAt = DateTime.Now;
                row.Auditoria_UpdatedAt = DateTime.Now;
            }

            if (!DateTime.TryParse(fechaInicioTraslado, out var fechaInicio))
            {
                _logger.LogWarning("FechInicioTraslado invalido para {Guia}. Valor: {Fecha}", guia, fechaInicioTraslado);
                return;
            }

            var nuevaGre = new GreInfo
            {
                greName = guia,
                ordenCompra = ordenCompra,
                notaVenta = notaVenta,
                delivery = delivery,
                facturaSAP = facturaSap,
                facturaSUNAT = facturaSunat,
                rucTranspor = rucTranspor,
                razoTrans = razoTrans,
                motivoTraslado = motivoTraslado,
                fechaInicioTraslado = fechaInicio,
                city = city,
                stateCode = stateCode,
                destinationPostCode = dirLlegUbiGeo,
                Auditoria_CreatedAt = DateTime.Now,
                Auditoria_UpdatedAt = DateTime.Now,
                Auditoria_Deleted = false
            };

            db.GreInfos.Add(nuevaGre);

            var dirKey = "dirPDFs";
            if (motivoTraslado == MotivoTraslado.ventaTerceros)
            {
                dirKey = serie == "T004" ? "dirHpmps" : "dirEcommerce";
            }

            if (!await TryCopyPdfAsync(fullPath, dirKey, o, cancellationToken))
            {
                _logger.LogWarning(
                    "GRE: {Guia} - Copia PDF fallida; no se persiste en BD (reintento posible).",
                    guia);
                return;
            }

            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("GRE: {Guia} - Motivo: {Motivo}\t{Err}", guia, motivoTraslado, data.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en guia {Guia}", guia);
        }
    }

    private async Task<bool> TryCopyPdfAsync(
        string filePath,
        string folderKey,
        GreOptions o,
        CancellationToken cancellationToken)
    {
        var pathTo = Path.GetFileName(filePath);
        var destinationDirectory = folderKey switch
        {
            "dirEcommerce" => o.DirEcommerce,
            "dirHpmps" => o.DirHpmps,
            _ => o.DirPdfs
        };

        var copyTo = Path.Combine(destinationDirectory, pathTo ?? "");
        const int bufferSizeInBytes = 500 * 1024;

        try
        {
            await using var inputFile = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: bufferSizeInBytes,
                useAsync: true);
            await using var outputFile = new FileStream(
                copyTo,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: bufferSizeInBytes,
                useAsync: true);
            var buffer = new byte[bufferSizeInBytes];
            int bytesRead;

            while ((bytesRead = await inputFile.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await outputFile.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copiado de PDF hacia {Dest}", copyTo);
            return false;
        }
    }

    private static async Task<bool> WaitUntilFileReadyAsync(
        string filePath,
        int retries,
        int delayMs,
        bool requireExclusiveLock,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < retries; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(filePath))
                {
                    await using var stream = new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        requireExclusiveLock ? FileShare.None : FileShare.ReadWrite,
                        bufferSize: 4096,
                        FileOptions.Asynchronous);
                    if (stream.Length > 0)
                    {
                        return true;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        return false;
    }
}
