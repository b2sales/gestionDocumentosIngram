using Microsoft.Extensions.DependencyInjection;

namespace GestionDocumentos.Idoc;

public static class IdocServiceCollectionExtensions
{
    public static IServiceCollection AddIdocPipeline(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureIdocOptions>();

        services.AddSingleton<IdocBackOfficePaths>();

        // Registros con IOptionsMonitor para que cambios de Parametros.json (connection strings)
        // se tomen en cada operación sin reiniciar el servicio.
        services.AddSingleton<BackOfficeParameterReader>();
        services.AddSingleton<IdocRepository>();

        services.AddSingleton<IdocFileProcessor>();
        return services;
    }
}
