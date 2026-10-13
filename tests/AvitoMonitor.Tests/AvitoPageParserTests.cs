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
        Assert.Equal(3190, page.TotalCount);
        Assert.True(page.HasNextPage);

        var first = page.Items[0];
        Assert.Equal("iPhone 15, 128 ГБ", first.Title);
        Assert.Equal(45000m, first.Price);
        Assert.Equal("https://www.avito.ru/sankt-peterburg/telefony/iphone_15_128_gb_1111111111", first.Url);
        Assert.Equal("Отличное", first.Description);
        Assert.Equal("Достоевская, до 5 мин.", first.Address);
        Assert.Equal("2 часа назад", first.PublishedText);
        Assert.Equal("Иван", first.SellerName);
        Assert.Equal("https://www.avito.ru/brands/i12345678", first.SellerUrl);
        Assert.Equal(12, first.SellerReviews);
    }

    [Fact]
    public void ReadsPriceFromTextShopPageAndSeparateBadges()
    {
        var shop = AvitoPageParser.ParseSearchPage(Fixture("search-page.html")).Items[1];

        Assert.Equal(70000m, shop.Price);
        Assert.Equal("Apple Store SPB", shop.SellerName);
        Assert.Equal("https://www.avito.ru/brands/apple-store-spb", shop.SellerUrl);
        Assert.Equal(1189, shop.SellerReviews);
        Assert.Equal("Цена ниже рыночной | Надёжный продавец", shop.Badges);
    }

    [Fact]
    public void FallsBackToOldLayoutSelectors()
    {
        var item = AvitoPageParser.ParseSearchPage(Fixture("search-page.html")).Items[2];

        Assert.Null(item.Price);
        Assert.Equal("Разбит экран, не включается", item.Description);
        Assert.Equal("Санкт-Петербург, м. Невский проспект", item.Address);
        Assert.Equal("https://www.avito.ru/user/abc123/profile", item.SellerUrl);
        Assert.Null(item.SellerReviews);
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

    [Theory]
    [InlineData("Доступ ограничен: проблема с IP")]
    [InlineData("Доступ ограничен: проверка безопасности")]
    public void RecognizesCheckPageByTitle(string title) =>
        Assert.True(AvitoPageParser.IsCheckPage(title, "<html></html>"));

    [Fact]
    public void RecognizesCheckPageByContent()
    {
        Assert.True(AvitoPageParser.IsCheckPage("Авито", "<div class='firewall-container'><div id='captcha'></div></div>"));
        Assert.False(AvitoPageParser.IsCheckPage("Купить iPhone", Fixture("search-page.html")));
    }

    [Fact]
    public void ParsesListingPageDetails()
    {
        // Так выглядит блок продавца на странице объявления (вёрстка октября 2026)
        const string html = """
            <div data-marker="item-view/item-description"><p>Продаю сам, агентам не беспокоить.</p></div>
            <div data-marker="item-view/seller-info">
              <div data-marker="seller-info/name"><a href="/brands/i87654321?src=item">Мария</a></div>
              <div data-marker="seller-info/label">Частное лицо</div>
              <div data-marker="seller-link/link-container"><a data-marker="seller-link/link" href="/brands/i87654321?src=item">Мария</a></div>
            </div>
            """;

        var details = AvitoPageParser.ParseListingPage(html);

        Assert.Equal("Продаю сам, агентам не беспокоить.", details.Description);
        Assert.Equal("Мария", details.SellerName);
        Assert.Equal("Частное лицо", details.SellerLabel);
        Assert.Equal("https://www.avito.ru/brands/i87654321", details.SellerUrl);
    }
}
