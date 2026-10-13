using System.Text.Json;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Storage;

/// <summary>
/// Старое хранилище — JSON-файлы в папке searches. Теперь только читается:
/// при первом запуске с PostgreSQL DatabaseInitializer переносит отсюда поиски и объявления в базу.
/// <code>
/// searches/
///   20261013-153000/
///     search.json     — поиск
///     listings.json   — собранные объявления
/// </code>
/// </summary>
public sealed class LegacyJsonSearchRepository(string searchesDir)
{
    private const string SearchFile = "search.json";
    private const string ListingsFile = "listings.json";

    public IReadOnlyList<SavedSearch> GetAll()
    {
        if (!Directory.Exists(searchesDir))
            return [];

        return Directory.GetDirectories(searchesDir)
            .Select(dir => Path.Combine(dir, SearchFile))
            .Where(File.Exists)
            .Select(file => JsonSerializer.Deserialize<SavedSearch>(File.ReadAllText(file), JsonDefaults.Options))
            .OfType<SavedSearch>()
            .OrderBy(search => search.CreatedAt)
            .ToList();
    }

    public IReadOnlyList<Listing> LoadListings(string searchId)
    {
        var file = Path.Combine(searchesDir, searchId, ListingsFile);
        if (!File.Exists(file))
            return [];

        return JsonSerializer.Deserialize<List<Listing>>(File.ReadAllText(file), JsonDefaults.Options) ?? [];
    }
}
