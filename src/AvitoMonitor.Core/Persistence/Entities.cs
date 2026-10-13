namespace AvitoMonitor.Persistence;

// Таблицы базы данных PostgreSQL. Это отдельные классы, а не модели из Domain:
// так сразу видно, что и в каком виде лежит в базе, а модели программы не зависят от EF Core.
// Все даты хранятся в UTC (так требует тип timestamptz), перевод в местное время — в EntityMapper.

/// <summary>Таблица searches — сохранённые поиски.</summary>
public sealed class SearchEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastScanAt { get; set; }
    public DateTime? LastWatchAt { get; set; }
    public bool WatchEnabled { get; set; }
    public List<SearchSourceEntity> Sources { get; set; } = new();
}

/// <summary>Таблица search_sources — запросы внутри поиска (несколько — это мультипоиск).</summary>
public sealed class SearchSourceEntity
{
    public long Id { get; set; }
    public string SearchId { get; set; } = "";
    public int Position { get; set; }
    public string Label { get; set; } = "";
    public string? Query { get; set; }
    public string? City { get; set; }
    public int? PriceMin { get; set; }
    public int? PriceMax { get; set; }
    public string Url { get; set; } = "";
    public int NextPageToScan { get; set; }
    public bool ScanCompleted { get; set; }
    public int? TotalCountOnAvito { get; set; }
}

/// <summary>Таблица listings — объявления. Ключ: поиск + номер объявления на Авито.</summary>
public sealed class ListingEntity
{
    public string SearchId { get; set; } = "";
    public string AvitoId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public decimal? Price { get; set; }
    public string? Description { get; set; }
    public string? Address { get; set; }
    public string? PublishedText { get; set; }
    public string? SellerName { get; set; }
    public string? SellerUrl { get; set; }
    public int? SellerReviews { get; set; }
    public string? SellerLabel { get; set; }
    public string? Badges { get; set; }
    public string SourceLabel { get; set; } = "";
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public bool DetailsLoaded { get; set; }
}

/// <summary>Таблица search_queries — всё, что вводили в поиск (каждый раз — новая строка).</summary>
public sealed class SearchQueryEntity
{
    public long Id { get; set; }
    public string Text { get; set; } = "";
    public string City { get; set; } = "";
    public int? PriceMin { get; set; }
    public int? PriceMax { get; set; }
    public string? SearchId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Таблица reports — Excel-отчёты (сам файл лежит в папке reports).</summary>
public sealed class ReportEntity
{
    public long Id { get; set; }
    public string? SearchId { get; set; }
    public string SearchName { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int ListingsCount { get; set; }
    public int ExcludedCount { get; set; }
    public string Summary { get; set; } = "";
    public string FilePath { get; set; } = "";
}
