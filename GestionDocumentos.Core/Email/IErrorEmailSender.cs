namespace GestionDocumentos.Core.Email;

public interface IErrorEmailSender
{
    Task SendBatchAsync(IReadOnlyList<ErrorEmailItem> items, CancellationToken cancellationToken);
}
