using AvitoMonitor.Domain;

namespace AvitoMonitor.Storage;

/// <summary>
/// Где хранятся поиски и объявления. Сейчас — PostgreSQL (PostgresSearchStore).
/// Сценарии (сканирование, слежение, редактор) работают только через этот интерфейс
/// и не знают, какая база под ним.
/// </summary>
public interface ISearchStore
{
    IReadOnlyList<SavedSearch> GetSearches();

    /// <summary>Создать или обновить поиск (вместе со списком его запросов).</summary>
    void SaveSearch(SavedSearch search);

    /// <summary>Удалить поиск и все его объявления. Записи об отчётах остаются.</summary>
    void DeleteSearch(string searchId);

    /// <summary>Объявления поиска: словарь «номер объявления → объявление».</summary>
    Dictionary<string, Listing> LoadListings(string searchId);

    /// <summary>Добавить новые объявления и обновить уже известные.</summary>
    void UpsertListings(string searchId, IEnumerable<Listing> listings);

    void DeleteListings(string searchId, IEnumerable<string> listingIds);
}
