using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Gre;

public static class GreServiceCollectionExtensions
{
    public static IServiceCollection AddGrePipeline(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureGreOptions>();

        services.AddDbContextFactory<GreDbContext>((sp, builder) =>
        {
            var cs = sp.GetRequiredService<IOptions<GreOptions>>().Value.ConnectionString;
            builder.UseSqlServer(cs, sql =>
            {
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            });
        });

        services.AddSingleton<GreReconciliationLookup>();
        services.AddSingleton<GreProcessingGate>();
        services.AddSingleton<GreFileProcessor>();
        services.AddSingleton<GreTxtTriggerProcessor>();
        return services;
    }
}
