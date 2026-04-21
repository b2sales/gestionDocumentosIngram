using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GestionDocumentos.Core.Email;

public sealed class SmtpErrorEmailSender
{
    private readonly IOptionsMonitor<SmtpErrorEmailOptions> _options;

    public SmtpErrorEmailSender(IOptionsMonitor<SmtpErrorEmailOptions> options)
    {
        _options = options;
    }

    public Task SendAsync(ErrorEmailItem item, CancellationToken cancellationToken)
        => SendBatchAsync(new[] { item }, cancellationToken);

    /// <summary>
    /// Envía un único correo-resumen que agrupa uno o más items de error.
    /// Si <paramref name="items"/> tiene un solo elemento, el asunto/cuerpo coincide con el formato legacy.
    /// </summary>
    public async Task SendBatchAsync(IReadOnlyList<ErrorEmailItem> items, CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
        {
            return;
        }

        var o = _options.CurrentValue;
        if (!o.Enabled || string.IsNullOrWhiteSpace(o.Host) || string.IsNullOrWhiteSpace(o.From) ||
            string.IsNullOrWhiteSpace(o.To))
        {
            return;
        }

        var recipients = ParseAddresses(o.To);
        if (recipients.Count == 0)
        {
            return;
        }

        string subject;
        string body;
        if (items.Count == 1)
        {
            var item = items[0];
            subject = $"{o.SubjectPrefix} — {item.Category}";
            body =
                $"Nivel: {item.Level}\r\n" +
                $"Categoría: {item.Category}\r\n" +
                $"Hora (UTC): {item.Timestamp:O}\r\n\r\n" +
                $"{item.Message}\r\n";

            if (!string.IsNullOrEmpty(item.ExceptionDetail))
            {
                body += "\r\n--- Excepción ---\r\n" + item.ExceptionDetail;
            }
        }
        else
        {
            var categoryCount = items.GroupBy(i => i.Category).Count();
            subject = $"{o.SubjectPrefix} — {items.Count} errores en {categoryCount} categoría(s)";

            var sb = new System.Text.StringBuilder();
            sb.Append("Resumen de errores agregados: ").Append(items.Count).Append(" evento(s)\r\n");
            sb.Append("Primero (UTC): ").Append(items[0].Timestamp.ToString("O")).Append("\r\n");
            sb.Append("Último  (UTC): ").Append(items[^1].Timestamp.ToString("O")).Append("\r\n\r\n");

            sb.Append("--- Conteos por categoría ---\r\n");
            foreach (var g in items.GroupBy(i => i.Category).OrderByDescending(g => g.Count()))
            {
                sb.Append(g.Count().ToString().PadLeft(5)).Append("  ").Append(g.Key).Append("\r\n");
            }
            sb.Append("\r\n--- Detalle ---\r\n");
            var index = 1;
            foreach (var it in items)
            {
                sb.Append('[').Append(index++).Append("] ")
                    .Append(it.Timestamp.ToString("O")).Append("  ")
                    .Append(it.Level).Append("  ")
                    .Append(it.Category).Append("\r\n")
                    .Append(it.Message).Append("\r\n");
                if (!string.IsNullOrEmpty(it.ExceptionDetail))
                {
                    sb.Append(it.ExceptionDetail).Append("\r\n");
                }
                sb.Append("\r\n");
            }
            body = sb.ToString();
        }

        // MailKit es el stack recomendado por Microsoft (System.Net.Mail.SmtpClient está en mantenimiento
        // y no soporta STARTTLS moderno con precisión). Usamos MimeKit para construir el mensaje y
        // MailKit.SmtpClient para el transporte con SecureSocketOptions explícito.
        var message = new MimeMessage
        {
            Subject = subject
        };
        message.From.Add(MailboxAddress.Parse(o.From));
        foreach (var to in recipients)
        {
            message.To.Add(MailboxAddress.Parse(to));
        }
        message.Body = new TextPart("plain") { Text = body };

        try
        {
            using var client = new SmtpClient();
            var secure = o.UseTls ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None;
            await client.ConnectAsync(o.Host, o.Port, secure, cancellationToken);
            if (!string.IsNullOrEmpty(o.UserName))
            {
                await client.AuthenticateAsync(o.UserName, o.Password, cancellationToken);
            }
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // NO usar ILogger aquí: ErrorEmailLoggerProvider filtra por categoría para cortar el loop,
            // pero redundamos a Console.Error para tolerar cambios futuros de categoría.
            Console.Error.WriteLine(
                $"[SmtpErrorEmailSender] Fallo de envío a {o.Host}:{o.Port}: {ex.Message}");
        }
    }

    private static List<string> ParseAddresses(string raw)
    {
        return raw
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
