using AvitoMonitor.Analysis;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Tests;

/// <summary>Копии, другие модели, шумоподавление и выбросы — на примере выдачи «airpods 4».</summary>
public class MarketFilteringTests
{
    [Theory]
    [InlineData("AirPods 4 «Оригинал» (2026) + Шумоподавление", "«оригинал»")]
    [InlineData("AirPods 4 \"Оригинал\" Шумоподавление", "\"оригинал\"")]
    [InlineData("Airpods 4 Luxe (Новинка 2026)", "luxe")]
    [InlineData("AirPods 4 Airoha (2026) Гарантия 365 дней", "airoha")]
    [InlineData("AirPods 4 FCO+ (Original, Airoha 1571AM)", "fco")]
    public void RecognizesReplicas(string title, string expectedMarker) =>
        Assert.Equal(expectedMarker, ReplicaDetector.FindMarker(new Listing { Title = title }, SearchCategory.General));

    [Theory]
    [InlineData("iPhone 15, 128 ГБ, оригинал, чек")]
    [InlineData("AirPods 4 не копия, оригинал")]
    [InlineData("Apple AirPods 4 ANC")]
    public void OrdinaryListingsAreNotReplicas(string title) =>
        Assert.Null(ReplicaDetector.FindMarker(new Listing { Title = title }, SearchCategory.General));

    [Fact]
    public void LuxuryFlatIsNotReplica() =>
        Assert.Null(ReplicaDetector.FindMarker(new Listing { Title = "2-к. квартира, 80 м², ремонт люкс" }, SearchCategory.RealEstate));

    [Theory]
    [InlineData("AirPods Pro 3 Premium 2026", "airpods 4")]
    [InlineData("Наушники Apple AirPods 4/4 ANC/Max/pro 2/pro 3", "airpods 4")]
    [InlineData("iPhone 15 Pro Max, 256 ГБ", "iphone 15")]
    public void RecognizesOtherModels(string title, string searchText) =>
        Assert.NotNull(ModelMatcher.FindMismatch(title, searchText));

    [Theory]
    [InlineData("AirPods4 ANC", "airpods 4")]
    [InlineData("Наушники Apple AirPods 4 с шумоподавлением", "airpods 4")]
    [InlineData("iPhone 15 Pro Max, 256 ГБ", "iphone 15 pro max")]
    [InlineData("Что угодно", null)]
    public void SameModelMatches(string title, string? searchText) =>
        Assert.Null(ModelMatcher.FindMismatch(title, searchText));

    [Theory]
    [InlineData("Apple AirPods 4 ANC", GroupKeys.NoiseCancellingGroup)]
    [InlineData("Наушники Apple AirPods 4 с шумоподавлением", GroupKeys.NoiseCancellingGroup)]
    [InlineData("Apple AirPods 4", "Без уточнения")]
    [InlineData("iPhone 15, 128 ГБ", "128 ГБ")]
    public void SplitsNoiseCancellingVersions(string title, string expectedGroup)
    {
        var search = new SavedSearch { Category = SearchCategory.General, Sources = [new SearchSource()] };

        Assert.Equal(expectedGroup, GroupKeys.For(new Listing { Title = title }, search).Group);
    }

    [Fact]
    public void DropsOutliers()
    {
        decimal[] prices = [1045, 2375, 8500, 9800, 10350, 10390, 10390, 10890, 13150];

        Assert.Equal([8500m, 9800m, 10350m, 10390m, 10390m, 10890m, 13150m], Statistics.WithoutOutliers(prices));
    }

    [Fact]
    public void AirPodsMarketIgnoresReplicasAndOtherModels()
    {
        var search = new SavedSearch
        {
            Category = SearchCategory.General,
            Sources = [new SearchSource { Label = "airpods 4", Url = "https://www.avito.ru/sankt-peterburg?q=airpods+4" }],
        };

        var listings = new List<Listing>
        {
            // Оригиналы без шумоподавления
            Item("Apple AirPods 4", 10390), Item("Наушники Apple Airpods 4", 10390), Item("Apple AirPods 4 новые", 10350),
            Item("Наушники Apple AirPods 4", 9800), Item("Apple airpods 4", 8500), Item("Apple AirPods 4", 13150),
            Item("Apple airpods 4", 10890),
            // Оригиналы с шумоподавлением
            Item("Apple AirPods 4 ANC", 13640), Item("Apple AirPods 4 ANC", 13390),
            Item("Наушники Apple AirPods 4 ANC с шумоподавлением", 13550),
            Item("Наушники Apple AirPods 4 с шумоподавлением", 12990), Item("Наушники Apple Airpods 4 ANC", 13290),
            // Копии
            Item("AirPods 4 «Оригинал» (2026) + Шумоподавление", 1341), Item("Airpods 4 Luxe (Новинка 2026)", 1470),
            Item("AirPods 4 Premium 2026 (Гарантия + Доставка)", 1666),
            // Дешёвые без явных признаков — уйдут в выбросы
            Item("AirPods 4 (Гарантия + Чехол)", 2375), Item("AirPods 4", 1045),
            // Другая модель
            Item("Apple AirPods Pro3 лучшие на рынке", 3890),
        };

        var analysis = new SearchAnalyzer(new AppSettings()).Analyze(search, listings);

        var regular = analysis.Market.Groups["Без уточнения"];
        Assert.Equal(10390m, regular.Median);
        Assert.Equal(2, regular.Outliers);
        Assert.Equal(13390m, analysis.Market.Groups[GroupKeys.NoiseCancellingGroup].Median);

        var replica = analysis.Evaluate(listings[12]);
        Assert.Equal(ExclusionKind.Replica, replica.Exclusion?.Kind);
        Assert.DoesNotContain(replica.Flags, f => f.Level == FlagLevel.Danger);

        Assert.Equal(ExclusionKind.OtherModel, analysis.Evaluate(listings[^1]).Exclusion?.Kind);

        // Дёшево без объяснения — по-прежнему «возможен обман»
        var suspicious = analysis.Evaluate(listings[16]);
        Assert.Null(suspicious.Exclusion);
        Assert.Contains(suspicious.Flags, f => f.Level == FlagLevel.Danger);
    }

    private static int _nextId;

    private static Listing Item(string title, decimal price)
    {
        var id = Interlocked.Increment(ref _nextId).ToString();
        return new Listing { Id = id, Title = title, Price = price, Url = $"https://www.avito.ru/x/y_{id}", SourceLabel = "airpods 4" };
    }
}
