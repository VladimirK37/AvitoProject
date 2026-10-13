using AvitoMonitor.Analysis;
using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;
using AvitoMonitor.Storage;
using AvitoMonitor.Telegram;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Workflows;

/// <summary>
/// Режим «Следить», одна проверка: открывает выдачу, отсортированную по дате,
/// находит объявления, которых раньше не было, оценивает их и присылает в Telegram.
/// Повторять проверку раз в N минут — задача MonitorService.
/// </summary>
public sealed class WatchWorkflow(
    AvitoBrowser browser,
    ISearchStore store,
    AppSettings settings,
    SearchAnalyzer analyzer,
    TelegramNotifier telegram,
    ILogger<WatchWorkflow> logger)
{
    /// <summary>Сколько страниц смотреть за одну проверку, если новых объявлений очень много.</summary>
    public const int MaxPagesPerCheck = 3;

    /// <summary>Сколько новых объявлений за одну проверку открывать для подробностей.</summary>
    public const int MaxDetailsPerCheck = 10;

    /// <summary>Одна проверка одного поиска. Возвращает число новых объявлений.</summary>
    /// <param name="onNewListing">Что сделать с каждым новым объявлением (показать в интерфейсе).</param>
    public async Task<int> CheckOnceAsync(SavedSearch search, Action<ListingAssessment> onNewListing, CancellationToken ct)
    {
        var listings = store.LoadListings(search.Id);
        var isFirstCheck = listings.Count == 0;
        var newListings = new List<Listing>();
        var touchedIds = new HashSet<string>(); // какие объявления встретились — их и сохраним
        var now = DateTime.Now;

        foreach (var source in search.Sources)
        {
            var newestFirstUrl = AvitoUrl.SortedByDate(new Uri(source.Url));

            for (var pageNumber = 1; pageNumber <= MaxPagesPerCheck; pageNumber++)
            {
                var page = await browser.LoadSearchPageAsync(AvitoUrl.WithPage(newestFirstUrl, pageNumber), ct);
                var fresh = page.Items.Where(item => !listings.ContainsKey(item.Id)).ToList();

                ListingMerger.Merge(listings, page.Items, source.Label, now);
                newListings.AddRange(fresh);
                touchedIds.UnionWith(page.Items.Select(item => item.Id));

                // На странице встретились уже знакомые объявления — значит, все новые мы уже увидели
                if (fresh.Count < page.Items.Count || page.Items.Count == 0 || page.HasNextPage == false)
                    break;
            }
        }

        search.LastWatchAt = now;

        if (isFirstCheck)
        {
            // Без этого в первый раз в Telegram прилетели бы десятки «новых» объявлений
            store.UpsertListings(search.Id, touchedIds.Select(id => listings[id]));
            store.SaveSearch(search);
            logger.LogInformation("«{Search}»: первый запуск — запомнил {Count} текущих объявлений, дальше буду присылать только новые. " +
                                  "Для точной рыночной цены сделайте «Сканировать».", search.Name, listings.Count);
            return 0;
        }

        if (settings.OpenNewListingsForDetails)
            await LoadDetailsAsync(search, newListings.Take(MaxDetailsPerCheck), ct);

        store.UpsertListings(search.Id, touchedIds.Select(id => listings[id]));
        store.SaveSearch(search);

        if (newListings.Count == 0)
        {
            logger.LogInformation("«{Search}»: новых объявлений нет", search.Name);
            return 0;
        }

        var analysis = analyzer.Analyze(search, listings.Values);
        foreach (var listing in newListings)
        {
            var assessment = analysis.Evaluate(listing);

            // Копии и другие модели — шум, по умолчанию не беспокоим ими
            if (settings.HideReplicasAndOtherModels && assessment.Exclusion is { } exclusion)
            {
                logger.LogInformation("«{Search}»: пропущено «{Title}» — {Reason}", search.Name, listing.Title, exclusion.Text);
                continue;
            }

            logger.LogInformation("«{Search}»: НОВОЕ «{Title}» — {Price}{Deviation} · {Seller}",
                search.Name, listing.Title,
                listing.Price is { } price ? TextUtils.Money(price) : "цена не указана",
                assessment.DeviationPercent is { } d ? $" ({TextUtils.Percent(d)} к рынку)" : "",
                assessment.Seller.Title);
            onNewListing(assessment);

            await telegram.TrySendAsync(TelegramMessageFormatter.NewListing(search, assessment), ct);
        }

        return newListings.Count;
    }

    /// <summary>Открывает новые объявления: полное описание и данные продавца точнее покажут магазины и агентов.</summary>
    private async Task LoadDetailsAsync(SavedSearch search, IEnumerable<Listing> listings, CancellationToken ct)
    {
        foreach (var listing in listings)
        {
            try
            {
                var details = await browser.LoadListingDetailsAsync(listing.Url, ct);
                details.ApplyTo(listing);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Не открылось — не страшно, оценим по данным из выдачи
                logger.LogWarning("«{Search}»: не удалось открыть объявление {Url}: {Message}", search.Name, listing.Url, ex.Message);
            }
        }
    }
}
