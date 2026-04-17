using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GestionDocumentos.Core.Abstractions;

namespace GestionDocumentos.Idoc;

public sealed class IdocFileProcessor : IFileProcessor
{
    private readonly IdocRepository _repository;
    private readonly IdocBackOfficePaths _paths;
    private readonly ILogger<IdocFileProcessor> _logger;

    public IdocFileProcessor(
        IdocRepository repository,
        IdocBackOfficePaths paths,
        ILogger<IdocFileProcessor> logger)
    {
        _repository = repository;
        _paths = paths;
        _logger = logger;
    }

    public async Task ProcessAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_paths.TibcoRoot))
        {
            _logger.LogWarning("Idoc TibcoRoot no configurado; se omite {File}", fullPath);
            return;
        }

        var relative = _paths.ToArchivoTibcoRelative(fullPath);

        try
        {
            if (await _repository.ExistsByNameFileAsync(relative, cancellationToken))
            {
                _logger.LogInformation("IDOC ya registrado: {File}", relative);
                return;
            }

            var xml = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var documento = IdocXmlParser.Parse(xml);
            documento.ArchivoTibco = relative;

            var result = await _repository.TryInsertDocumentAsync(documento, cancellationToken);
            if (!result.WasInserted)
            {
                _logger.LogInformation("IDOC ya registrado: {File}", documento.ArchivoTibco);
                return;
            }

            _logger.LogInformation(
                "IDOC registrado {Serie}-{Numero} filas={Rows} archivo={File}",
                documento.Serie,
                documento.Numero,
                result.RowsAffected,
                documento.ArchivoTibco);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando IDOC {File}", fullPath);
        }
    }
}
