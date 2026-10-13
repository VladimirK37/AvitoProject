using AvitoMonitor.Analysis;
using AvitoMonitor.Domain;
using AvitoMonitor.Storage;

namespace AvitoMonitor.Export;

/// <summary>Анализ сохранённого поиска целиком: для сводки в консоли и для Excel.</summary>
public sealed class ReportService(SearchRepository repository, SearchAnalyzer analyzer, ExcelReportBuilder excel)
{
    public (SearchAnalysis Analysis, IReadOnlyList<ListingAssessment> Rows) Analyze(SavedSearch search)
    {
        var listings = repository.LoadListings(search.Id).Values.ToList();
        var analysis = analyzer.Analyze(search, listings);

        // Сортировка: по группе, внутри группы — самые выгодные сверху
        var rows = GroupKeys.OrderByGroup(listings.Select(analysis.Evaluate), a => a.GroupKey)
            .ThenBy(a => a.DeviationPercent ?? double.MaxValue)
            .ToList();

        return (analysis, rows);
    }

    /// <summary>Строит Excel-файл и возвращает путь к нему.</summary>
    public string BuildExcel(SavedSearch search)
    {
        var (analysis, rows) = Analyze(search);
        var path = repository.GetReportPath(search);

        try
        {
            excel.Build(analysis, rows, path);
        }
        catch (IOException)
        {
            // Старый отчёт открыт в Excel и заблокирован — сохраняем рядом под другим именем
            path = Path.Combine(Path.GetDirectoryName(path)!, $"report-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");
            excel.Build(analysis, rows, path);
        }

        return path;
    }
}
