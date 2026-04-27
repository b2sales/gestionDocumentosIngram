using GestionDocumentos.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Gre;

public sealed class GreTxtTriggerProcessor : IFileProcessor
{
    private readonly GreFileProcessor _greProcessor;
    private readonly IOptionsMonitor<GreOptions> _options;
    private readonly ILogger<GreTxtTriggerProcessor> _logger;

    public GreTxtTriggerProcessor(
        GreFileProcessor greProcessor,
        IOptionsMonitor<GreOptions> options,
        ILogger<GreTxtTriggerProcessor> logger)
    {
        _greProcessor = greProcessor;
        _options = options;
        _logger = logger;
    }

    public async Task ProcessAsync(string fullPath, CancellationToken cancellationToken)
    {
        var txtFileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(txtFileName))
        {
            return;
        }

        var o = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(o.GrePdf) || !Directory.Exists(o.GrePdf))
        {
            _logger.LogWarning("GRE TXT trigger omitido: carpeta grePDF no existe: {Path}", o.GrePdf);
            return;
        }

        if (!GrePathUtility.TryGetPdfSearchPatternFromTxtFileName(txtFileName, out var pdfPattern))
        {
            _logger.LogWarning("GRE TXT trigger omitido: nombre TXT invalido: {Txt}", txtFileName);
            return;
        }

        var candidates = Directory
            .EnumerateFiles(o.GrePdf, pdfPattern, SearchOption.TopDirectoryOnly)
            .Where(pdfPath =>
                string.Equals(
                    Path.GetFileName(GrePathUtility.GetTxtPathForPdf(Path.GetFileName(pdfPath), "")),
                    txtFileName,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(File.GetLastWriteTimeUtc)
            .ToList();

        if (candidates.Count == 0)
        {
            _logger.LogInformation(
                "GRE TXT recibido sin PDF pendiente relacionado: {Txt} (patron {Pattern})",
                txtFileName,
                pdfPattern);
            return;
        }

        foreach (var pdfPath in candidates)
        {
            _logger.LogInformation("GRE TXT {Txt} dispara reproceso de PDF {Pdf}", txtFileName, Path.GetFileName(pdfPath));
            await _greProcessor.ProcessAsync(pdfPath, cancellationToken);
        }
    }
}
