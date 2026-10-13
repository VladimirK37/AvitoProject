using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;
using AvitoMonitor.Storage;

namespace AvitoMonitor.Workflows;

/// <summary>Прогресс сканирования одной страницы — для вывода в консоль.</summary>
public sealed record ScanProgress(string SourceLabel, int Page, int ItemsOnPage, int Added, int TotalCollected, int? TotalOnAvito);

/// <summary>
/// Режим «Сканировать»: листает выдачу страница за страницей и собирает ВСЕ текущие объявления.
/// По ним потом считается медиана — «рыночная цена», с которой сравниваются новые объявления.
///
/// После каждой страницы прогресс сохраняется. Если остановить сбор (Ctrl+C) или
/// Авито прервёт работу, «Дособрать» продолжит с того же места.
/// </summary>
public sealed class ScanWorkflow(AvitoBrowser browser, SearchRepository repository, AppSettings settings)
{
    public async Task RunAsync(SavedSearch search, bool restart, Action<ScanProgress> onPage, CancellationToken ct)
    {
        var listings = repository.LoadListings(search.Id);

        foreach (var source in search.Sources)
        {
            if (restart)
            {
                source.NextPageToScan = 1;
                source.ScanCompleted = false;
            }

            var previousPageIds = new HashSet<string>();

            while (!source.ScanCompleted)
            {
                ct.ThrowIfCancellationRequested();

                var url = AvitoUrl.WithPage(new Uri(source.Url), source.NextPageToScan);
                var page = await browser.LoadSearchPageAsync(url, ct);
                if (page.TotalCount is not null)
                    source.TotalCountOnAvito = page.TotalCount;

                var added = ListingMerger.Merge(listings, page.Items, source.Label, DateTime.Now);
                onPage(new ScanProgress(source.Label, source.NextPageToScan, page.Items.Count, added, listings.Count, source.TotalCountOnAvito));

                // Если Авито повторил ту же страницу — значит, страницы закончились
                var pageIds = page.Items.Select(item => item.Id).ToHashSet();
                var repeatedPage = pageIds.Count > 0 && pageIds.SetEquals(previousPageIds);
                previousPageIds = pageIds;

                source.NextPageToScan++;
                source.ScanCompleted = page.Items.Count == 0
                                       || page.HasNextPage == false
                                       || repeatedPage
                                       || source.NextPageToScan > settings.MaxPagesPerScan;

                repository.SaveListings(search.Id, listings);
                repository.Save(search);
            }
        }

        search.LastScanAt = DateTime.Now;
        repository.Save(search);
    }
}
