using AvitoMonitor.Domain;

namespace AvitoMonitor.Storage;

/// <summary>
/// Все поиски в памяти — один общий список для интерфейса и фоновых задач.
/// Благодаря этому окно и слежение работают с одними и теми же объектами SavedSearch:
/// если слежение обновило «последнюю проверку», интерфейс видит это сразу.
/// </summary>
public sealed class SearchCatalog
{
    private readonly SearchRepository _repository;
    private readonly List<SavedSearch> _searches;
    private readonly object _lock = new();

    public SearchCatalog(SearchRepository repository)
    {
        _repository = repository;
        _searches = repository.GetAll().ToList();
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
        lock (_lock)
            _searches.Add(search);

        _repository.Save(search);
    }

    public void Save(SavedSearch search) => _repository.Save(search);

    public void Delete(SavedSearch search)
    {
        lock (_lock)
            _searches.Remove(search);

        _repository.Delete(search);
    }
}
