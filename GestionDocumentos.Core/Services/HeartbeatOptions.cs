namespace GestionDocumentos.Core.Services;

public sealed class HeartbeatOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 15;
}
