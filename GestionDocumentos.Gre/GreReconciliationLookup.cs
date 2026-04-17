using Microsoft.EntityFrameworkCore;

namespace GestionDocumentos.Gre;

/// <summary>
/// Consultas por lote contra <see cref="GreDbContext"/> para omitir PDFs ya registrados en conciliación.
/// </summary>
public sealed class GreReconciliationLookup
{
    private const int ChunkSize = 500;
    private readonly IDbContextFactory<GreDbContext> _dbFactory;

    public GreReconciliationLookup(IDbContextFactory<GreDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<HashSet<string>> GetExistingGreNamesAsync(
        IReadOnlyCollection<string> greNames,
        CancellationToken cancellationToken)
    {
        var distinct = greNames.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (distinct.Count == 0)
        {
            return result;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        for (var i = 0; i < distinct.Count; i += ChunkSize)
        {
            var chunk = distinct.Skip(i).Take(ChunkSize).ToList();
            var found = await db.GreInfos
                .AsNoTracking()
                .Where(g => chunk.Contains(g.greName))
                .Select(g => g.greName)
                .ToListAsync(cancellationToken);
            foreach (var n in found)
            {
                result.Add(n);
            }
        }

        return result;
    }
}
