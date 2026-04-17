using Microsoft.Extensions.Configuration;
using GestionDocumentos.Core.Abstractions;

namespace GestionDocumentos.Core.Services;

public sealed class JsonParameterProvider(IConfiguration configuration) : IParameterProvider
{
    public string? GetValue(string key) => configuration[key];

    public Task<string?> GetDbValueAsync(string table, string key, CancellationToken cancellationToken)
    {
        var value = configuration[$"DbParameters:{table}:{key}"];
        return Task.FromResult<string?>(value);
    }
}
