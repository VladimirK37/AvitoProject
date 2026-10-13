using System.Text.RegularExpressions;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Analysis;

/// <summary>
/// Определяет, кто продаёт: частник/собственник, магазин/агентство — или бизнес,
/// который записался как частное лицо.
///
/// Открытый бизнес: на странице объявления Авито пишет «Компания»/«Агентство»
/// или у продавца фирменная страница магазина (avito.ru/brands/smart-butik).
///
/// Скрытый бизнес — «баллы подозрения»:
/// <list type="bullet">
/// <item>у продавца 200+ отзывов — +2, 50+ отзывов — +1;</item>
/// <item>у продавца 3 и больше объявлений в этой выдаче — +2;</item>
/// <item>название продавца похоже на компанию («ООО», «store», «недвижимость») — +2;</item>
/// <item>каждое «магазинное» или «агентское» слово в тексте («в наличии», «рассрочка», «комиссия агентства») — +1.</item>
/// </list>
/// Набралось 2 балла и больше — считаем, что это бизнес под видом частника.
/// </summary>
public static class SellerClassifier
{
    public const int SuspicionThreshold = 2;
    public const int ManyListingsThreshold = 3;
    public const int ManyReviewsThreshold = 200;
    public const int SomeReviewsThreshold = 50;

    // Обычный профиль на Авито — /brands/i12345678 или /brands/<32 символа 0-9a-f>.
    // Такой адрес бывает и у частников, и у компаний, поэтому сам по себе ни о чём не говорит.
    private static readonly Regex OrdinaryProfileId = new(@"^(i\d+|[0-9a-f]{32})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ShopWords =
    [
        "магазин", "в наличии", "trade-in", "трейд-ин", "трейд ин", "рассрочк", "кредит", "оптом", "опт и розниц",
        "сервисный центр", "гарантия от магазина", "гарантия 1 год", "гарантия 12 мес", "ростест", "скидк", "акция",
        "доставка по россии", "доставка по всей", "большой выбор", "любые модели", "пункт выдачи", "наш адрес",
        "автосалон", "дилер", "автокредит", "выкуп авто",
    ];

    private static readonly string[] AgentWords =
    [
        "агентство", "агентства", "риелтор", "риэлтор", "агент по недвижимости", "наш агент", "сопровождение сделки",
        "юридическое сопровождение", "эксклюзив", "подбор квартир", "специалист по недвижимости", "наша компания",
        "без комиссии для покупателя", "комиссия агентства", "услуги агентства", "ипотечный брокер",
    ];

    /// <summary>Фразы настоящих собственников. Вырезаем их перед поиском агентских слов: «агентам не беспокоить» — не агент.</summary>
    private static readonly string[] OwnerPhrases =
    [
        "агентам не беспокоить", "агентов не беспокоить", "агентствам не беспокоить", "агентам не звонить",
        "риелторам не беспокоить", "риэлторам не беспокоить", "риелторам не звонить", "риэлторам не звонить",
        "без агентов", "без риелторов", "без риэлторов", "без посредников", "не агентство", "от собственника",
    ];

    private static readonly string[] CompanyNameWords =
    [
        "ооо", "ип ", "store", "shop", "магазин", "сервис", "маркет", "market", "недвижимост", "агентство",
        "realty", "estate", "групп", "group", "трейд", "trade", "центр", "салон", "компания",
    ];

    /// <summary>Ключ продавца: ссылка на профиль, а если её нет — имя.</summary>
    public static string? SellerKey(Listing listing) =>
        !string.IsNullOrWhiteSpace(listing.SellerUrl) ? listing.SellerUrl
        : !string.IsNullOrWhiteSpace(listing.SellerName) ? listing.SellerName
        : null;

    /// <summary>Сколько объявлений у каждого продавца в собранной выдаче.</summary>
    public static Dictionary<string, int> CountListingsBySeller(IEnumerable<Listing> listings) =>
        listings.Select(SellerKey)
            .OfType<string>()
            .GroupBy(key => key)
            .ToDictionary(group => group.Key, group => group.Count());

    /// <summary>
    /// У продавца фирменная страница магазина со своим адресом (avito.ru/brands/smart-butik),
    /// а не обычный профиль (avito.ru/brands/i12345678). Такие страницы бывают только у бизнеса.
    /// </summary>
    public static bool HasNamedShopPage(string? sellerUrl)
    {
        const string marker = "/brands/";
        var path = sellerUrl?.Split('?', '#')[0].TrimEnd('/') ?? "";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;

        var slug = path[(index + marker.Length)..];
        return slug.Length > 0 && !slug.Contains('/') && !OrdinaryProfileId.IsMatch(slug);
    }

    public static SellerVerdict Classify(Listing listing, SearchCategory category, int sellerListingsInSearch)
    {
        var isRealEstate = category == SearchCategory.RealEstate;
        var label = TextUtils.Normalize(listing.SellerLabel);

        // 1. Бизнес открыто: подпись на странице объявления или фирменная страница магазина
        var declaredReason =
            label.Contains("компания", StringComparison.Ordinal)
            || label.Contains("агентство", StringComparison.Ordinal)
            || label.Contains("магазин", StringComparison.Ordinal)
                ? $"Авито пишет о продавце: «{listing.SellerLabel}»"
                : HasNamedShopPage(listing.SellerUrl)
                    ? "У продавца фирменная страница магазина на Авито"
                    : null;

        if (declaredReason is not null)
            return new SellerVerdict(isRealEstate ? SellerKind.Agency : SellerKind.Shop, [declaredReason]);

        // 2. Похож на частника — проверяем признаки бизнеса
        var reasons = new List<string>();
        var score = 0;

        if (listing.SellerReviews >= ManyReviewsThreshold)
        {
            score += 2;
            reasons.Add($"У продавца {listing.SellerReviews} отзывов — столько бывает у магазинов и перекупщиков");
        }
        else if (listing.SellerReviews >= SomeReviewsThreshold)
        {
            score += 1;
            reasons.Add($"У продавца {listing.SellerReviews} отзывов");
        }

        if (sellerListingsInSearch >= ManyListingsThreshold)
        {
            score += 2;
            reasons.Add($"У продавца {sellerListingsInSearch} объявлений в этой выдаче");
        }

        var sellerName = TextUtils.Normalize(listing.SellerName) + " ";
        var companyWord = CompanyNameWords.FirstOrDefault(word => sellerName.Contains(word, StringComparison.Ordinal));
        if (companyWord is not null)
        {
            score += 2;
            reasons.Add($"Имя продавца похоже на компанию («{listing.SellerName}»)");
        }

        // Значки Авито («Скидка на доставку», «Оплата частями») сюда не берём: их ставит Авито, а не продавец
        var fullText = TextUtils.Normalize($"{listing.Title} {listing.Description}");
        var ownerHits = TextUtils.FindAll(fullText, OwnerPhrases);
        var textWithoutOwnerPhrases = TextUtils.RemovePhrases(fullText, OwnerPhrases);
        var businessHits = TextUtils.FindAll(textWithoutOwnerPhrases, isRealEstate ? AgentWords : ShopWords);
        if (businessHits.Count > 0)
        {
            score += businessHits.Count;
            reasons.Add("В тексте: " + string.Join(", ", businessHits.Select(word => $"«{word}»")));
        }

        if (score >= SuspicionThreshold)
            return new SellerVerdict(isRealEstate ? SellerKind.HiddenAgent : SellerKind.HiddenShop, reasons);

        // 3. Признаков бизнеса мало — считаем частником / собственником
        if (isRealEstate)
        {
            List<string> ownerReasons = ownerHits.Count > 0
                ? ["Пишет: " + string.Join(", ", ownerHits.Select(word => $"«{word}»"))]
                : ["Признаков агентства не найдено"];
            return new SellerVerdict(SellerKind.Owner, ownerReasons.Concat(reasons).ToList());
        }

        return new SellerVerdict(SellerKind.Private,
            reasons.Count > 0 ? reasons : ["Признаков магазина не найдено"]);
    }
}
