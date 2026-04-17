using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionDocumentos.Core.Email;

public sealed class SmtpErrorEmailSender
{
    private readonly IOptionsMonitor<SmtpErrorEmailOptions> _options;
    private readonly ILogger<SmtpErrorEmailSender> _logger;

    public SmtpErrorEmailSender(IOptionsMonitor<SmtpErrorEmailOptions> options, ILogger<SmtpErrorEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(ErrorEmailItem item, CancellationToken cancellationToken)
    {
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

        var subject = $"{o.SubjectPrefix} — {item.Category}";
        var body =
            $"Nivel: {item.Level}\r\n" +
            $"Categoría: {item.Category}\r\n" +
            $"Hora (UTC): {item.Timestamp:O}\r\n\r\n" +
            $"{item.Message}\r\n";

        if (!string.IsNullOrEmpty(item.ExceptionDetail))
        {
            body += "\r\n--- Excepción ---\r\n" + item.ExceptionDetail;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(o.From),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };

        foreach (var to in recipients)
        {
            message.To.Add(to);
        }

        using var client = new SmtpClient(o.Host, o.Port)
        {
            EnableSsl = o.UseTls,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (string.IsNullOrEmpty(o.UserName))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = null;
        }
        else
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(o.UserName, o.Password);
        }

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo enviar correo de error SMTP a {Host}:{Port}", o.Host, o.Port);
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
