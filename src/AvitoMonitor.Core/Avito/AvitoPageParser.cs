using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AvitoMonitor.Analysis;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Avito;

/// <summary>Результат разбора одной страницы выдачи.</summary>
/// <param name="Items">Объявления на странице.</param>
/// <param name="TotalCount">Сколько всего объявлений по поиску (число в заголовке), если удалось найти.</param>
/// <param name="HasNextPage">Есть ли следующая страница; null — кнопок страниц не нашли, точно неизвестно.</param>
public sealed record SearchPage(IReadOnlyList<Listing> Items, int? TotalCount, bool? HasNextPage);

/// <summary>Данные со страницы самого объявления.</summary>
public sealed record ListingDetails(string? Description, string? SellerName, string? SellerLabel, string? SellerUrl)
{
    public void ApplyTo(Listing listing)
    {
        listing.Description = Description ?? listing.Description;
        listing.SellerName = SellerName ?? listing.SellerName;
        listing.SellerLabel = SellerLabel ?? listing.SellerLabel;
        listing.SellerUrl = SellerUrl ?? listing.SellerUrl;
        listing.DetailsLoaded = true;
    }
}

/// <summary>
/// Достаёт объявления из HTML страниц Авито.
///
/// Авито размечает элементы атрибутом data-marker (например, data-marker="item" — карточка
/// объявления). Эти метки меняются редко, поэтому опираемся на них. Если Авито
/// поменяет вёрстку — править нужно только этот файл.
/// </summary>
public static class AvitoPageParser
{
    private static readonly HtmlParser Parser = new();

    /// <summary>Заголовок страницы-заглушки «Доступ ограничен: проблема с IP».</summary>
    public static bool IsCheckTitle(string? title) =>
        title is not null && title.Contains("Доступ ограничен", StringComparison.OrdinalIgnoreCase);

    /// <summary>Похоже ли, что вместо нужной страницы Авито показал проверку «я не робот».</summary>
    public static bool IsCheckPage(string? title, string html)
    {
        if (IsCheckTitle(title))
            return true;

        return !html.Contains("data-marker=\"item\"", StringComparison.Ordinal)
               && html.Contains("captcha", StringComparison.OrdinalIgnoreCase)
               && html.Contains("firewall", StringComparison.OrdinalIgnoreCase);
    }

    public static SearchPage ParseSearchPage(string html)
    {
        var document = Parser.ParseDocument(html);

        // Берём объявления из основного списка выдачи. Ниже него Авито показывает блоки
        // «рекомендации» и «в других городах» — они нам не нужны.
        IParentNode catalog = (IParentNode?)document.QuerySelector("[data-marker='catalog-serp']") ?? document;

        var items = new List<Listing>();
        var seenIds = new HashSet<string>();
        foreach (var card in catalog.QuerySelectorAll("[data-marker='item']"))
        {
            var listing = ParseCard(card);
            if (listing is not null && seenIds.Add(listing.Id))
                items.Add(listing);
        }

        var totalCount = (int?)TextUtils.ParseMoney(document.QuerySelector("[data-marker='page-title/count']")?.TextContent);

        bool? hasNextPage = null;
        var nextButton = document.QuerySelector("[data-marker='pagination-button/nextPage']");
        if (nextButton is not null)
        {
            var disabled = nextButton.GetAttribute("aria-disabled") == "true"
                           || nextButton.HasAttribute("disabled")
                           || (nextButton.ClassName?.Contains("disabled", StringComparison.OrdinalIgnoreCase) ?? false);
            hasNextPage = !disabled;
        }
        else if (document.QuerySelector("[data-marker^='pagination-button']") is not null)
        {
            // Кнопки страниц есть, а «следующей» нет — это последняя страница
            hasNextPage = false;
        }

        return new SearchPage(items, totalCount, hasNextPage);
    }

    public static ListingDetails ParseListingPage(string html)
    {
        var document = Parser.ParseDocument(html);

        var description = FirstText(document,
            "[data-marker='item-view/item-description']",
            "[itemprop='description']");

        var sellerName = FirstText(document, "[data-marker='seller-info/name']");
        var sellerLabel = FirstText(document, "[data-marker='seller-info/label']");

        var sellerLink = document.QuerySelector("[data-marker='seller-link/link']")
                         ?? document.QuerySelector("[data-marker='seller-info/name'] a")
                         ?? document.QuerySelector("[data-marker^='seller-info'] a[href*='/brands/']")
                         ?? document.QuerySelector("[data-marker^='seller-info'] a[href*='/user/']");
        var sellerHref = sellerLink?.GetAttribute("href");

        return new ListingDetails(
            NullIfEmpty(description),
            NullIfEmpty(sellerName),
            NullIfEmpty(sellerLabel),
            sellerHref is null ? null : AvitoUrl.ToAbsolute(sellerHref, dropQuery: true));
    }

    private static Listing? ParseCard(IElement card)
    {
        var titleLink = card.QuerySelector("a[data-marker='item-title']") ?? card.QuerySelector("a[itemprop='url']");
        var href = titleLink?.GetAttribute("href");
        var id = card.GetAttribute("data-item-id") ?? AvitoUrl.ExtractItemId(href);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(href))
            return null;

        var title = FirstText(card, "[itemprop='name']", "[data-marker='item-title']");
        if (string.IsNullOrEmpty(title))
            title = TextUtils.Clean(titleLink!.GetAttribute("title"));

        var price = TextUtils.ParseMoney(card.QuerySelector("meta[itemprop='price']")?.GetAttribute("content"))
                    ?? TextUtils.ParseMoney(FirstText(card, "[data-marker='item-price']"));

        // В части категорий (например, телефоны) описания в выдаче нет — вместо него параметры: «Новый», «Отличное»
        var description = TextUtils.Clean(card.QuerySelector("meta[itemprop='description']")?.GetAttribute("content"));
        if (description.Length == 0)
            description = FirstText(card, "[class*='descriptionStep']", "[class*='item-description']", "[data-marker='item-specific-params']");

        // Продавец: ссылка на его профиль (/brands/...; в старой вёрстке — /user/...)
        var sellerLink = card.QuerySelector("a[href*='/brands/']") ?? card.QuerySelector("a[href*='/user/']");
        var sellerHref = sellerLink?.GetAttribute("href");
        var sellerName = TextUtils.Clean(sellerLink?.TextContent);
        if (sellerName.Length == 0)
            sellerName = FirstText(card, "[data-marker='seller-info/name']", "[class*='userInfo'] p", "[class*='sellerInfo'] p");

        // «1 962 отзыва»
        var sellerSummary = FirstText(card, "[data-marker='seller-info/summary']");
        var sellerReviews = sellerSummary.Contains("отзыв", StringComparison.OrdinalIgnoreCase)
            ? (int?)TextUtils.ParseMoney(sellerSummary)
            : null;

        var badges = LeafTexts(card, "[data-marker^='badge-title']");
        if (badges.Count == 0)
            badges = LeafTexts(card, "[class*='badge'], [data-marker*='badge']");

        return new Listing
        {
            Id = id,
            Title = title,
            Url = AvitoUrl.ToAbsolute(href, dropQuery: true),
            Price = price is > 0 ? price : null,
            Description = NullIfEmpty(description),
            // «Достоевская, до 5 мин.» или город, если продавец из другого города с доставкой
            Address = NullIfEmpty(FirstText(card,
                "[data-marker='item-location']", "[data-marker='item-address']", "[class*='geo-root']", "[class*='geo-address']")),
            PublishedText = NullIfEmpty(FirstText(card, "[data-marker='item-date']")),
            SellerName = NullIfEmpty(sellerName),
            SellerUrl = sellerHref is null ? null : AvitoUrl.ToAbsolute(sellerHref, dropQuery: true),
            SellerReviews = sellerReviews,
            Badges = badges.Count > 0 ? string.Join(" | ", badges) : null,
        };
    }

    /// <summary>
    /// Тексты самых «глубоких» подходящих элементов. Значки бывают вложены друг в друга,
    /// и без этого их текст склеился бы: «Надёжный продавецДокументы проверены».
    /// </summary>
    private static List<string> LeafTexts(IElement root, string selector) =>
        root.QuerySelectorAll(selector)
            .Where(element => element.QuerySelector(selector) is null)
            .Select(element => TextUtils.Clean(element.TextContent))
            .Where(text => text.Length > 0)
            .Distinct()
            .ToList();

    /// <summary>Текст первого найденного элемента из списка селекторов (пробуем по очереди).</summary>
    private static string FirstText(IParentNode root, params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var text = TextUtils.Clean(root.QuerySelector(selector)?.TextContent);
            if (text.Length > 0)
                return text;
        }

        return "";
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
