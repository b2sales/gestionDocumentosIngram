using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Idoc;

public static class IdocServiceCollectionExtensions
{
    public static IServiceCollection AddIdocPipeline(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureIdocOptions>();

        services.AddSingleton<IdocBackOfficePaths>();

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<IdocOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<BackOfficeParameterReader>>();
            return new BackOfficeParameterReader(opts.BackOfficeConnectionString, logger);
        });

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<IdocOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<IdocRepository>>();
            return new IdocRepository(opts.ConnectionString, logger);
        });

        services.AddSingleton<IdocFileProcessor>();
        return services;
    }
}
