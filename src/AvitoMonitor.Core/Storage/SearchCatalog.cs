using AvitoMonitor.Domain;

namespace AvitoMonitor.Storage;

/// <summary>
/// Все поиски в памяти — один общий список для интерфейса и фоновых задач.
/// Благодаря этому окно и слежение работают с одними и теми же объектами SavedSearch:
/// если слежение обновило «последнюю проверку», интерфейс видит это сразу.
/// </summary>
public sealed class SearchCatalog
{
    private readonly ISearchStore _store;
    private readonly List<SavedSearch> _searches;
    private readonly object _lock = new();

    public SearchCatalog(ISearchStore store)
    {
        _store = store;
        _searches = store.GetSearches().ToList();
    }

    public IReadOnlyList<SavedSearch> All
    {
        get
        {
            lock (_lock)
                return _searches.ToList();
        }
    }

    public void Add(SavedSearch search)
    {
        _store.SaveSearch(search);

        lock (_lock)
            _searches.Add(search);
    }

    public void Save(SavedSearch search) => _store.SaveSearch(search);

    public void Delete(SavedSearch search)
    {
        lock (_lock)
            _searches.Remove(search);

        _store.DeleteSearch(search.Id);
    }
}
