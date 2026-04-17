namespace GestionDocumentos.Core.Abstractions;

public interface IFileProcessor
{
    Task ProcessAsync(string fullPath, CancellationToken cancellationToken);
}
