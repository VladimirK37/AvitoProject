using AvitoMonitor.Avito;
using AvitoMonitor.Domain;
using AvitoMonitor.History;
using AvitoMonitor.Persistence;

namespace AvitoMonitor.Tests;

/// <summary>Поиск по словам, перенос данных в таблицы базы и ключи Elasticsearch — без запуска самих баз.</summary>
public class StorageTests
{
    [Theory]
    [InlineData("airpods 4", "sankt-peterburg", null, null, "https://www.avito.ru/sankt-peterburg?q=airpods+4")]
    [InlineData("  airpods 4 ", "moskva", 7000, null, "https://www.avito.ru/moskva?q=airpods+4&pmin=7000")]
    [InlineData("квартира студия", "rossiya", 1000000, 5000000, "https://www.avito.ru/rossiya?q=%D0%BA%D0%B2%D0%B0%D1%80%D1%82%D0%B8%D1%80%D0%B0+%D1%81%D1%82%D1%83%D0%B4%D0%B8%D1%8F&pmin=1000000&pmax=5000000")]
    public void BuildsSearchUrlFromKeywords(string query, string city, int? min, int? max, string expected) =>
        // AbsoluteUri — адрес в том виде, в каком он уходит на сайт (русские буквы закодированы)
        Assert.Equal(expected, new SearchRequest(query, city, min, max).ToUrl().AbsoluteUri);

    [Fact]
    public void BuiltUrlPassesValidation()
    {
        var url = new SearchRequest("iphone 15", "sankt-peterburg").ToUrl();

        Assert.True(AvitoUrl.TryParseSearchUrl(url.ToString(), out _, out _));
        Assert.Equal("iphone 15", AvitoUrl.GetSearchText(url));
    }

    [Fact]
    public void DescribesRequest() =>
        Assert.Equal("«airpods 4» · Москва · от 7 000 ₽",
            new SearchRequest("airpods 4", "moskva", 7000).Describe().Replace(' ', ' ').Replace(',', ' '));

    [Fact]
    public void SearchSurvivesRoundTripThroughEntity()
    {
        var search = new SavedSearch
        {
            Id = "20261013-120000-000",
            Name = "airpods 4",
            Category = SearchCategory.General,
            CreatedAt = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Local),
            LastScanAt = new DateTime(2026, 10, 13, 12, 30, 0, DateTimeKind.Local),
            WatchEnabled = true,
            Sources =
            [
                new SearchSource { Label = "A", Query = "airpods 4", City = "moskva", PriceMin = 7000, Url = "u1", NextPageToScan = 3 },
                new SearchSource { Label = "B", Url = "u2", ScanCompleted = true, TotalCountOnAvito = 42 },
            ],
        };

        var entity = new SearchEntity();
        EntityMapper.CopyToEntity(search, entity);
        var back = EntityMapper.ToDomain(entity);

        Assert.Equal(DateTimeKind.Utc, entity.CreatedAt.Kind);
        Assert.Equal(search.CreatedAt, back.CreatedAt);
        Assert.Equal(search.LastScanAt, back.LastScanAt);
        Assert.True(back.WatchEnabled);
        Assert.Equal(["A", "B"], back.Sources.Select(s => s.Label));
        Assert.Equal("airpods 4", back.Sources[0].Query);
        Assert.Equal(7000, back.Sources[0].PriceMin);
        Assert.Equal(3, back.Sources[0].NextPageToScan);
        Assert.True(back.Sources[1].ScanCompleted);
        Assert.Equal(42, back.Sources[1].TotalCountOnAvito);
    }

    [Fact]
    public void ListingSurvivesRoundTripThroughEntity()
    {
        var listing = new Listing
        {
            Id = "7850117997",
            Title = "iPhone 15, 128 ГБ",
            Url = "https://www.avito.ru/x/y_7850117997",
            Price = 45990,
            SellerReviews = 1962,
            Badges = "Надёжный продавец",
            SourceLabel = "iphone 15",
            FirstSeenAt = DateTime.Now,
            LastSeenAt = DateTime.Now,
            DetailsLoaded = true,
        };

        var entity = new ListingEntity();
        EntityMapper.CopyToEntity("search-1", listing, entity);
        var back = EntityMapper.ToDomain(entity);

        Assert.Equal("search-1", entity.SearchId);
        Assert.Equal("7850117997", entity.AvitoId);
        Assert.Equal(listing.Id, back.Id);
        Assert.Equal(listing.Price, back.Price);
        Assert.Equal(listing.SellerReviews, back.SellerReviews);
        Assert.Equal(listing.FirstSeenAt, back.FirstSeenAt);
        Assert.True(back.DetailsLoaded);
    }

    [Fact]
    public void SameQueryGivesSameElasticDocument()
    {
        var a = ElasticQueryIndex.DocumentId(new SearchRequest("AirPods 4 ", "sankt-peterburg"));
        var b = ElasticQueryIndex.DocumentId(new SearchRequest("airpods 4", "sankt-peterburg"));
        var otherCity = ElasticQueryIndex.DocumentId(new SearchRequest("airpods 4", "moskva"));

        Assert.Equal(a, b);
        Assert.NotEqual(a, otherCity);
    }
}
