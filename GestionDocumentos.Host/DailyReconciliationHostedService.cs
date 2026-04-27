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
    private int _running;

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
            try
            {
                await ScheduleAndRunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Conciliación diaria: error inesperado en el bucle principal. Reintentando en 1 minuto.");
                await SafeDelayAsync(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task ScheduleAndRunAsync(CancellationToken stoppingToken)
    {
        var opts = _reconcileOptions.CurrentValue;
        if (!opts.Enabled)
        {
            await SafeDelayAsync(TimeSpan.FromMinutes(5), stoppingToken);
            return;
        }

        if (!TryParseLocalTime(opts.DailyTimeLocal, out var timeOfDay))
        {
            _logger.LogError(
                "Conciliación: hora inválida en DailyTimeLocal '{Value}'. Use HH:mm (24 h).",
                opts.DailyTimeLocal);
            await SafeDelayAsync(TimeSpan.FromHours(1), stoppingToken);
            return;
        }

        var nextLocal = GetNextLocalRunTime(timeOfDay);
        var delay = nextLocal - DateTimeOffset.Now;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        _logger.LogInformation(
            "Conciliación diaria programada para {NextLocal:yyyy-MM-dd HH:mm zzz} (hora local), en {Delay}",
            nextLocal,
            delay);

        await Task.Delay(delay, stoppingToken);

        // Lock de instancia: si la ejecución previa aún no terminó (p. ej. corrida manual + schedule),
        // saltamos esta corrida sin esperar y reprogramamos normalmente.
        if (!TryEnterRunLock())
        {
            _logger.LogWarning("Conciliación: ya hay una ejecución en curso. Se omite esta corrida.");
            return;
        }

        try
        {
            await RunReconciliationAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conciliación diaria falló. El servicio seguirá activo y reintentará en el próximo ciclo.");
            await SafeDelayAsync(TimeSpan.FromMinutes(1), stoppingToken);
        }
        finally
        {
            ExitRunLock();
        }
    }

    private bool TryEnterRunLock() =>
        Interlocked.CompareExchange(ref _running, 1, 0) == 0;

    private void ExitRunLock() =>
        Interlocked.Exchange(ref _running, 0);

    private static async Task SafeDelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
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
        var failed = 0;
        await Parallel.ForEachAsync(toProcess, parallel, async (path, token) =>
        {
            try
            {
                await _greProcessor.ProcessAsync(path, token);
                Interlocked.Increment(ref processed);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                _logger.LogError(ex, "Conciliación GRE: falló procesamiento de {Path}; se continúa con el resto.", path);
            }
        });

        _logger.LogInformation(
            "Conciliación GRE: {Processed} PDF(s) procesados, {Failed} fallidos.",
            processed,
            failed);
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
        var failed = 0;
        await Parallel.ForEachAsync(toProcess, parallel, async (path, token) =>
        {
            try
            {
                await _idocProcessor.ProcessAsync(path, token);
                Interlocked.Increment(ref processed);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                _logger.LogError(ex, "Conciliación IDOC: falló procesamiento de {Path}; se continúa con el resto.", path);
            }
        });

        _logger.LogInformation(
            "Conciliación IDOC: {Processed} XML(s) procesados, {Failed} fallidos.",
            processed,
            failed);
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

    /// <summary>
    /// Enumera archivos en <paramref name="folder"/> usando un único <see cref="FileInfo"/> por archivo.
    /// <see cref="DirectoryInfo.EnumerateFiles(string,SearchOption)"/> devuelve <see cref="FileInfo"/>
    /// con metadata ya cacheada por el SO, evitando llamadas adicionales a <c>File.GetLastWriteTime</c>/
    /// <c>GetCreationTime</c> que son especialmente costosas sobre UNC/SMB.
    /// </summary>
    private List<string> GetCandidateFiles(string folder, string pattern, bool onlyToday, int maxFiles)
    {
        DirectoryInfo dir;
        try
        {
            dir = new DirectoryInfo(folder);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conciliación: no se pudo abrir carpeta {Path}", folder);
            return new List<string>();
        }

        var today = DateTime.Today;
        var selected = dir
            .EnumerateFiles(pattern, SearchOption.TopDirectoryOnly)
            .Select(fi => new { Info = fi, SortKey = TryGetSortKey(fi) })
            .Where(x => !onlyToday || IsFromToday(x.Info, today))
            .OrderBy(x => x.SortKey)
            .Take(maxFiles)
            .Select(x => x.Info.FullName)
            .ToList();

        return selected;
    }

    private bool IsFromToday(FileInfo fi, DateTime today)
    {
        try
        {
            return fi.LastWriteTime.Date == today || fi.CreationTime.Date == today;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo determinar si el archivo es de hoy para conciliación: {Path}", fi.FullName);
            return false;
        }
    }

    private DateTime TryGetSortKey(FileInfo fi)
    {
        try
        {
            var write = fi.LastWriteTime;
            var create = fi.CreationTime;
            var effective = write <= create ? write : create;
            return effective == DateTime.MinValue ? DateTime.MaxValue : effective;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo obtener metadata de fecha para conciliación: {Path}", fi.FullName);
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

    /// <summary>
    /// Devuelve el próximo <see cref="DateTimeOffset"/> local a <paramref name="timeOfDay"/>.
    /// Usamos <see cref="DateTimeOffset"/> para que los saltos de DST (horario de verano) no
    /// provoquen corridas duplicadas o perdidas respecto a la versión basada en <see cref="DateTime"/>.
    /// </summary>
    private static DateTimeOffset GetNextLocalRunTime(TimeSpan timeOfDay)
    {
        var now = DateTimeOffset.Now;
        var candidate = new DateTimeOffset(now.Year, now.Month, now.Day, timeOfDay.Hours, timeOfDay.Minutes, 0, now.Offset);
        return now <= candidate ? candidate : candidate.AddDays(1);
    }
}
