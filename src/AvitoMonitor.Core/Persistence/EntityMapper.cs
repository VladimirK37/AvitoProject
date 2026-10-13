using AvitoMonitor.Domain;

namespace AvitoMonitor.Persistence;

/// <summary>
/// Перекладывает данные между моделями программы (Domain) и таблицами базы (Entities).
/// Даты: в программе — местное время, в базе — UTC.
/// </summary>
public static class EntityMapper
{
    // ----- Поиск -----

    public static SavedSearch ToDomain(SearchEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Category = Enum.TryParse<SearchCategory>(entity.Category, out var category) ? category : SearchCategory.General,
        CreatedAt = ToLocal(entity.CreatedAt),
        LastScanAt = ToLocal(entity.LastScanAt),
        LastWatchAt = ToLocal(entity.LastWatchAt),
        WatchEnabled = entity.WatchEnabled,
        Sources = entity.Sources.OrderBy(s => s.Position).Select(ToDomain).ToList(),
    };

    /// <summary>Записывает поиск в строку таблицы. Список запросов (sources) заменяется целиком.</summary>
    public static void CopyToEntity(SavedSearch search, SearchEntity entity)
    {
        entity.Id = search.Id;
        entity.Name = search.Name;
        entity.Category = search.Category.ToString();
        entity.CreatedAt = ToUtc(search.CreatedAt);
        entity.LastScanAt = ToUtc(search.LastScanAt);
        entity.LastWatchAt = ToUtc(search.LastWatchAt);
        entity.WatchEnabled = search.WatchEnabled;
        entity.Sources = search.Sources.Select((source, index) => ToEntity(search.Id, source, index)).ToList();
    }

    private static SearchSource ToDomain(SearchSourceEntity entity) => new()
    {
        Label = entity.Label,
        Query = entity.Query,
        City = entity.City,
        PriceMin = entity.PriceMin,
        PriceMax = entity.PriceMax,
        Url = entity.Url,
        NextPageToScan = entity.NextPageToScan,
        ScanCompleted = entity.ScanCompleted,
        TotalCountOnAvito = entity.TotalCountOnAvito,
    };

    private static SearchSourceEntity ToEntity(string searchId, SearchSource source, int position) => new()
    {
        SearchId = searchId,
        Position = position,
        Label = source.Label,
        Query = source.Query,
        City = source.City,
        PriceMin = source.PriceMin,
        PriceMax = source.PriceMax,
        Url = source.Url,
        NextPageToScan = source.NextPageToScan,
        ScanCompleted = source.ScanCompleted,
        TotalCountOnAvito = source.TotalCountOnAvito,
    };

    // ----- Объявление -----

    public static Listing ToDomain(ListingEntity entity) => new()
    {
        Id = entity.AvitoId,
        Title = entity.Title,
        Url = entity.Url,
        Price = entity.Price,
        Description = entity.Description,
        Address = entity.Address,
        PublishedText = entity.PublishedText,
        SellerName = entity.SellerName,
        SellerUrl = entity.SellerUrl,
        SellerReviews = entity.SellerReviews,
        SellerLabel = entity.SellerLabel,
        Badges = entity.Badges,
        SourceLabel = entity.SourceLabel,
        FirstSeenAt = ToLocal(entity.FirstSeenAt),
        LastSeenAt = ToLocal(entity.LastSeenAt),
        DetailsLoaded = entity.DetailsLoaded,
    };

    public static void CopyToEntity(string searchId, Listing listing, ListingEntity entity)
    {
        entity.SearchId = searchId;
        entity.AvitoId = listing.Id;
        entity.Title = listing.Title;
        entity.Url = listing.Url;
        entity.Price = listing.Price;
        entity.Description = listing.Description;
        entity.Address = listing.Address;
        entity.PublishedText = listing.PublishedText;
        entity.SellerName = listing.SellerName;
        entity.SellerUrl = listing.SellerUrl;
        entity.SellerReviews = listing.SellerReviews;
        entity.SellerLabel = listing.SellerLabel;
        entity.Badges = listing.Badges;
        entity.SourceLabel = listing.SourceLabel;
        entity.FirstSeenAt = ToUtc(listing.FirstSeenAt);
        entity.LastSeenAt = ToUtc(listing.LastSeenAt);
        entity.DetailsLoaded = listing.DetailsLoaded;
    }

    // ----- Отчёт -----

    public static ReportRecord ToDomain(ReportEntity entity) => new(
        entity.Id,
        entity.SearchId,
        entity.SearchName,
        entity.Description,
        ToLocal(entity.CreatedAt),
        entity.ListingsCount,
        entity.ExcludedCount,
        entity.Summary,
        entity.FilePath);

    // ----- Даты -----

    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
        _ => value.ToUniversalTime(),
    };

    public static DateTime? ToUtc(DateTime? value) => value is { } v ? ToUtc(v) : null;

    public static DateTime ToLocal(DateTime value) =>
        (value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value).ToLocalTime();

    public static DateTime? ToLocal(DateTime? value) => value is { } v ? ToLocal(v) : null;
}
