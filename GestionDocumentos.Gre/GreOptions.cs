namespace GestionDocumentos.Gre;

public sealed class GreOptions
{
    public string GrePdf { get; set; } = "";
    public string GreTxt { get; set; } = "";
    public string DirPdfs { get; set; } = "";
    public string DirEcommerce { get; set; } = "";
    public string DirHpmps { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public int ProcessingConcurrency { get; set; } = 6;
    public int QueueCapacity { get; set; } = 2000;
    public int FileReadyRetries { get; set; } = 10;
    public int FileReadyDelayMs { get; set; } = 500;

    /// <summary>Buffer de <see cref="System.IO.FileSystemWatcher"/> (bytes).</summary>
    public int WatcherInternalBufferSize { get; set; } = 65536;
}
