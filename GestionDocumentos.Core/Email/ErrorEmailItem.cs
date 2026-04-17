using Microsoft.Extensions.Logging;

namespace GestionDocumentos.Core.Email;

public sealed record ErrorEmailItem(
    string Category,
    LogLevel Level,
    string Message,
    string? ExceptionDetail,
    DateTimeOffset Timestamp);
