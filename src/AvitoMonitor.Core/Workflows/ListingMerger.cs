using AvitoMonitor.Domain;

namespace AvitoMonitor.Workflows;

public static class ListingMerger
{
    /// <summary>
    /// Добавляет объявления со страницы к уже собранным.
    /// Новые — добавляет, известные — обновляет (цена могла измениться).
    /// </summary>
    /// <returns>Сколько объявлений оказались новыми.</returns>
    public static int Merge(IDictionary<string, Listing> known, IEnumerable<Listing> fromPage, string sourceLabel, DateTime now)
    {
        var added = 0;

        foreach (var item in fromPage)
        {
            if (known.TryGetValue(item.Id, out var existing))
            {
                existing.Title = item.Title;
                existing.Price = item.Price;
                existing.Address = item.Address ?? existing.Address;
                existing.PublishedText = item.PublishedText ?? existing.PublishedText;
                existing.Badges = item.Badges ?? existing.Badges;
                existing.SellerName ??= item.SellerName;
                existing.SellerUrl ??= item.SellerUrl;
                existing.SellerReviews = item.SellerReviews ?? existing.SellerReviews;

                // Полное описание со страницы объявления не затираем коротким фрагментом из выдачи
                if (!existing.DetailsLoaded)
                    existing.Description = item.Description ?? existing.Description;

                existing.LastSeenAt = now;
                continue;
            }

            item.SourceLabel = sourceLabel;
            item.FirstSeenAt = now;
            item.LastSeenAt = now;
            known[item.Id] = item;
            added++;
        }

        return added;
    }
}
