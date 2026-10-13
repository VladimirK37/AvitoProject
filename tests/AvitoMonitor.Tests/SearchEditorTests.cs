using AvitoMonitor.Avito;
using AvitoMonitor.Domain;
using AvitoMonitor.Workflows;

namespace AvitoMonitor.Tests;

/// <summary>Что происходит с собранными объявлениями при правке поиска.</summary>
public class SearchEditorTests
{
    private const string Spb = "sankt-peterburg";

    private static readonly SearchRequest Iphone15 = new("iphone 15", Spb);
    private static readonly SearchRequest Iphone16 = new("iphone 16", Spb);
    private static readonly SearchRequest Iphone17 = new("iphone 17", Spb);

    [Fact]
    public void RenameKeepsListingsAndUpdatesTheirLabel()
    {
        var (search, listings) = TwoSources();

        var result = SearchEditor.Apply(search, "Айфоны", SearchCategory.General,
            [Keywords("iPhone 15", "Пятнадцатый", Iphone15), Keywords("iPhone 16", "iPhone 16", Iphone16)], listings);

        Assert.Equal("Айфоны", search.Name);
        Assert.Equal(["Пятнадцатый", "iPhone 16"], search.Sources.Select(s => s.Label));
        Assert.Equal(4, listings.Count);
        Assert.Equal(2, listings.Values.Count(l => l.SourceLabel == "Пятнадцатый"));
        Assert.Equal(2, result.RelabeledListings.Count);
        Assert.Empty(result.RemovedListingIds);
        Assert.Equal(2, result.Changes.Count);
    }

    [Fact]
    public void ChangedQueryRemovesOldListingsAndRestartsScan()
    {
        var (search, listings) = TwoSources();
        search.Sources[0].NextPageToScan = 7;
        search.Sources[0].ScanCompleted = true;

        var result = SearchEditor.Apply(search, search.Name, search.Category,
            [Keywords("iPhone 15", "iPhone 15", Iphone17), Keywords("iPhone 16", "iPhone 16", Iphone16)], listings);

        var source = search.Sources[0];
        Assert.Equal("iphone 17", source.Query);
        Assert.Equal(Iphone17.ToUrl().ToString(), source.Url);
        Assert.Equal(1, source.NextPageToScan);
        Assert.False(source.ScanCompleted);
        Assert.DoesNotContain(listings.Values, l => l.SourceLabel == "iPhone 15");
        Assert.Equal(["1", "2"], result.RemovedListingIds.Order());
    }

    [Fact]
    public void ChangedCityOrPriceAlsoRestartsScan()
    {
        var (search, listings) = TwoSources();
        var cheaperInMoscow = new SearchRequest("iphone 15", "moskva", PriceMax: 50000);

        SearchEditor.Apply(search, search.Name, search.Category,
            [Keywords("iPhone 15", "iPhone 15", cheaperInMoscow), Keywords("iPhone 16", "iPhone 16", Iphone16)], listings);

        Assert.Equal("moskva", search.Sources[0].City);
        Assert.Equal(50000, search.Sources[0].PriceMax);
        Assert.Equal("https://www.avito.ru/moskva?q=iphone+15&pmax=50000", search.Sources[0].Url);
        Assert.Equal(2, listings.Count);
    }

    [Fact]
    public void RemovedSourceDeletesItsListingsAndNewSourceStartsFromScratch()
    {
        var (search, listings) = TwoSources();

        var result = SearchEditor.Apply(search, search.Name, search.Category,
            [Keywords("iPhone 16", "iPhone 16", Iphone16), Keywords(null, "iPhone 17", Iphone17)], listings);

        Assert.Equal(["iPhone 16", "iPhone 17"], search.Sources.Select(s => s.Label));
        Assert.Equal("iphone 17", search.Sources[1].Query);
        Assert.Equal(1, search.Sources[1].NextPageToScan);
        Assert.All(listings.Values, l => Assert.Equal("iPhone 16", l.SourceLabel));
        Assert.Equal(2, result.RemovedListingIds.Count);
    }

    [Fact]
    public void SwappedLabelsStayConsistent()
    {
        var (search, listings) = TwoSources();
        var fifteenIds = listings.Values.Where(l => l.SourceLabel == "iPhone 15").Select(l => l.Id).ToList();

        SearchEditor.Apply(search, search.Name, search.Category,
            [Keywords("iPhone 15", "iPhone 16", Iphone15), Keywords("iPhone 16", "iPhone 15", Iphone16)], listings);

        Assert.All(fifteenIds, id => Assert.Equal("iPhone 16", listings[id].SourceLabel));
    }

    [Fact]
    public void NoChangesWhenNothingEdited()
    {
        var (search, listings) = TwoSources();

        var result = SearchEditor.Apply(search, search.Name, search.Category,
            [Keywords("iPhone 15", "iPhone 15", Iphone15), Keywords("iPhone 16", "iPhone 16", Iphone16)], listings);

        Assert.Empty(result.Changes);
        Assert.Equal(4, listings.Count);
    }

    [Fact]
    public void LegacyUrlSourceCanStillBeEdited()
    {
        var search = new SavedSearch
        {
            Name = "Старый",
            Sources = [new SearchSource { Label = "ссылка", Url = "https://www.avito.ru/sankt-peterburg/telefony?q=iphone+15" }],
        };
        var listings = new Dictionary<string, Listing> { ["1"] = new() { Id = "1", SourceLabel = "ссылка" } };

        SearchEditor.Apply(search, search.Name, search.Category,
            [SourceEdit.ForUrl("ссылка", "ссылка", "https://www.avito.ru/moskva/telefony?q=iphone+15")], listings);

        Assert.Null(search.Sources[0].Query);
        Assert.Equal("https://www.avito.ru/moskva/telefony?q=iphone+15", search.Sources[0].Url);
        Assert.Empty(listings);
    }

    [Fact]
    public void ValidationFindsMistakes()
    {
        var errors = SearchEditor.Validate(" ",
        [
            Keywords(null, "", new SearchRequest(" ", Spb)),
            Keywords(null, "A", new SearchRequest("iphone", Spb, PriceMin: 50000, PriceMax: 10000)),
            SourceEdit.ForUrl(null, "A", "не ссылка"),
        ]);

        Assert.Contains(errors, e => e.Contains("название поиска"));
        Assert.Contains(errors, e => e.Contains("Запрос №1") && e.Contains("короткое название"));
        Assert.Contains(errors, e => e.Contains("Запрос №1") && e.Contains("что искать"));
        Assert.Contains(errors, e => e.Contains("Запрос №2") && e.Contains("«от» больше"));
        Assert.Contains(errors, e => e.Contains("Запрос №3") && e.Contains("ссылку"));
        Assert.Contains(errors, e => e.Contains("«A»"));
    }

    private static SourceEdit Keywords(string? originalLabel, string label, SearchRequest request) =>
        SourceEdit.ForKeywords(originalLabel, label, request);

    private static (SavedSearch Search, Dictionary<string, Listing> Listings) TwoSources()
    {
        var search = new SavedSearch
        {
            Id = "test",
            Name = "iPhone",
            Category = SearchCategory.General,
            Sources =
            [
                Source("iPhone 15", Iphone15),
                Source("iPhone 16", Iphone16),
            ],
        };

        var listings = new Dictionary<string, Listing>();
        foreach (var (id, label) in new[] { ("1", "iPhone 15"), ("2", "iPhone 15"), ("3", "iPhone 16"), ("4", "iPhone 16") })
            listings[id] = new Listing { Id = id, Title = label, SourceLabel = label };

        return (search, listings);
    }

    private static SearchSource Source(string label, SearchRequest request) => new()
    {
        Label = label,
        Query = request.Query,
        City = request.City,
        Url = request.ToUrl().ToString(),
    };
}
