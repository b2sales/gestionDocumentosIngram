using System.Collections.Concurrent;

namespace GestionDocumentos.Gre;

public sealed class GreProcessingGate
{
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public bool TryEnter(string pdfPath) =>
        _inFlight.TryAdd(Normalize(pdfPath), 0);

    public void Exit(string pdfPath) =>
        _inFlight.TryRemove(Normalize(pdfPath), out _);

    private static string Normalize(string path) =>
        Path.GetFullPath(path);
}
