using AvitoMonitor.Domain;
using AvitoMonitor.Workflows;

namespace AvitoMonitor.Tests;

/// <summary>Что происходит с собранными объявлениями при правке поиска.</summary>
public class SearchEditorTests
{
    private const string Iphone15 = "https://www.avito.ru/sankt-peterburg/telefony?q=iphone+15";
    private const string Iphone16 = "https://www.avito.ru/sankt-peterburg/telefony?q=iphone+16";
    private const string Iphone17 = "https://www.avito.ru/sankt-peterburg/telefony?q=iphone+17";

    [Fact]
    public void RenameKeepsListingsAndUpdatesTheirLabel()
    {
        var (search, listings) = TwoSources();

        var changes = SearchEditor.Apply(search, "Айфоны", SearchCategory.General,
            [new SourceEdit("iPhone 15", "Пятнадцатый", Iphone15), new SourceEdit("iPhone 16", "iPhone 16", Iphone16)], listings);

        Assert.Equal("Айфоны", search.Name);
        Assert.Equal(["Пятнадцатый", "iPhone 16"], search.Sources.Select(s => s.Label));
        Assert.Equal(4, listings.Count);
        Assert.Equal(2, listings.Values.Count(l => l.SourceLabel == "Пятнадцатый"));
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void ChangedUrlRemovesOldListingsAndRestartsScan()
    {
        var (search, listings) = TwoSources();
        search.Sources[0].NextPageToScan = 7;
        search.Sources[0].ScanCompleted = true;

        SearchEditor.Apply(search, search.Name, search.Category,
            [new SourceEdit("iPhone 15", "iPhone 15", Iphone17), new SourceEdit("iPhone 16", "iPhone 16", Iphone16)], listings);

        Assert.Equal(Iphone17, search.Sources[0].Url);
        Assert.Equal(1, search.Sources[0].NextPageToScan);
        Assert.False(search.Sources[0].ScanCompleted);
        Assert.DoesNotContain(listings.Values, l => l.SourceLabel == "iPhone 15");
        Assert.Equal(2, listings.Count);
    }

    [Fact]
    public void RemovedSourceDeletesItsListingsAndNewSourceStartsFromScratch()
    {
        var (search, listings) = TwoSources();

        SearchEditor.Apply(search, search.Name, search.Category,
            [new SourceEdit("iPhone 16", "iPhone 16", Iphone16), new SourceEdit(null, "iPhone 17", Iphone17)], listings);

        Assert.Equal(["iPhone 16", "iPhone 17"], search.Sources.Select(s => s.Label));
        Assert.Equal(1, search.Sources[1].NextPageToScan);
        Assert.All(listings.Values, l => Assert.Equal("iPhone 16", l.SourceLabel));
    }

    [Fact]
    public void SwappedLabelsStayConsistent()
    {
        var (search, listings) = TwoSources();
        var fifteenIds = listings.Values.Where(l => l.SourceLabel == "iPhone 15").Select(l => l.Id).ToList();

        SearchEditor.Apply(search, search.Name, search.Category,
            [new SourceEdit("iPhone 15", "iPhone 16", Iphone15), new SourceEdit("iPhone 16", "iPhone 15", Iphone16)], listings);

        Assert.All(fifteenIds, id => Assert.Equal("iPhone 16", listings[id].SourceLabel));
    }

    [Fact]
    public void NoChangesWhenNothingEdited()
    {
        var (search, listings) = TwoSources();

        var changes = SearchEditor.Apply(search, search.Name, search.Category,
            [new SourceEdit("iPhone 15", "iPhone 15", Iphone15), new SourceEdit("iPhone 16", "iPhone 16", Iphone16)], listings);

        Assert.Empty(changes);
        Assert.Equal(4, listings.Count);
    }

    [Fact]
    public void ValidationFindsMistakes()
    {
        var errors = SearchEditor.Validate(" ",
        [
            new SourceEdit(null, "", "не ссылка"),
            new SourceEdit(null, "A", Iphone15),
            new SourceEdit(null, "A", Iphone16),
        ]);

        Assert.Contains(errors, e => e.Contains("название поиска"));
        Assert.Contains(errors, e => e.Contains("Ссылка №1") && e.Contains("короткое название"));
        Assert.Contains(errors, e => e.Contains("Ссылка №1") && e.Contains("ссылку"));
        Assert.Contains(errors, e => e.Contains("«A»"));
    }

    private static (SavedSearch Search, Dictionary<string, Listing> Listings) TwoSources()
    {
        var search = new SavedSearch
        {
            Id = "test",
            Name = "iPhone",
            Category = SearchCategory.General,
            Sources =
            [
                new SearchSource { Label = "iPhone 15", Url = Iphone15 },
                new SearchSource { Label = "iPhone 16", Url = Iphone16 },
            ],
        };

        var listings = new Dictionary<string, Listing>();
        foreach (var (id, label) in new[] { ("1", "iPhone 15"), ("2", "iPhone 15"), ("3", "iPhone 16"), ("4", "iPhone 16") })
            listings[id] = new Listing { Id = id, Title = label, SourceLabel = label };

        return (search, listings);
    }
}
