using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Idoc;

public sealed class ConfigureIdocOptions : IConfigureOptions<IdocOptions>
{
    private readonly IConfiguration _configuration;

    public ConfigureIdocOptions(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(IdocOptions options)
    {
        var useTest = bool.TryParse(_configuration["environmentTest"], out var testFlag) && testFlag;

        options.BackOfficeConnectionString = useTest
            ? _configuration["backOfficeContextTest"] ?? _configuration["backOfficeContext"] ?? ""
            : _configuration["backOfficeContext"] ?? "";

        var folder = useTest
            ? _configuration["idocFolderTest"] ?? ""
            : _configuration["idocFolder"] ?? "";

        options.WatchFolder = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootOverride = useTest
            ? _configuration["idocTibcoRootTest"] ?? _configuration["idocTibcoRoot"]
            : _configuration["idocTibcoRoot"] ?? _configuration["idocTibcoRootTest"];
        if (!string.IsNullOrWhiteSpace(rootOverride))
        {
            options.TibcoRoot = rootOverride.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        else
        {
            options.TibcoRoot = options.WatchFolder;
        }

        options.ConnectionString = _configuration["idocContext"] ?? "";

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
