using AvitoMonitor.Configuration;
using AvitoMonitor.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Persistence;

/// <summary>
/// Подготовка базы при запуске программы:
/// 1) применяет миграции — создаёт или обновляет таблицы;
/// 2) один раз переносит в PostgreSQL поиски и объявления, которые раньше хранились в JSON-файлах.
/// </summary>
public sealed class DatabaseInitializer(
    IDbContextFactory<AvitoDbContext> dbFactory,
    ISearchStore store,
    LegacyJsonSearchRepository legacy,
    AppPaths paths,
    ILogger<DatabaseInitializer> logger)
{
    private const string ImportedMarkerFile = "_перенесено-в-postgresql.txt";

    public void Initialize()
    {
        using (var db = dbFactory.CreateDbContext())
            db.Database.Migrate();

        logger.LogInformation("PostgreSQL подключён, структура базы актуальна");
        ImportLegacyJson();
    }

    private void ImportLegacyJson()
    {
        var marker = Path.Combine(paths.SearchesDir, ImportedMarkerFile);
        if (File.Exists(marker))
            return;

        var searches = legacy.GetAll();
        if (searches.Count == 0)
            return;

        var listingsCount = 0;
        foreach (var search in searches)
        {
            var listings = legacy.LoadListings(search.Id);
            store.SaveSearch(search);
            store.UpsertListings(search.Id, listings);
            listingsCount += listings.Count;
        }

        File.WriteAllText(marker,
            $"Поиски из этой папки перенесены в PostgreSQL {DateTime.Now:dd.MM.yyyy HH:mm}. Папку можно удалить.");
        logger.LogInformation("Перенесено из JSON в PostgreSQL: поисков {Searches}, объявлений {Listings}", searches.Count, listingsCount);
    }
}
