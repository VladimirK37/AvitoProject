using AvitoMonitor.Analysis;
using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;
using AvitoMonitor.Storage;
using AvitoMonitor.Telegram;

namespace AvitoMonitor.Workflows;

/// <summary>Куда WatchWorkflow сообщает о том, что происходит (реализует консольный интерфейс).</summary>
public interface IWatchObserver
{
    void OnInfo(string message);

    void OnNewListing(SavedSearch search, ListingAssessment assessment);

    void OnError(SavedSearch search, Exception exception);
}

/// <summary>
/// Режим «Следить»: раз в N минут открывает выдачу, отсортированную по дате,
/// находит объявления, которых раньше не было, оценивает их и присылает в Telegram.
/// </summary>
public sealed class WatchWorkflow(
    AvitoBrowser browser,
    SearchRepository repository,
    AppSettings settings,
    SearchAnalyzer analyzer,
    TelegramNotifier telegram)
{
    /// <summary>Сколько страниц смотреть за одну проверку, если новых объявлений очень много.</summary>
    public const int MaxPagesPerCheck = 3;

    /// <summary>Сколько новых объявлений за одну проверку открывать для подробностей.</summary>
    public const int MaxDetailsPerCheck = 10;

    /// <summary>Следит за поисками, пока не отменят (Ctrl+C).</summary>
    public async Task RunAsync(IReadOnlyList<SavedSearch> searches, IWatchObserver observer, CancellationToken ct)
    {
        while (true)
        {
            foreach (var search in searches)
            {
                try
                {
                    await CheckOnceAsync(search, observer, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Одна неудачная проверка не должна останавливать слежение
                    observer.OnError(search, ex);
                }
            }

            var next = DateTime.Now.AddMinutes(settings.WatchIntervalMinutes);
            observer.OnInfo($"Следующая проверка в {next:HH:mm}. Остановить слежение: Ctrl+C");
            await Task.Delay(next - DateTime.Now, ct);
        }
    }

    /// <summary>Одна проверка одного поиска. Возвращает число новых объявлений.</summary>
    public async Task<int> CheckOnceAsync(SavedSearch search, IWatchObserver observer, CancellationToken ct)
    {
        var listings = repository.LoadListings(search.Id);
        var isFirstCheck = listings.Count == 0;
        var newListings = new List<Listing>();
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

                // На странице встретились уже знакомые объявления — значит, все новые мы уже увидели
                if (fresh.Count < page.Items.Count || page.Items.Count == 0 || page.HasNextPage == false)
                    break;
            }
        }

        search.LastWatchAt = now;

        if (isFirstCheck)
        {
            // Без этого в первый раз в Telegram прилетели бы десятки «новых» объявлений
            repository.SaveListings(search.Id, listings);
            repository.Save(search);
            observer.OnInfo($"«{search.Name}»: первый запуск — запомнил {listings.Count} текущих объявлений, " +
                            "дальше буду присылать только новые. Для точной медианы сделайте «Сканировать».");
            return 0;
        }

        if (settings.OpenNewListingsForDetails)
            await LoadDetailsAsync(search, newListings.Take(MaxDetailsPerCheck), observer, ct);

        repository.SaveListings(search.Id, listings);
        repository.Save(search);

        if (newListings.Count == 0)
        {
            observer.OnInfo($"«{search.Name}»: новых объявлений нет");
            return 0;
        }

        var analysis = analyzer.Analyze(search, listings.Values);
        foreach (var listing in newListings)
        {
            var assessment = analysis.Evaluate(listing);

            // Копии и другие модели — шум, по умолчанию не беспокоим ими
            if (settings.HideReplicasAndOtherModels && assessment.Exclusion is { } exclusion)
            {
                observer.OnInfo($"Пропущено «{listing.Title}» — {exclusion.Text}");
                continue;
            }

            observer.OnNewListing(search, assessment);

            var sent = await telegram.TrySendAsync(TelegramMessageFormatter.NewListing(search, assessment), ct);
            if (!sent && telegram.IsEnabled)
                observer.OnInfo($"Не удалось отправить в Telegram: {telegram.LastError}");
        }

        return newListings.Count;
    }

    /// <summary>Открывает новые объявления: полное описание и данные продавца точнее покажут магазины и агентов.</summary>
    private async Task LoadDetailsAsync(SavedSearch search, IEnumerable<Listing> listings, IWatchObserver observer, CancellationToken ct)
    {
        foreach (var listing in listings)
        {
            try
            {
                var details = await browser.LoadListingDetailsAsync(listing.Url, ct);
                details.ApplyTo(listing);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Не открылось — не страшно, оценим по данным из выдачи
                observer.OnError(search, ex);
            }
        }
    }
}
