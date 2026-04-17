using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GestionDocumentos.Gre;
using GestionDocumentos.Idoc;

namespace GestionDocumentos.Host;

/// <summary>
/// Ejecuta periódicamente un barrido de carpetas de origen para reprocesar archivos
/// (p. ej. perdidos por el <see cref="System.IO.FileSystemWatcher"/>). Los procesadores son idempotentes.
/// </summary>
public sealed class DailyReconciliationHostedService : BackgroundService
{
    private readonly GreFileProcessor _greProcessor;
    private readonly IdocFileProcessor _idocProcessor;
    private readonly GreReconciliationLookup _greReconciliationLookup;
    private readonly IdocRepository _idocRepository;
    private readonly IOptionsMonitor<ReconciliationOptions> _reconcileOptions;
    private readonly IOptionsMonitor<GreOptions> _greOptions;
    private readonly IOptionsMonitor<IdocOptions> _idocOptions;
    private readonly IdocBackOfficePaths _idocPaths;
    private readonly BackOfficeParameterReader _backOfficeReader;
    private readonly ILogger<DailyReconciliationHostedService> _logger;

    public DailyReconciliationHostedService(
        GreFileProcessor greProcessor,
        IdocFileProcessor idocProcessor,
        GreReconciliationLookup greReconciliationLookup,
        IdocRepository idocRepository,
        IOptionsMonitor<ReconciliationOptions> reconcileOptions,
        IOptionsMonitor<GreOptions> greOptions,
        IOptionsMonitor<IdocOptions> idocOptions,
        IdocBackOfficePaths idocPaths,
        BackOfficeParameterReader backOfficeReader,
        ILogger<DailyReconciliationHostedService> logger)
    {
        _greProcessor = greProcessor;
        _idocProcessor = idocProcessor;
        _greReconciliationLookup = greReconciliationLookup;
        _idocRepository = idocRepository;
        _reconcileOptions = reconcileOptions;
        _greOptions = greOptions;
        _idocOptions = idocOptions;
        _idocPaths = idocPaths;
        _backOfficeReader = backOfficeReader;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = _reconcileOptions.CurrentValue;
            if (!opts.Enabled)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                continue;
            }

            if (!TryParseLocalTime(opts.DailyTimeLocal, out var timeOfDay))
            {
                _logger.LogError(
                    "Conciliación: hora inválida en DailyTimeLocal '{Value}'. Use HH:mm (24 h).",
                    opts.DailyTimeLocal);
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                continue;
            }

            var nextLocal = GetNextLocalRunTime(timeOfDay);
            var delay = nextLocal - DateTime.Now;
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            _logger.LogInformation(
                "Conciliación diaria programada para {NextLocal:yyyy-MM-dd HH:mm} (hora local), en {Delay}",
                nextLocal,
                delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await RunReconciliationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conciliación diaria falló. El servicio seguirá activo y reintentará en el próximo ciclo.");
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task RunReconciliationAsync(CancellationToken cancellationToken)
    {
        var opts = _reconcileOptions.CurrentValue;
        var maxDoP = Math.Clamp(opts.MaxConcurrent, 1, 32);
        var maxFiles = Math.Clamp(opts.MaxFilesPerSource, 1, 1_000_000);

        _logger.LogInformation(
            "Inicio conciliación (solo hoy: {OnlyToday}, paralelismo: {DoP}, límite por origen: {MaxFiles}, omitir ya en BD: {SkipDb})",
            opts.OnlyTodaysFiles,
            maxDoP,
            maxFiles,
            opts.SkipAlreadyInDatabase);

        if (opts.GreEnabled)
        {
            try
            {
                await ReconcileGreAsync(
                    opts.OnlyTodaysFiles,
                    opts.SkipAlreadyInDatabase,
                    maxDoP,
                    maxFiles,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conciliación GRE falló; se continuará con el resto de pipelines.");
            }
        }

        if (opts.IdocEnabled)
        {
            try
            {
                await ReconcileIdocAsync(
                    opts.OnlyTodaysFiles,
                    opts.SkipAlreadyInDatabase,
                    maxDoP,
                    maxFiles,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conciliación IDOC falló; se continuará con el resto de pipelines.");
            }
        }

        _logger.LogInformation("Fin conciliación diaria.");
    }

    private async Task ReconcileGreAsync(
        bool onlyToday,
        bool skipAlreadyInDatabase,
        int maxDoP,
        int maxFiles,
        CancellationToken cancellationToken)
    {
        var folder = _greOptions.CurrentValue.GrePdf;
        if (string.IsNullOrWhiteSpace(folder))
        {
            _logger.LogWarning("Conciliación GRE omitida: grePDF vacío.");
            return;
        }

        if (!Directory.Exists(folder))
        {
            _logger.LogWarning("Conciliación GRE omitida: carpeta no existe: {Path}", folder);
            return;
        }

        var candidates = GetCandidateFiles(folder, "*.pdf", onlyToday, maxFiles);

        _logger.LogInformation(
            "Conciliación GRE: hasta {MaxFiles} PDF(s) candidatos en {Path}",
            maxFiles,
            folder);

        IEnumerable<string> toProcess = candidates;
        if (skipAlreadyInDatabase)
        {
            var keyed = new List<(string Path, string GreName)>();
            var needsFullParse = new List<string>();
            foreach (var path in candidates)
            {
                var fn = Path.GetFileName(path);
                if (GrePathUtility.TryGetGreNameFromPdfFileName(fn, out var greName))
                {
                    keyed.Add((path, greName));
                }
                else
                {
                    needsFullParse.Add(path);
                }
            }

            var distinctNames = keyed.Select(k => k.GreName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var existing = await _greReconciliationLookup.GetExistingGreNamesAsync(distinctNames, cancellationToken);
            var pendingKeyed = keyed.Where(k => !existing.Contains(k.GreName)).Select(k => k.Path).ToList();
            toProcess = pendingKeyed.Concat(needsFullParse);

            _logger.LogInformation(
                "Conciliación GRE: candidatos={Total}, ya en BD={Skipped}, sin greName desde nombre={NoKey}, a procesar={Pending}",
                candidates.Count,
                keyed.Count - pendingKeyed.Count,
                needsFullParse.Count,
                pendingKeyed.Count + needsFullParse.Count);
        }

        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDoP,
            CancellationToken = cancellationToken
        };

        var processed = 0;
        await Parallel.ForEachAsync(toProcess, parallel, async (path, token) =>
        {
            await _greProcessor.ProcessAsync(path, token);
            Interlocked.Increment(ref processed);
        });

        _logger.LogInformation("Conciliación GRE: invocados {Processed} PDF(s) al procesador.", processed);
    }

    private async Task ReconcileIdocAsync(
        bool onlyToday,
        bool skipAlreadyInDatabase,
        int maxDoP,
        int maxFiles,
        CancellationToken cancellationToken)
    {
        var (folder, resolvedFromDb) = await ResolveIdocFolderAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(folder))
        {
            _logger.LogWarning("Conciliación IDOC omitida: no se pudo resolver carpeta de vigilancia.");
            return;
        }

        folder = NormalizeWatchPath(folder);
        _idocPaths.Apply(folder, folder, resolvedFromDb || _idocPaths.ResolvedFromDatabase);

        if (!Directory.Exists(folder))
        {
            _logger.LogWarning("Conciliación IDOC omitida: carpeta no existe: {Path}", folder);
            return;
        }

        var candidates = GetCandidateFiles(folder, "*.xml", onlyToday, maxFiles);

        _logger.LogInformation(
            "Conciliación IDOC: hasta {MaxFiles} XML(s) candidatos en {Path}",
            maxFiles,
            folder);

        IEnumerable<string> toProcess = candidates;
        if (skipAlreadyInDatabase && !string.IsNullOrWhiteSpace(_idocPaths.TibcoRoot))
        {
            var mapped = candidates
                .Select(p => (Path: p, Rel: _idocPaths.ToArchivoTibcoRelative(p)))
                .ToList();
            var distinctRel = mapped.Select(m => m.Rel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var existing = await _idocRepository.GetExistingNameFilesAsync(distinctRel, cancellationToken);
            var pendingPaths = mapped.Where(m => !existing.Contains(m.Rel)).Select(m => m.Path).ToList();
            toProcess = pendingPaths;

            _logger.LogInformation(
                "Conciliación IDOC: candidatos={Total}, ya en BD={Skipped}, a procesar={Pending}",
                candidates.Count,
                candidates.Count - pendingPaths.Count,
                pendingPaths.Count);
        }

        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDoP,
            CancellationToken = cancellationToken
        };

        var processed = 0;
        await Parallel.ForEachAsync(toProcess, parallel, async (path, token) =>
        {
            await _idocProcessor.ProcessAsync(path, token);
            Interlocked.Increment(ref processed);
        });

        _logger.LogInformation("Conciliación IDOC: invocados {Processed} XML(s) al procesador.", processed);
    }

    private async Task<(string? Path, bool ResolvedFromDatabase)> ResolveIdocFolderAsync(
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_idocPaths.WatchFolder))
        {
            return (NormalizeWatchPath(_idocPaths.WatchFolder), _idocPaths.ResolvedFromDatabase);
        }

        var o = _idocOptions.CurrentValue;
        if (!string.IsNullOrWhiteSpace(o.BackOfficeConnectionString))
        {
            var fromDb = await _backOfficeReader.GetValorAsync("IDOC", "I2CARPETA", cancellationToken);
            return string.IsNullOrWhiteSpace(fromDb)
                ? (null, false)
                : (NormalizeWatchPath(fromDb), true);
        }

        return string.IsNullOrWhiteSpace(o.WatchFolder)
            ? (null, false)
            : (NormalizeWatchPath(o.WatchFolder), false);
    }

    private static string NormalizeWatchPath(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private bool IsFromToday(string path)
    {
        var today = DateTime.Today;
        try
        {
            var write = File.GetLastWriteTime(path);
            var create = File.GetCreationTime(path);
            return write.Date == today || create.Date == today;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo determinar si el archivo es de hoy para conciliación: {Path}", path);
            return false;
        }
    }

    private List<string> GetCandidateFiles(string folder, string pattern, bool onlyToday, int maxFiles)
    {
        var selected = Directory
            .EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly)
            .Select(path => new { Path = path, SortKey = TryGetSortKey(path) })
            .Where(x => !onlyToday || IsFromToday(x.Path))
            .OrderBy(x => x.SortKey)
            .Take(maxFiles)
            .Select(x => x.Path)
            .ToList();

        return selected;
    }

    private DateTime TryGetSortKey(string path)
    {
        try
        {
            var write = File.GetLastWriteTime(path);
            var create = File.GetCreationTime(path);
            var effective = write <= create ? write : create;
            return effective == DateTime.MinValue ? DateTime.MaxValue : effective;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo obtener metadata de fecha para conciliación: {Path}", path);
            return DateTime.MaxValue;
        }
    }

    private static bool TryParseLocalTime(string text, out TimeSpan timeOfDay)
    {
        timeOfDay = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m))
        {
            return false;
        }

        if (h is < 0 or > 23 || m is < 0 or > 59)
        {
            return false;
        }

        timeOfDay = new TimeSpan(h, m, 0);
        return true;
    }

    private static DateTime GetNextLocalRunTime(TimeSpan timeOfDay)
    {
        var now = DateTime.Now;
        var candidate = now.Date + timeOfDay;
        return now <= candidate ? candidate : candidate.AddDays(1);
    }
}
