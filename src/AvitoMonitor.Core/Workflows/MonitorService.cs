using System.Collections.Concurrent;
using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;
using AvitoMonitor.Export;
using AvitoMonitor.Storage;
using AvitoMonitor.Telegram;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Workflows;

/// <summary>Что сейчас делается по поиску.</summary>
public enum JobKind
{
    None,
    Scanning,
    Watching,
}

/// <summary>
/// Фоновые задачи: сканирование и слежение. По каждому поиску — не больше одной задачи,
/// но разные поиски могут работать одновременно (страницы Авито всё равно открываются по очереди).
///
/// Интерфейс подписывается на события и узнаёт о прогрессе, новых объявлениях и проверках «я не робот».
/// События приходят из фоновых потоков — интерфейс сам переносит их в свой поток.
/// </summary>
public sealed class MonitorService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, RunningJob> _jobs = new();
    private readonly ScanWorkflow _scan;
    private readonly WatchWorkflow _watch;
    private readonly SearchCatalog _catalog;
    private readonly TelegramNotifier _telegram;
    private readonly ReportService _reports;
    private readonly AppSettings _settings;
    private readonly ILogger<MonitorService> _logger;

    public MonitorService(ScanWorkflow scan, WatchWorkflow watch, SearchCatalog catalog, AvitoBrowser browser,
        TelegramNotifier telegram, ReportService reports, AppSettings settings, ILogger<MonitorService> logger)
    {
        _scan = scan;
        _watch = watch;
        _catalog = catalog;
        _telegram = telegram;
        _reports = reports;
        _settings = settings;
        _logger = logger;

        browser.CheckRequired += OnCheckRequired;
        browser.CheckPassed += () => CheckPassed?.Invoke();
    }

    /// <summary>По поиску началась или закончилась задача, либо изменилось время следующей проверки.</summary>
    public event Action<SavedSearch>? JobChanged;

    public event Action<SavedSearch, ScanProgress>? ScanProgressed;

    public event Action<SavedSearch, ListingAssessment>? NewListingFound;

    /// <summary>Авито просит пройти проверку «я не робот».</summary>
    public event Action<string>? CheckRequired;

    public event Action? CheckPassed;

    public JobKind GetJob(SavedSearch search) => _jobs.TryGetValue(search.Id, out var job) ? job.Kind : JobKind.None;

    /// <summary>Когда следующая проверка новых объявлений (если идёт слежение).</summary>
    public DateTime? GetNextCheckAt(SavedSearch search) => _jobs.TryGetValue(search.Id, out var job) ? job.NextCheckAt : null;

    /// <param name="restart">true — с первой страницы, false — «дособрать» с места остановки.</param>
    /// <param name="thenWatch">После сканирования сразу включить слежение.</param>
    public void StartScan(SavedSearch search, bool restart, bool thenWatch = false)
    {
        if (thenWatch)
        {
            search.WatchEnabled = true;
            _catalog.Save(search);
        }

        Start(search, JobKind.Scanning, async (job, ct) =>
        {
            await _scan.RunAsync(search, restart, progress => ScanProgressed?.Invoke(search, progress), ct);
            CreateReportSafely(search);
            if (thenWatch)
                await WatchLoopAsync(search, job, ct);
        });
    }

    public void StartWatch(SavedSearch search)
    {
        search.WatchEnabled = true;
        _catalog.Save(search);
        Start(search, JobKind.Watching, (job, ct) => WatchLoopAsync(search, job, ct));
    }

    /// <summary>Кнопка «Стоп»: останавливает задачу. Слежение выключается и не продолжится после перезапуска.</summary>
    public async Task StopAsync(SavedSearch search)
    {
        if (_jobs.TryGetValue(search.Id, out var job))
        {
            await job.Cancellation.CancelAsync();
            await job.Task;
        }

        if (search.WatchEnabled)
        {
            search.WatchEnabled = false;
            _catalog.Save(search);
        }
    }

    /// <summary>При запуске программы: продолжить слежение за поисками, у которых оно было включено.</summary>
    public void ResumeWatching()
    {
        foreach (var search in _catalog.All.Where(s => s.WatchEnabled && GetJob(s) == JobKind.None))
        {
            _logger.LogInformation("«{Search}»: продолжаю слежение после перезапуска", search.Name);
            Start(search, JobKind.Watching, (job, ct) => WatchLoopAsync(search, job, ct));
        }
    }

    /// <summary>При закрытии программы: остановить всё, но запомнить, за чем следили.</summary>
    public async Task StopAllAsync()
    {
        var jobs = _jobs.Values.ToList();
        foreach (var job in jobs)
            await job.Cancellation.CancelAsync();

        await Task.WhenAll(jobs.Select(job => job.Task));
    }

    public async ValueTask DisposeAsync() => await StopAllAsync();

    private void Start(SavedSearch search, JobKind kind, Func<RunningJob, CancellationToken, Task> work)
    {
        var job = new RunningJob { Kind = kind };
        if (!_jobs.TryAdd(search.Id, job))
            throw new InvalidOperationException($"По поиску «{search.Name}» уже идёт работа — сначала нажмите «Стоп».");

        JobChanged?.Invoke(search);

        job.Task = Task.Run(async () =>
        {
            try
            {
                await work(job, job.Cancellation.Token);
            }
            catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
            {
                _logger.LogInformation("«{Search}»: остановлено", search.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "«{Search}»: ошибка — {Message}", search.Name, ex.Message);
            }
            finally
            {
                _jobs.TryRemove(search.Id, out _);
                JobChanged?.Invoke(search);
            }
        });
    }

    private async Task WatchLoopAsync(SavedSearch search, RunningJob job, CancellationToken ct)
    {
        job.Kind = JobKind.Watching;
        JobChanged?.Invoke(search);
        _logger.LogInformation("«{Search}»: слежение включено, проверка каждые {Minutes} мин", search.Name, _settings.WatchIntervalMinutes);

        while (true)
        {
            job.NextCheckAt = null;
            JobChanged?.Invoke(search);

            try
            {
                await _watch.CheckOnceAsync(search, assessment => NewListingFound?.Invoke(search, assessment), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Одна неудачная проверка не должна останавливать слежение
                _logger.LogError(ex, "«{Search}»: проверка не удалась — {Message}", search.Name, ex.Message);
            }

            job.NextCheckAt = DateTime.Now.AddMinutes(_settings.WatchIntervalMinutes);
            JobChanged?.Invoke(search);
            _logger.LogInformation("«{Search}»: следующая проверка в {Time:HH:mm}", search.Name, job.NextCheckAt);

            await Task.Delay(job.NextCheckAt.Value - DateTime.Now, ct);
        }
    }

    /// <summary>После каждого сбора — Excel-отчёт и запись о нём в базе. Ошибка отчёта не мешает слежению.</summary>
    private void CreateReportSafely(SavedSearch search)
    {
        try
        {
            _reports.CreateReport(search);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "«{Search}»: не удалось сохранить отчёт — {Message}", search.Name, ex.Message);
        }
    }

    private void OnCheckRequired(string message)
    {
        CheckRequired?.Invoke(message);
        _ = _telegram.TrySendAsync("⚠️ " + TelegramMessageFormatter.Escape(message), CancellationToken.None);
    }

    private sealed class RunningJob
    {
        public JobKind Kind { get; set; }

        public DateTime? NextCheckAt { get; set; }

        public CancellationTokenSource Cancellation { get; } = new();

        public Task Task { get; set; } = Task.CompletedTask;
    }
}
