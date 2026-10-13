using AvitoMonitor.Analysis;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Tests;

public class SellerClassifierTests
{
    [Fact]
    public void NamedShopPageIsShop()
    {
        var listing = new Listing { Title = "iPhone 15", SellerUrl = "https://www.avito.ru/brands/smart-butik" };

        Assert.Equal(SellerKind.Shop, SellerClassifier.Classify(listing, SearchCategory.General, 1).Kind);
    }

    [Theory]
    [InlineData("https://www.avito.ru/brands/smart-butik", true)]
    [InlineData("https://www.avito.ru/brands/ofisjobsa-spb?src=search_seller_info", true)]
    [InlineData("https://www.avito.ru/brands/i12345678", false)]
    [InlineData("https://www.avito.ru/brands/0a1b2c3d4e5f60718293a4b5c6d7e8f9", false)]
    [InlineData("https://www.avito.ru/user/abc/profile", false)]
    [InlineData(null, false)]
    public void RecognizesNamedShopPage(string? url, bool expected) =>
        Assert.Equal(expected, SellerClassifier.HasNamedShopPage(url));

    [Fact]
    public void OrdinaryProfileIsNotCompanyByItself()
    {
        var listing = new Listing { Title = "iPhone 15", SellerUrl = "https://www.avito.ru/brands/i12345678", SellerReviews = 3 };

        Assert.Equal(SellerKind.Private, SellerClassifier.Classify(listing, SearchCategory.General, 1).Kind);
    }

    [Fact]
    public void CompanyLabelFromListingPageIsShop()
    {
        // На странице объявления у профиля /brands/i... может стоять «Компания»
        var listing = new Listing { Title = "iPhone 15", SellerUrl = "https://www.avito.ru/brands/i12345678", SellerLabel = "Компания" };

        var verdict = SellerClassifier.Classify(listing, SearchCategory.General, 1);

        Assert.Equal(SellerKind.Shop, verdict.Kind);
        Assert.Contains("Компания", verdict.Reasons[0]);
    }

    [Fact]
    public void HundredsOfReviewsLookLikeReseller()
    {
        var listing = new Listing { Title = "iPhone 15", SellerUrl = "https://www.avito.ru/brands/i12345678", SellerReviews = 1962 };

        var verdict = SellerClassifier.Classify(listing, SearchCategory.General, 1);

        Assert.Equal(SellerKind.HiddenShop, verdict.Kind);
        Assert.Contains(verdict.Reasons, r => r.Contains("1962 отзывов"));
    }

    [Fact]
    public void AvitoBadgesAreNotSellerWords()
    {
        var listing = new Listing { Title = "iPhone 15", Badges = "Скидка на доставку | Оплата частями | Только доставка" };

        Assert.Equal(SellerKind.Private, SellerClassifier.Classify(listing, SearchCategory.General, 1).Kind);
    }

    [Fact]
    public void PrivateSellerWithManyListingsAndShopWordsIsHiddenShop()
    {
        var listing = new Listing
        {
            Title = "iPhone 15 128 ГБ",
            Description = "Все цвета в наличии, рассрочка, trade-in",
            SellerName = "Алексей",
            SellerUrl = "https://www.avito.ru/user/abc/profile",
        };

        var verdict = SellerClassifier.Classify(listing, SearchCategory.General, sellerListingsInSearch: 7);

        Assert.Equal(SellerKind.HiddenShop, verdict.Kind);
        Assert.Contains(verdict.Reasons, r => r.Contains("7 объявлений"));
    }

    [Fact]
    public void OrdinaryPrivateSeller()
    {
        var listing = new Listing { Title = "iPhone 15", Description = "Продаю свой телефон, на гарантии", SellerName = "Иван" };

        Assert.Equal(SellerKind.Private, SellerClassifier.Classify(listing, SearchCategory.General, 1).Kind);
    }

    [Fact]
    public void OwnerPhraseIsNotAgentWord()
    {
        var listing = new Listing
        {
            Title = "2-к. квартира, 54 м², 5/9 эт.",
            Description = "Продаю от собственника, агентам не беспокоить!",
            SellerName = "Мария",
        };

        var verdict = SellerClassifier.Classify(listing, SearchCategory.RealEstate, 1);

        Assert.Equal(SellerKind.Owner, verdict.Kind);
        Assert.Contains(verdict.Reasons, r => r.Contains("агентам не беспокоить"));
    }

    [Fact]
    public void AgentPretendingToBeOwner()
    {
        var listing = new Listing
        {
            Title = "2-к. квартира, 54 м², 5/9 эт.",
            Description = "Собственник. Полное юридическое сопровождение сделки, без комиссии для покупателя",
            SellerName = "Недвижимость СПб",
            Badges = "Собственник",
        };

        Assert.Equal(SellerKind.HiddenAgent, SellerClassifier.Classify(listing, SearchCategory.RealEstate, 1).Kind);
    }

    [Fact]
    public void CountsListingsBySeller()
    {
        Listing[] listings =
        [
            new() { SellerUrl = "u1" }, new() { SellerUrl = "u1" }, new() { SellerName = "Пётр" }, new(),
        ];

        var counts = SellerClassifier.CountListingsBySeller(listings);

        Assert.Equal(2, counts["u1"]);
        Assert.Equal(1, counts["Пётр"]);
    }
}
