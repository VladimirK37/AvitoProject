using System.Text.Json;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Storage;

/// <summary>
/// Хранит поиски и объявления в обычных JSON-файлах — их можно открыть и посмотреть глазами.
///
/// Структура папки:
/// <code>
/// searches/
///   20261013-153000/
///     search.json     — ссылки, название, прогресс сканирования
///     listings.json   — все собранные объявления
///     report.xlsx     — Excel-отчёт
/// </code>
/// </summary>
public sealed class SearchRepository(string searchesDir)
{
    private const string SearchFile = "search.json";
    private const string ListingsFile = "listings.json";
    private const string ReportFile = "report.xlsx";

    public static string NewId() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

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

    public void Save(SavedSearch search)
    {
        Directory.CreateDirectory(GetDir(search.Id));
        FileUtils.WriteAllTextAtomic(Path.Combine(GetDir(search.Id), SearchFile), JsonSerializer.Serialize(search, JsonDefaults.Options));
    }

    public void Delete(SavedSearch search)
    {
        var dir = GetDir(search.Id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    /// <summary>Объявления поиска: словарь «номер объявления → объявление».</summary>
    public Dictionary<string, Listing> LoadListings(string searchId)
    {
        var file = Path.Combine(GetDir(searchId), ListingsFile);
        if (!File.Exists(file))
            return new Dictionary<string, Listing>();

        var list = JsonSerializer.Deserialize<List<Listing>>(File.ReadAllText(file), JsonDefaults.Options) ?? [];
        return list.GroupBy(listing => listing.Id).ToDictionary(group => group.Key, group => group.Last());
    }

    public void SaveListings(string searchId, IReadOnlyDictionary<string, Listing> listings)
    {
        Directory.CreateDirectory(GetDir(searchId));
        var ordered = listings.Values.OrderBy(listing => listing.FirstSeenAt).ToList();
        FileUtils.WriteAllTextAtomic(Path.Combine(GetDir(searchId), ListingsFile), JsonSerializer.Serialize(ordered, JsonDefaults.Options));
    }

    public string GetReportPath(SavedSearch search) => Path.Combine(GetDir(search.Id), ReportFile);

    public string GetDir(string searchId) => Path.Combine(searchesDir, searchId);
}
