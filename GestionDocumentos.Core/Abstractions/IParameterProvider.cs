namespace GestionDocumentos.Core.Abstractions;

public interface IParameterProvider
{
    string? GetValue(string key);
    Task<string?> GetDbValueAsync(string table, string key, CancellationToken cancellationToken);
}
