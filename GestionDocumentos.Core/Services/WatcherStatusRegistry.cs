using System.Collections.Concurrent;

namespace GestionDocumentos.Core.Services;

/// <summary>
/// Registro en memoria de watchers vivos para que el <c>HeartbeatHostedService</c> pueda
/// reportar estado. Se accede siempre desde providers lambda para capturar el estado fresco
/// del engine sin mantener referencia cíclica al hosted service.
/// </summary>
public sealed class WatcherStatusRegistry
{
    private readonly ConcurrentDictionary<string, Func<WatcherSnapshot>> _providers =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, Func<WatcherSnapshot> provider) =>
        _providers[name] = provider;

    public void Unregister(string name) =>
        _providers.TryRemove(name, out _);

    public IReadOnlyList<(string Name, WatcherSnapshot Snapshot)> SnapshotAll()
    {
        var result = new List<(string, WatcherSnapshot)>(_providers.Count);
        foreach (var kv in _providers)
        {
            WatcherSnapshot snap;
            try
            {
                snap = kv.Value();
            }
            catch
            {
                snap = new WatcherSnapshot(false, 0, null, "provider-threw");
            }

            result.Add((kv.Key, snap));
        }

        return result;
    }
}

public readonly record struct WatcherSnapshot(
    bool IsActive,
    int PendingCount,
    DateTimeOffset? LastProcessedAtUtc,
    string? Path);
