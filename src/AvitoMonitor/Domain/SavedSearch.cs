using System.Text.Json.Serialization;

namespace AvitoMonitor.Domain;

/// <summary>
/// Сохранённый поиск: одна или несколько ссылок Авито с фильтрами.
/// Несколько ссылок — это «мультипоиск», например отдельно iPhone 15, 16 и 17.
/// </summary>
public sealed class SavedSearch
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Категория определяет, как группировать объявления и с чем сравнивать цену.</summary>
    public SearchCategory Category { get; set; }

    public List<SearchSource> Sources { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    public DateTime? LastScanAt { get; set; }

    public DateTime? LastWatchAt { get; set; }

    [JsonIgnore]
    public bool IsMulti => Sources.Count > 1;
}

/// <summary>Одна ссылка внутри поиска и прогресс её сканирования.</summary>
public sealed class SearchSource
{
    /// <summary>Короткое название ссылки, например «iPhone 15».</summary>
    public string Label { get; set; } = "";

    /// <summary>Ссылка на страницу поиска Авито со всеми фильтрами.</summary>
    public string Url { get; set; } = "";

    /// <summary>С какой страницы продолжать сканирование («Дособрать»).</summary>
    public int NextPageToScan { get; set; } = 1;

    /// <summary>Сканирование дошло до последней страницы.</summary>
    public bool ScanCompleted { get; set; }

    /// <summary>Сколько объявлений Авито показывал по этой ссылке при последнем сканировании.</summary>
    public int? TotalCountOnAvito { get; set; }
}
