using AvitoMonitor.Avito;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Tests;

public class AvitoUrlTests
{
    [Fact]
    public void NormalizesMobileLinkAndDropsPageNumber()
    {
        Assert.True(AvitoUrl.TryParseSearchUrl("https://m.avito.ru/sankt-peterburg/telefony?q=iphone+15&p=3#top", out var url, out _));

        Assert.Equal("https://www.avito.ru/sankt-peterburg/telefony?q=iphone+15", url.ToString());
    }

    [Theory]
    [InlineData("просто текст")]
    [InlineData("https://www.ozon.ru/search?text=iphone")]
    [InlineData("https://www.avito.ru/")]
    [InlineData("https://www.avito.ru/sankt-peterburg/telefony/iphone_15_128_gb_4378912345")]
    public void RejectsNonSearchLinks(string input)
    {
        Assert.False(AvitoUrl.TryParseSearchUrl(input, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void SetsPageAndSortWithoutTouchingOtherParameters()
    {
        var url = new Uri("https://www.avito.ru/sankt-peterburg/telefony?q=iphone&pmax=60000");

        var result = AvitoUrl.WithPage(AvitoUrl.SortedByDate(url), 2).ToString();

        Assert.Equal("https://www.avito.ru/sankt-peterburg/telefony?q=iphone&pmax=60000&s=104&p=2", result);
        Assert.Equal("https://www.avito.ru/sankt-peterburg/telefony?q=iphone&pmax=60000", AvitoUrl.WithPage(url, 1).ToString());
    }

    [Theory]
    [InlineData("https://www.avito.ru/sankt-peterburg/kvartiry/prodam-ASgBAgICAUSSA8YQ", SearchCategory.RealEstate)]
    [InlineData("https://www.avito.ru/moskva/avtomobili/toyota/camry", SearchCategory.Cars)]
    [InlineData("https://www.avito.ru/sankt-peterburg/telefony?q=iphone", SearchCategory.General)]
    public void DetectsCategory(string url, SearchCategory expected) =>
        Assert.Equal(expected, AvitoUrl.DetectCategory(new Uri(url)));

    [Theory]
    [InlineData("https://www.avito.ru/sankt-peterburg/telefony?q=iphone+15", "iphone 15")]
    [InlineData("https://www.avito.ru/sankt-peterburg/telefony/mobilnye_telefony/apple/iphone_15-ASgBAgICA0SywA2S", "iphone 15")]
    public void SuggestsName(string url, string expected) =>
        Assert.Equal(expected, AvitoUrl.SuggestName(new Uri(url)));

    [Theory]
    [InlineData("/spb/telefony/iphone_15_128_gb_4378912345?context=abc", "4378912345")]
    [InlineData("/spb/telefony/mobilnye_telefony", null)]
    public void ExtractsItemId(string href, string? expected) =>
        Assert.Equal(expected, AvitoUrl.ExtractItemId(href));
}
