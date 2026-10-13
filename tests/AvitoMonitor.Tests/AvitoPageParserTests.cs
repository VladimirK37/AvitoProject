using AvitoMonitor.Avito;

namespace AvitoMonitor.Tests;

public class AvitoPageParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ParsesListingsFromSearchPage()
    {
        var page = AvitoPageParser.ParseSearchPage(Fixture("search-page.html"));

        Assert.Equal(["1111111111", "2222222222", "3333333333"], page.Items.Select(i => i.Id));
        Assert.Equal(868, page.TotalCount);
        Assert.True(page.HasNextPage);

        var first = page.Items[0];
        Assert.Equal("iPhone 15, 128 ГБ", first.Title);
        Assert.Equal(45000m, first.Price);
        Assert.Equal("https://www.avito.ru/sankt-peterburg/telefony/iphone_15_128_gb_1111111111", first.Url);
        Assert.Equal("Отличное состояние, полный комплект", first.Description);
        Assert.Equal("Санкт-Петербург, м. Невский проспект", first.Address);
        Assert.Equal("2 часа назад", first.PublishedText);
        Assert.Equal("Иван", first.SellerName);
        Assert.Equal("https://www.avito.ru/user/abc123/profile", first.SellerUrl);
    }

    [Fact]
    public void ReadsPriceFromTextAndCompanyLink()
    {
        var company = AvitoPageParser.ParseSearchPage(Fixture("search-page.html")).Items[1];

        Assert.Equal(70000m, company.Price);
        Assert.Equal("Apple Store SPB", company.SellerName);
        Assert.Contains("/brands/", company.SellerUrl);
        Assert.Equal("Гарантия", company.Badges);
    }

    [Fact]
    public void MissingPriceIsNull()
    {
        var item = AvitoPageParser.ParseSearchPage(Fixture("search-page.html")).Items[2];

        Assert.Null(item.Price);
    }

    [Fact]
    public void LastPageHasNoNextButton()
    {
        const string html = """
            <div data-marker="item" data-item-id="1234567"><a data-marker="item-title" href="/x/y_1234567">A</a></div>
            <span data-marker="pagination-button/page(5)">5</span>
            """;

        Assert.False(AvitoPageParser.ParseSearchPage(html).HasNextPage);
    }

    [Fact]
    public void UnknownPaginationIsNull()
    {
        const string html = """<div data-marker="item" data-item-id="1234567"><a data-marker="item-title" href="/x/y_1234567">A</a></div>""";

        Assert.Null(AvitoPageParser.ParseSearchPage(html).HasNextPage);
    }

    [Fact]
    public void RecognizesCheckPage()
    {
        Assert.True(AvitoPageParser.IsCheckPage("Доступ ограничен: проблема с IP", "<html></html>"));
        Assert.True(AvitoPageParser.IsCheckPage("Авито", "<div class='firewall-container'><div id='captcha'></div></div>"));
        Assert.False(AvitoPageParser.IsCheckPage("Купить iPhone", Fixture("search-page.html")));
    }

    [Fact]
    public void ParsesListingPageDetails()
    {
        const string html = """
            <div data-marker="item-view/item-description"><p>Продаю сам, агентам не беспокоить.</p></div>
            <div data-marker="seller-info/name"><a href="/user/xyz/profile?src=item">Мария</a></div>
            <div data-marker="seller-info/label">Частное лицо</div>
            """;

        var details = AvitoPageParser.ParseListingPage(html);

        Assert.Equal("Продаю сам, агентам не беспокоить.", details.Description);
        Assert.Equal("Мария", details.SellerName);
        Assert.Equal("Частное лицо", details.SellerLabel);
        Assert.Equal("https://www.avito.ru/user/xyz/profile", details.SellerUrl);
    }
}
