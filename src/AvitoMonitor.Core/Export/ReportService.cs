using AvitoMonitor.Analysis;
using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;
using AvitoMonitor.Persistence;
using AvitoMonitor.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Export;

/// <summary>
/// Анализ поиска целиком (для окна) и Excel-отчёты.
/// Каждый отчёт — отдельный файл в папке reports с датой в имени, а запись о нём
/// (поиск, что искали, сколько объявлений, сводка цен, путь к файлу) — в таблице reports в PostgreSQL.
/// </summary>
public sealed class ReportService(
    ISearchStore store,
    SearchAnalyzer analyzer,
    ExcelReportBuilder excel,
    IDbContextFactory<AvitoDbContext> dbFactory,
    AppPaths paths,
    ILogger<ReportService> logger)
{
    /// <summary>Появился новый отчёт (событие может прийти из фонового потока).</summary>
    public event Action<ReportRecord>? ReportCreated;

    public (SearchAnalysis Analysis, IReadOnlyList<ListingAssessment> Rows) Analyze(SavedSearch search)
    {
        var listings = store.LoadListings(search.Id).Values.ToList();
        var analysis = analyzer.Analyze(search, listings);

        // Сортировка: по группе, внутри группы — самые выгодные сверху
        var rows = GroupKeys.OrderByGroup(listings.Select(analysis.Evaluate), a => a.GroupKey)
            .ThenBy(a => a.DeviationPercent ?? double.MaxValue)
            .ToList();

        return (analysis, rows);
    }

    /// <summary>Построить Excel-файл и сохранить запись о нём в базу.</summary>
    public ReportRecord CreateReport(SavedSearch search)
    {
        var (analysis, rows) = Analyze(search);
        var createdAt = DateTime.Now;
        var path = Path.Combine(paths.ReportsDir, $"{SafeFileName(search.Name)} {createdAt:yyyy-MM-dd HH-mm-ss}.xlsx");
        excel.Build(analysis, rows, path);

        var entity = new ReportEntity
        {
            SearchId = search.Id,
            SearchName = search.Name,
            Description = DescribeSources(search),
            CreatedAt = EntityMapper.ToUtc(createdAt),
            ListingsCount = rows.Count,
            ExcludedCount = rows.Count(r => r.Exclusion is not null),
            Summary = Summarize(analysis),
            FilePath = path,
        };

        using (var db = dbFactory.CreateDbContext())
        {
            db.Reports.Add(entity);
            db.SaveChanges();
        }

        var record = EntityMapper.ToDomain(entity);
        logger.LogInformation("«{Search}»: отчёт сохранён — {File}", search.Name, path);
        ReportCreated?.Invoke(record);
        return record;
    }

    public IReadOnlyList<ReportRecord> GetReports(int limit = 1000)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Reports
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .AsEnumerable()
            .Select(EntityMapper.ToDomain)
            .ToList();
    }

    /// <summary>Удалить запись об отчёте (и, если нужно, сам файл).</summary>
    public void DeleteReport(ReportRecord report, bool deleteFile)
    {
        using (var db = dbFactory.CreateDbContext())
            db.Reports.Where(r => r.Id == report.Id).ExecuteDelete();

        if (deleteFile && File.Exists(report.FilePath))
            File.Delete(report.FilePath);

        logger.LogInformation("Удалён отчёт «{Search}» от {Date:dd.MM.yyyy HH:mm}", report.SearchName, report.CreatedAt);
    }

    /// <summary>Что искали: «airpods 4» · Санкт-Петербург; для старых поисков — ссылка.</summary>
    public static string DescribeSources(SavedSearch search) =>
        string.Join("; ", search.Sources.Select(source => source.IsKeywordSearch
            ? new SearchRequest(source.Query!, source.City ?? AvitoCities.DefaultSlug, source.PriceMin, source.PriceMax).Describe()
            : source.Url));

    private static string Summarize(SearchAnalysis analysis) =>
        string.Join("; ", GroupKeys.OrderByGroup(analysis.Market.Groups.Values, g => g.Key)
            .Take(8)
            .Select(g => $"{g.Key} — {TextUtils.Metric(g.Median, analysis.Market.PerSquareMeter)} ({g.Count} шт.)"));

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length switch
        {
            0 => "Поиск",
            > 60 => safe[..60],
            _ => safe,
        };
    }
}
