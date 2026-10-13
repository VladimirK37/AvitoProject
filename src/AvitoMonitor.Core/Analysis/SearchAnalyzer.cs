using AvitoMonitor.Avito;
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
    /// <summary>Узкие группы: «128 ГБ», «2-к», «С шумоподавлением (ANC)»…</summary>
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

/// <summary>
/// Решает, какие объявления НЕ учитывать в рыночной цене:
/// копии («Оригинал» в кавычках, Luxe, Airoha) и другие модели (искали AirPods 4 — попались AirPods Pro 3).
/// </summary>
public sealed class MarketFilter
{
    private readonly SavedSearch _search;
    private readonly Dictionary<string, string?> _searchTextByLabel;

    public MarketFilter(SavedSearch search)
    {
        _search = search;
        _searchTextByLabel = search.Sources
            .GroupBy(source => source.Label)
            .ToDictionary(group => group.Key, group => SearchTextOf(group.First().Url));
    }

    private static string? SearchTextOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? AvitoUrl.GetSearchText(uri) : null;

    public MarketExclusion? Check(Listing listing)
    {
        var replicaMarker = ReplicaDetector.FindMarker(listing, _search.Category);
        if (replicaMarker is not null)
            return new MarketExclusion(ExclusionKind.Replica, replicaMarker);

        // У квартир заголовки шаблонные («2-к. квартира, 54 м²»), сравнивать их с запросом бессмысленно
        if (_search.Category == SearchCategory.RealEstate)
            return null;

        var mismatch = ModelMatcher.FindMismatch(listing.Title, SearchTextFor(listing));
        return mismatch is null ? null : new MarketExclusion(ExclusionKind.OtherModel, mismatch);
    }

    /// <summary>Текст запроса той ссылки, из которой пришло объявление (важно для мультипоиска).</summary>
    private string? SearchTextFor(Listing listing)
    {
        if (_searchTextByLabel.TryGetValue(listing.SourceLabel, out var text))
            return text;

        return _search.Sources.Count == 1 ? _searchTextByLabel.Values.FirstOrDefault() : null;
    }
}

/// <summary>Анализ выдачи: рынок + сколько объявлений у каждого продавца. Умеет оценить любое объявление.</summary>
public sealed class SearchAnalysis(
    SavedSearch search,
    MarketSnapshot market,
    MarketFilter filter,
    IReadOnlyDictionary<string, int> sellerCounts,
    AppSettings settings)
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

        var exclusion = filter.Check(listing);

        var flags = RedFlagDetector.Detect(listing, deviation, settings.SuspiciouslyCheapPercent, Search.Category).ToList();
        if (exclusion is { Kind: ExclusionKind.OtherModel })
            flags.Insert(0, new RedFlag(FlagLevel.Info, $"Другая модель ({exclusion.Detail}) — в рыночной цене не учитывается"));

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
            flags,
            SellerClassifier.Classify(listing, Search.Category, sellerListings),
            MessageDraftBuilder.Build(listing, Search.Category),
            exclusion);
    }
}

/// <summary>Строит анализ по всем собранным объявлениям поиска.</summary>
public sealed class SearchAnalyzer(AppSettings settings)
{
    public SearchAnalysis Analyze(SavedSearch search, IReadOnlyCollection<Listing> listings)
    {
        var filter = new MarketFilter(search);

        // В рыночную цену идут только объявления нужной модели и не копии
        var points = listings
            .Where(listing => filter.Check(listing) is null)
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
        return new SearchAnalysis(search, market, filter, SellerClassifier.CountListingsBySeller(listings), settings);
    }

    private static GroupStats BuildStats(string key, IEnumerable<decimal> values)
    {
        var all = values.ToList();
        var kept = Statistics.WithoutOutliers(all);
        return new GroupStats(key, kept.Count, Statistics.Median(kept)!.Value, kept.Min(), kept.Max(), Outliers: all.Count - kept.Count);
    }
}
