using AvitoMonitor.Analysis;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Tests;

public class AnalysisTests
{
    [Fact]
    public void MedianOfOddAndEvenCounts()
    {
        Assert.Equal(3m, Statistics.Median([5, 1, 3]));
        Assert.Equal(2.5m, Statistics.Median([4, 1, 3, 2]));
        Assert.Null(Statistics.Median([]));
    }

    [Fact]
    public void ComparesWithMedianOfOwnGroup()
    {
        var search = new SavedSearch { Category = SearchCategory.General, Sources = [new SearchSource()] };
        var listings = new List<Listing>();
        for (var i = 0; i < 5; i++)
            listings.Add(Phone($"{i}", "iPhone 15, 128 ГБ", 60_000));
        for (var i = 5; i < 10; i++)
            listings.Add(Phone($"{i}", "iPhone 15, 256 ГБ", 80_000));
        var cheap = Phone("cheap", "iPhone 15, 128 ГБ", 45_000);
        listings.Add(cheap);

        var analysis = new SearchAnalyzer(new AppSettings()).Analyze(search, listings);
        var result = analysis.Evaluate(cheap);

        Assert.Equal("128 ГБ", result.GroupKey);
        Assert.Equal(60_000m, result.Reference!.Median);
        Assert.Equal(-25, result.DeviationPercent!.Value, precision: 1);
        Assert.Contains(result.Flags, f => f.Text.Contains("выгодно"));
    }

    [Fact]
    public void SmallGroupFallsBackToWholeSearch()
    {
        var search = new SavedSearch { Category = SearchCategory.General, Sources = [new SearchSource()] };
        var listings = Enumerable.Range(0, 6).Select(i => Phone($"{i}", "iPhone 15, 128 ГБ", 60_000)).ToList();
        var rare = Phone("rare", "iPhone 15, 512 ГБ", 90_000);
        listings.Add(rare);

        var result = new SearchAnalyzer(new AppSettings()).Analyze(search, listings).Evaluate(rare);

        Assert.True(result.ReferenceIsWholeSearch);
        Assert.Equal(GroupKeys.AllListings, result.Reference!.Key);
    }

    [Fact]
    public void MultiSearchKeepsModelsApart()
    {
        var search = new SavedSearch
        {
            Category = SearchCategory.General,
            Sources = [new SearchSource { Label = "iPhone 15" }, new SearchSource { Label = "iPhone 17" }],
        };
        var listing = new Listing { Title = "iPhone 256 ГБ", SourceLabel = "iPhone 17" };

        Assert.Equal(new GroupKeys("iPhone 17 · 256 ГБ", "iPhone 17"), GroupKeys.For(listing, search));
    }

    [Fact]
    public void GroupsAreOrderedBySize()
    {
        string[] keys = ["1 ТБ", "256 ГБ", "128 ГБ", "Без уточнения"];

        Assert.Equal(["128 ГБ", "256 ГБ", "1 ТБ", "Без уточнения"], GroupKeys.OrderByGroup(keys, k => k));
    }

    [Fact]
    public void VeryCheapWithoutReasonLooksLikeScam()
    {
        var flags = RedFlagDetector.Detect(Phone("1", "iPhone 15, 128 ГБ", 20_000), deviationPercent: -67, cheapThresholdPercent: 40);

        Assert.Contains(flags, f => f.Level == FlagLevel.Danger && f.Text.Contains("Подозрительно дёшево"));
    }

    [Fact]
    public void CheapBecauseBrokenIsExplained()
    {
        var flags = RedFlagDetector.Detect(Phone("1", "iPhone 15 разбит экран", 4_500), deviationPercent: -87, cheapThresholdPercent: 40);

        Assert.Contains(flags, f => f.Level == FlagLevel.Warning && f.Text.Contains("Битый"));
        Assert.DoesNotContain(flags, f => f.Level == FlagLevel.Danger);
    }

    [Fact]
    public void NegationsDoNotTriggerFlags()
    {
        var listing = Phone("1", "iPhone 15", 60_000);
        listing.Description = "Не битый, не заблокирован, без предоплаты";

        Assert.Empty(RedFlagDetector.Detect(listing, deviationPercent: 0, cheapThresholdPercent: 40));
    }

    [Fact]
    public void PrepaymentRequestIsDanger()
    {
        var listing = Phone("1", "iPhone 15", 60_000);
        listing.Description = "Отправлю СДЭК, только предоплата на карту";

        Assert.Contains(RedFlagDetector.Detect(listing, null, 40), f => f.Level == FlagLevel.Danger);
    }

    private static Listing Phone(string id, string title, decimal price) =>
        new() { Id = id, Title = title, Price = price, Url = $"https://www.avito.ru/x/y_{id}" };
}
