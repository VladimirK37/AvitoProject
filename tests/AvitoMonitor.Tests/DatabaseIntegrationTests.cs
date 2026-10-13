using AvitoMonitor.Analysis;
using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;
using AvitoMonitor.Export;
using AvitoMonitor.History;
using AvitoMonitor.Persistence;
using AvitoMonitor.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvitoMonitor.Tests;

/// <summary>
/// Проверки на настоящих PostgreSQL и Elasticsearch. Если переменные окружения не заданы — тесты ничего не делают.
///
/// Запуск (PowerShell):
/// <code>
/// docker compose up -d
/// $env:AVITO_TEST_DB = "Host=localhost;Port=5433;Database=avito_test;Username=avito;Password=avito_local"
/// $env:AVITO_TEST_ELASTIC = "http://localhost:9200"
/// dotnet test
/// </code>
/// Используются отдельная база avito_test (пересоздаётся при каждом запуске) и индекс avito-queries-test —
/// рабочие данные программы не трогаются.
/// </summary>
public class DatabaseIntegrationTests
{
    private const string TestIndex = "avito-queries-test";

    private static readonly string? DatabaseConnection = Environment.GetEnvironmentVariable("AVITO_TEST_DB");
    private static readonly string? ElasticUrl = Environment.GetEnvironmentVariable("AVITO_TEST_ELASTIC");
    private static readonly object PrepareLock = new();
    private static bool _databasePrepared;

    [Fact]
    public void StoreSavesSearchesAndListings()
    {
        if (DatabaseConnection is null)
            return;

        var store = new PostgresSearchStore(Factory());
        var search = NewSearch();
        store.SaveSearch(search);

        store.UpsertListings(search.Id, [Item("1", 10000), Item("2", 12000)]);
        store.UpsertListings(search.Id, [Item("2", 11500), Item("3", 9000)]); // одно обновилось, одно новое

        search.Sources[0].NextPageToScan = 5;
        search.Sources.Add(new SearchSource { Label = "второй", Query = "airpods pro", City = "moskva", Url = "u2" });
        store.SaveSearch(search);

        var loaded = store.GetSearches().Single(s => s.Id == search.Id);
        Assert.Equal(["airpods 4", "второй"], loaded.Sources.Select(s => s.Label));
        Assert.Equal(5, loaded.Sources[0].NextPageToScan);
        Assert.True(Math.Abs((loaded.CreatedAt - search.CreatedAt).TotalMilliseconds) < 1);

        var listings = store.LoadListings(search.Id);
        Assert.Equal(3, listings.Count);
        Assert.Equal(11500m, listings["2"].Price);

        store.DeleteListings(search.Id, ["1"]);
        Assert.Equal(2, store.LoadListings(search.Id).Count);

        store.DeleteSearch(search.Id);
        Assert.DoesNotContain(store.GetSearches(), s => s.Id == search.Id);
        Assert.Empty(store.LoadListings(search.Id));
    }

    [Fact]
    public void ReportsAreSavedAndSurviveSearchDeletion()
    {
        if (DatabaseConnection is null)
            return;

        var factory = Factory();
        var store = new PostgresSearchStore(factory);
        var search = NewSearch();
        store.SaveSearch(search);
        store.UpsertListings(search.Id, Enumerable.Range(1, 6).Select(i => Item($"{i}", 10000 + i * 100)));

        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "avito-it-" + Guid.NewGuid().ToString("N")[..8]));
        var reports = new ReportService(store, new SearchAnalyzer(new AppSettings()), new ExcelReportBuilder(), factory, paths,
            NullLogger<ReportService>.Instance);

        var report = reports.CreateReport(search);
        Assert.True(File.Exists(report.FilePath));
        Assert.Equal(6, report.ListingsCount);
        Assert.Contains("airpods 4", report.Description);

        store.DeleteSearch(search.Id);
        var afterDeletion = reports.GetReports().Single(r => r.Id == report.Id);
        Assert.Null(afterDeletion.SearchId);
        Assert.Equal(search.Name, afterDeletion.SearchName);

        reports.DeleteReport(afterDeletion, deleteFile: true);
        Assert.False(File.Exists(report.FilePath));
        Assert.DoesNotContain(reports.GetReports(), r => r.Id == report.Id);
    }

    [Fact]
    public async Task HistoryIsSearchableInDatabaseAndElasticsearch()
    {
        if (DatabaseConnection is null)
            return;

        var factory = Factory();
        using var http = new HttpClient();
        if (ElasticUrl is not null)
            await http.DeleteAsync($"{ElasticUrl}/{TestIndex}");

        var history = new QueryHistoryService(factory, Elastic(http, ElasticUrl ?? "http://127.0.0.1:1"), NullLogger<QueryHistoryService>.Instance);
        await history.RecordAsync(new SearchRequest("airpods 4", "sankt-peterburg"), null);
        await history.RecordAsync(new SearchRequest("airpods 4", "sankt-peterburg"), null);
        await history.RecordAsync(new SearchRequest("iphone 15 128", "moskva", PriceMin: 50000), null);

        // Elasticsearch «выключен» — ищем по PostgreSQL (часть текста)
        var databaseOnly = new QueryHistoryService(factory, Elastic(http, "http://127.0.0.1:1"), NullLogger<QueryHistoryService>.Instance);
        var (fromDatabase, viaElastic) = await databaseOnly.SearchAsync("pods", 10, CancellationToken.None);
        Assert.False(viaElastic);
        Assert.Equal(2, Assert.Single(fromDatabase).Uses);

        if (ElasticUrl is null)
            return;

        // Elasticsearch — с опечаткой и по началу слова
        await http.PostAsync($"{ElasticUrl}/{TestIndex}/_refresh", null);

        var (withTypo, fromElastic) = await history.SearchAsync("airpds", 10, CancellationToken.None);
        Assert.True(fromElastic);
        var airpods = Assert.Single(withTypo, e => e.Text == "airpods 4");
        Assert.Equal(2, airpods.Uses);

        var (byPrefix, _) = await history.SearchAsync("iph", 10, CancellationToken.None);
        var iphone = Assert.Single(byPrefix);
        Assert.Equal("moskva", iphone.City);
        Assert.Equal(50000, iphone.PriceMin);

        await http.DeleteAsync($"{ElasticUrl}/{TestIndex}");
    }

    private static ElasticQueryIndex Elastic(HttpClient http, string url) =>
        new(http, new AppSettings { ElasticsearchUrl = url, ElasticsearchIndex = TestIndex }, NullLogger<ElasticQueryIndex>.Instance);

    /// <summary>Фабрика контекстов для тестовой базы; при первом вызове база пересоздаётся.</summary>
    private static IDbContextFactory<AvitoDbContext> Factory()
    {
        var factory = new TestDbContextFactory(new DbContextOptionsBuilder<AvitoDbContext>().UseNpgsql(DatabaseConnection).Options);
        lock (PrepareLock)
        {
            if (!_databasePrepared)
            {
                using var db = factory.CreateDbContext();
                db.Database.EnsureDeleted();
                db.Database.Migrate();
                _databasePrepared = true;
            }
        }

        return factory;
    }

    private static SavedSearch NewSearch() => new()
    {
        Id = "it-" + Guid.NewGuid().ToString("N")[..8],
        Name = "airpods 4",
        Category = SearchCategory.General,
        CreatedAt = DateTime.Now,
        Sources = [new SearchSource { Label = "airpods 4", Query = "airpods 4", City = "sankt-peterburg", Url = "u1" }],
    };

    private static Listing Item(string id, decimal price) => new()
    {
        Id = id,
        Title = "Apple AirPods 4",
        Url = $"https://www.avito.ru/x/y_{id}",
        Price = price,
        SourceLabel = "airpods 4",
        FirstSeenAt = DateTime.Now,
        LastSeenAt = DateTime.Now,
    };

    private sealed class TestDbContextFactory(DbContextOptions<AvitoDbContext> options) : IDbContextFactory<AvitoDbContext>
    {
        public AvitoDbContext CreateDbContext() => new(options);
    }
}
