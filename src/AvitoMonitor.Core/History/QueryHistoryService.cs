using AvitoMonitor.Avito;
using AvitoMonitor.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.History;

/// <summary>Один уникальный запрос из истории.</summary>
public sealed record QueryHistoryEntry(string Text, string City, int? PriceMin, int? PriceMax, int Uses, DateTime LastUsedAt)
{
    public SearchRequest ToRequest() => new(Text, City, PriceMin, PriceMax);

    /// <summary>«airpods 4» · Санкт-Петербург · от 7 000 ₽</summary>
    public string Display => ToRequest().Describe();

    public string Details => $"искали раз: {Uses} · последний раз {LastUsedAt:dd.MM.yyyy HH:mm}";

    // Так запрос показывается в поле ввода после выбора из подсказок
    public override string ToString() => Text;
}

/// <summary>
/// История всего, что вводили в поиск.
/// <list type="bullet">
/// <item>PostgreSQL — главное хранилище: каждая отправка запроса — отдельная строка в search_queries;</item>
/// <item>Elasticsearch — индекс для быстрого поиска по истории с опечатками.</item>
/// </list>
/// Elasticsearch не обязателен: если он не запущен, история пишется только в PostgreSQL и ищется там.
/// При запуске программы индекс досинхронизируется из PostgreSQL (SyncElasticAsync).
/// </summary>
public sealed class QueryHistoryService(
    IDbContextFactory<AvitoDbContext> dbFactory,
    ElasticQueryIndex elastic,
    ILogger<QueryHistoryService> logger)
{
    public ElasticQueryIndex Elastic => elastic;

    /// <summary>Запомнить, что искали.</summary>
    public async Task RecordAsync(SearchRequest request, string? searchId, CancellationToken ct = default)
    {
        var now = DateTime.Now;

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.SearchQueries.Add(new SearchQueryEntity
            {
                Text = request.Query.Trim(),
                City = request.City,
                PriceMin = request.PriceMin,
                PriceMax = request.PriceMax,
                SearchId = searchId,
                CreatedAt = EntityMapper.ToUtc(now),
            });
            await db.SaveChangesAsync(ct);
        }

        // Не получилось — не страшно: запись уже в PostgreSQL, в индекс попадёт при следующей синхронизации
        await elastic.RecordUseAsync(request, now, ct);
    }

    /// <summary>Поиск по истории: сначала Elasticsearch, если он недоступен — PostgreSQL.</summary>
    public async Task<(IReadOnlyList<QueryHistoryEntry> Entries, bool FromElastic)> SearchAsync(string text, int size, CancellationToken ct)
    {
        var fromElastic = await elastic.SearchAsync(text, size, ct);
        if (fromElastic is not null)
            return (fromElastic, true);

        return (await SearchInDatabaseAsync(text, size, ct), false);
    }

    /// <summary>Переложить всю историю из PostgreSQL в Elasticsearch (при запуске программы).</summary>
    public async Task<int> SyncElasticAsync(CancellationToken ct = default)
    {
        var entries = await SearchInDatabaseAsync("", int.MaxValue, ct);
        var count = 0;
        foreach (var entry in entries)
        {
            if (!await elastic.PutAsync(entry, ct))
                return count; // Elasticsearch недоступен — попробуем в следующий раз

            count++;
        }

        if (count > 0)
            logger.LogInformation("Elasticsearch: история запросов синхронизирована ({Count})", count);
        return count;
    }

    private async Task<IReadOnlyList<QueryHistoryEntry>> SearchInDatabaseAsync(string text, int size, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var queries = db.SearchQueries.AsNoTracking();
        text = text.Trim();
        if (text.Length > 0)
            queries = queries.Where(q => EF.Functions.ILike(q.Text, "%" + text + "%"));

        var rows = await queries
            .GroupBy(q => new { q.Text, q.City, q.PriceMin, q.PriceMax })
            .Select(g => new
            {
                g.Key.Text,
                g.Key.City,
                g.Key.PriceMin,
                g.Key.PriceMax,
                Uses = g.Count(),
                LastUsedAt = g.Max(q => q.CreatedAt),
            })
            .OrderByDescending(r => r.LastUsedAt)
            .Take(size)
            .ToListAsync(ct);

        return rows
            .Select(r => new QueryHistoryEntry(r.Text, r.City, r.PriceMin, r.PriceMax, r.Uses, EntityMapper.ToLocal(r.LastUsedAt)))
            .ToList();
    }
}
