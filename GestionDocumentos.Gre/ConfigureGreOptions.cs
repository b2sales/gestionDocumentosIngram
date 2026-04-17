using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Gre;

public sealed class ConfigureGreOptions : IConfigureOptions<GreOptions>
{
    private readonly IConfiguration _configuration;

    public ConfigureGreOptions(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(GreOptions options)
    {
        var useTest = bool.TryParse(_configuration["environmentTest"], out var testFlag) && testFlag;

        string Pick(string prodKey, string testKey) =>
            useTest
                ? _configuration[testKey] ?? ""
                : _configuration[prodKey] ?? "";

        options.GrePdf = Pick("grePDF", "grePDFTest");
        options.GreTxt = Pick("greTXT", "greTXTTest");
        options.DirPdfs = Pick("dirPDFs", "dirPDFsTest");
        options.DirEcommerce = Pick("dirEcommerce", "dirEcommerceTest");
        options.DirHpmps = Pick("dirHpmps", "dirHpmpsTest");
        options.ConnectionString = _configuration["greContext"] ?? "";

        if (int.TryParse(_configuration["processingConcurrency"], out var conc))
        {
            options.ProcessingConcurrency = conc;
        }

        if (int.TryParse(_configuration["queueCapacity"], out var q))
        {
            options.QueueCapacity = q;
        }

        if (int.TryParse(_configuration["fileReadyRetries"], out var r))
        {
            options.FileReadyRetries = r;
        }

        if (int.TryParse(_configuration["fileReadyDelayMs"], out var d))
        {
            options.FileReadyDelayMs = d;
        }

        if (int.TryParse(_configuration["watcherInternalBufferSize"], out var buf))
        {
            options.WatcherInternalBufferSize = Math.Clamp(buf, 4096, 1_048_576);
        }
    }
}
