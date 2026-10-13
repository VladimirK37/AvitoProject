using AvitoMonitor.Domain;
using AvitoMonitor.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AvitoMonitor.Storage;

/// <summary>
/// Поиски и объявления в PostgreSQL.
/// На каждую операцию — свой короткий DbContext: так безопасно работать из нескольких потоков
/// (окно и фоновые задачи обращаются к базе одновременно).
/// </summary>
public sealed class PostgresSearchStore(IDbContextFactory<AvitoDbContext> dbFactory) : ISearchStore
{
    public IReadOnlyList<SavedSearch> GetSearches()
    {
        using var db = dbFactory.CreateDbContext();
        return db.Searches
            .AsNoTracking()
            .Include(s => s.Sources)
            .OrderBy(s => s.CreatedAt)
            .AsEnumerable()
            .Select(EntityMapper.ToDomain)
            .ToList();
    }

    public void SaveSearch(SavedSearch search)
    {
        using var db = dbFactory.CreateDbContext();
        var entity = db.Searches.Include(s => s.Sources).FirstOrDefault(s => s.Id == search.Id);
        if (entity is null)
        {
            entity = new SearchEntity();
            EntityMapper.CopyToEntity(search, entity);
            db.Searches.Add(entity);
        }
        else
        {
            // Запросы поиска проще заменить целиком, чем сравнивать по одному
            db.SearchSources.RemoveRange(entity.Sources);
            EntityMapper.CopyToEntity(search, entity);
        }

        db.SaveChanges();
    }

    public void DeleteSearch(string searchId)
    {
        using var db = dbFactory.CreateDbContext();
        db.Listings.Where(l => l.SearchId == searchId).ExecuteDelete();
        db.Searches.Where(s => s.Id == searchId).ExecuteDelete(); // запросы удалятся каскадом, отчёты останутся
    }

    public Dictionary<string, Listing> LoadListings(string searchId)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Listings
            .AsNoTracking()
            .Where(l => l.SearchId == searchId)
            .AsEnumerable()
            .Select(EntityMapper.ToDomain)
            .ToDictionary(l => l.Id);
    }

    public void UpsertListings(string searchId, IEnumerable<Listing> listings)
    {
        var list = listings.DistinctBy(l => l.Id).ToList();
        if (list.Count == 0)
            return;

        using var db = dbFactory.CreateDbContext();

        // Одним запросом достаём те, что уже есть в базе, остальные — добавляем
        var ids = list.Select(l => l.Id).ToList();
        var existing = db.Listings
            .Where(l => l.SearchId == searchId && ids.Contains(l.AvitoId))
            .ToDictionary(l => l.AvitoId);

        foreach (var listing in list)
        {
            if (existing.TryGetValue(listing.Id, out var entity))
            {
                EntityMapper.CopyToEntity(searchId, listing, entity);
                continue;
            }

            // Сначала заполняем (в том числе ключ), потом добавляем — иначе EF увидит несколько строк с пустым ключом
            entity = new ListingEntity();
            EntityMapper.CopyToEntity(searchId, listing, entity);
            db.Listings.Add(entity);
        }

        db.SaveChanges();
    }

    public void DeleteListings(string searchId, IEnumerable<string> listingIds)
    {
        var ids = listingIds.ToList();
        if (ids.Count == 0)
            return;

        using var db = dbFactory.CreateDbContext();
        db.Listings.Where(l => l.SearchId == searchId && ids.Contains(l.AvitoId)).ExecuteDelete();
    }
}
