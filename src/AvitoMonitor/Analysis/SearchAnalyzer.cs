using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Analysis;

/// <summary>
/// «Рынок» по собранной выдаче: медиана, минимум и максимум в каждой группе.
/// </summary>
public sealed class MarketSnapshot(
    IReadOnlyDictionary<string, GroupStats> groups,
    IReadOnlyDictionary<string, GroupStats> fallbacks,
    bool perSquareMeter)
{
    /// <summary>Узкие группы: «128 ГБ», «2-к»…</summary>
    public IReadOnlyDictionary<string, GroupStats> Groups { get; } = groups;

    /// <summary>Широкие группы: «Все объявления» или название ссылки в мультипоиске.</summary>
    public IReadOnlyDictionary<string, GroupStats> Fallbacks { get; } = fallbacks;

    /// <summary>true — сравниваем цену за м² (недвижимость).</summary>
    public bool PerSquareMeter { get; } = perSquareMeter;

    /// <summary>С какой группой сравнивать: своя группа, если в ней достаточно объявлений, иначе широкая.</summary>
    public (GroupStats? Stats, bool IsWholeSearch) FindReference(GroupKeys keys, int minGroupSize)
    {
        Groups.TryGetValue(keys.Group, out var group);
        if (group is not null && group.Count >= minGroupSize)
            return (group, false);

        if (Fallbacks.TryGetValue(keys.Fallback, out var fallback) && fallback.Count >= 2)
            return (fallback, true);

        return (group, false);
    }
}

/// <summary>Анализ выдачи: рынок + сколько объявлений у каждого продавца. Умеет оценить любое объявление.</summary>
public sealed class SearchAnalysis(SavedSearch search, MarketSnapshot market, IReadOnlyDictionary<string, int> sellerCounts, AppSettings settings)
{
    public SavedSearch Search { get; } = search;

    public MarketSnapshot Market { get; } = market;

    /// <summary>Полная оценка одного объявления.</summary>
    public ListingAssessment Evaluate(Listing listing)
    {
        var keys = GroupKeys.For(listing, Search);
        var value = ListingFeatures.GetComparableValue(listing, Search.Category);
        var (reference, isWholeSearch) = Market.FindReference(keys, settings.MinGroupSizeForMedian);

        double? deviation = value is > 0 && reference is { Median: > 0 }
            ? (double)((value.Value - reference.Median) / reference.Median * 100m)
            : null;

        var sellerKey = SellerClassifier.SellerKey(listing);
        var sellerListings = sellerKey is null ? 0 : sellerCounts.GetValueOrDefault(sellerKey);

        return new ListingAssessment(
            listing,
            keys.Group,
            value,
            Market.PerSquareMeter,
            reference,
            isWholeSearch,
            deviation,
            RedFlagDetector.Detect(listing, deviation, settings.SuspiciouslyCheapPercent),
            SellerClassifier.Classify(listing, Search.Category, sellerListings),
            MessageDraftBuilder.Build(listing, Search.Category));
    }
}

/// <summary>Строит анализ по всем собранным объявлениям поиска.</summary>
public sealed class SearchAnalyzer(AppSettings settings)
{
    public SearchAnalysis Analyze(SavedSearch search, IReadOnlyCollection<Listing> listings)
    {
        var points = listings
            .Select(listing => (
                Keys: GroupKeys.For(listing, search),
                Value: ListingFeatures.GetComparableValue(listing, search.Category)))
            .Where(point => point.Value is > 0)
            .ToList();

        var groups = points
            .GroupBy(point => point.Keys.Group)
            .ToDictionary(group => group.Key, group => BuildStats(group.Key, group.Select(point => point.Value!.Value)));

        var fallbacks = points
            .GroupBy(point => point.Keys.Fallback)
            .ToDictionary(group => group.Key, group => BuildStats(group.Key, group.Select(point => point.Value!.Value)));

        var market = new MarketSnapshot(groups, fallbacks, perSquareMeter: search.Category == SearchCategory.RealEstate);
        return new SearchAnalysis(search, market, SellerClassifier.CountListingsBySeller(listings), settings);
    }

    private static GroupStats BuildStats(string key, IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return new GroupStats(key, list.Count, Statistics.Median(list)!.Value, list.Min(), list.Max());
    }
}
