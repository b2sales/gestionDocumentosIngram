namespace GestionDocumentos.Core.Email;

public sealed class SmtpErrorEmailOptions
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public string UserName { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>Dirección remitente (From).</summary>
    public string From { get; set; } = "";

    /// <summary>Destinatarios separados por coma, punto y coma o salto de línea.</summary>
    public string To { get; set; } = "";

    /// <summary>TLS (STARTTLS en puerto típico 587 o SSL implícito según servidor).</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>Prefijo del asunto del correo.</summary>
    public string SubjectPrefix { get; set; } = "[GestionDocumentos] Error";

    /// <summary>Segundos mínimos entre correos (evita inundar ante muchos errores).</summary>
    public int ThrottleSeconds { get; set; } = 120;

    /// <summary>
    /// Ventana (segundos) durante la cual se agregan errores posteriores al primero para mandarlos
    /// en un solo correo-resumen. Si es 0, se envía un correo por item (compatibilidad vieja).
    /// </summary>
    public int AggregationWindowSeconds { get; set; } = 60;

    /// <summary>Cantidad máxima de errores agregados por correo-resumen.</summary>
    public int MaxBatchSize { get; set; } = 50;
}
