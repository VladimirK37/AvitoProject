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

    /// <summary>Слежение включено — после перезапуска программы оно продолжится само.</summary>
    public bool WatchEnabled { get; set; }

    [JsonIgnore]
    public bool IsMulti => Sources.Count > 1;

    /// <summary>Новый номер поиска: дата и время с миллисекундами, например 20261013-153000-123.</summary>
    public static string NewId() => DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
}

/// <summary>
/// Один запрос внутри поиска и прогресс его сканирования.
/// Обычно это слова + город (+ цена), а ссылку программа составляет сама.
/// Старые поиски, созданные по готовой ссылке, хранят только Url (Query = null).
/// </summary>
public sealed class SearchSource
{
    /// <summary>Короткое название, например «iPhone 15».</summary>
    public string Label { get; set; } = "";

    /// <summary>Что ищем — слова, как в строке поиска Авито. null — поиск задан готовой ссылкой.</summary>
    public string? Query { get; set; }

    /// <summary>Город (slug из адреса Авито, например sankt-peterburg).</summary>
    public string? City { get; set; }

    public int? PriceMin { get; set; }

    public int? PriceMax { get; set; }

    /// <summary>Ссылка на страницу поиска Авито (для поиска по словам составляется автоматически).</summary>
    public string Url { get; set; } = "";

    [JsonIgnore]
    public bool IsKeywordSearch => !string.IsNullOrWhiteSpace(Query);

    /// <summary>С какой страницы продолжать сканирование («Дособрать»).</summary>
    public int NextPageToScan { get; set; } = 1;

    /// <summary>Сканирование дошло до последней страницы.</summary>
    public bool ScanCompleted { get; set; }

    /// <summary>Сколько объявлений Авито показывал по этой ссылке при последнем сканировании.</summary>
    public int? TotalCountOnAvito { get; set; }
}
