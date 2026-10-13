using System.Text.RegularExpressions;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Avito;

/// <summary>Всё, что касается ссылок Авито: проверка, номер страницы, сортировка, категория.</summary>
public static class AvitoUrl
{
    public const string BaseUrl = "https://www.avito.ru";

    /// <summary>
    /// Значение параметра сортировки «по дате» (s=104).
    /// В режиме слежения выдача сортируется по дате, чтобы новые объявления были сверху.
    /// </summary>
    public const string SortByDateValue = "104";

    // У ссылки на объявление в конце стоит его номер: .../iphone_15_128_gb_4378912345
    private static readonly Regex ItemIdRegex = new(@"_(\d{6,})$", RegexOptions.Compiled);

    /// <summary>Проверяет, что пользователь вставил ссылку на поиск Авито, и приводит её к единому виду.</summary>
    public static bool TryParseSearchUrl(string? input, out Uri url, out string error)
    {
        url = null!;
        error = "";
        input = input?.Trim() ?? "";

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            error = "Это не похоже на ссылку. Скопируйте адрес из браузера целиком, вместе с https://";
            return false;
        }

        if (uri.Host != "avito.ru" && !uri.Host.EndsWith(".avito.ru", StringComparison.OrdinalIgnoreCase))
        {
            error = "Нужна ссылка на avito.ru";
            return false;
        }

        if (ExtractItemId(uri.AbsolutePath) is not null)
        {
            error = "Это ссылка на одно объявление. Нужна ссылка на страницу поиска с фильтрами (список объявлений).";
            return false;
        }

        if (uri.AbsolutePath.Trim('/').Length == 0 && string.IsNullOrEmpty(uri.Query))
        {
            error = "Это главная страница Авито. Настройте поиск и фильтры, а потом скопируйте ссылку.";
            return false;
        }

        // m.avito.ru → www.avito.ru, убираем номер страницы и «якорь» после #
        var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Host = "www.avito.ru", Port = -1, Fragment = "" };
        url = SetQueryParameter(builder.Uri, "p", null);
        return true;
    }

    /// <summary>Ссылка на конкретную страницу выдачи (параметр p).</summary>
    public static Uri WithPage(Uri searchUrl, int page) =>
        SetQueryParameter(searchUrl, "p", page <= 1 ? null : page.ToString());

    /// <summary>Та же выдача, но отсортированная по дате — новые объявления сверху.</summary>
    public static Uri SortedByDate(Uri searchUrl) => SetQueryParameter(searchUrl, "s", SortByDateValue);

    /// <summary>Угадывает категорию по адресу: /kvartiry/ — недвижимость, /avtomobili/ — авто.</summary>
    public static SearchCategory DetectCategory(Uri searchUrl)
    {
        var path = searchUrl.AbsolutePath.ToLowerInvariant();

        string[] realEstateParts =
        [
            "/kvartiry", "/komnaty", "/doma_dachi_kottedzhi", "/zemelnye_uchastki",
            "/kommercheskaya_nedvizhimost", "/garazhi_i_mashinomesta", "/nedvizhimost",
        ];

        if (realEstateParts.Any(path.Contains))
            return SearchCategory.RealEstate;

        if (path.Contains("/avtomobili"))
            return SearchCategory.Cars;

        return SearchCategory.General;
    }

    /// <summary>Предлагает название поиска: текст из строки поиска (q=) или последний кусок адреса.</summary>
    public static string SuggestName(Uri searchUrl)
    {
        var query = GetQueryParameter(searchUrl, "q");
        if (!string.IsNullOrWhiteSpace(query))
            return query;

        var lastSegment = searchUrl.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? "";

        // iphone_15-ASgBAgICA0Sy... → iphone 15 (после дефиса у Авито идёт закодированный фильтр)
        var dash = lastSegment.IndexOf("-ASg", StringComparison.Ordinal);
        if (dash > 0)
            lastSegment = lastSegment[..dash];

        var name = lastSegment.Replace('_', ' ').Trim();
        return name.Length > 0 ? name : "Поиск";
    }

    /// <summary>Номер объявления из ссылки на него или null.</summary>
    public static string? ExtractItemId(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;

        var path = href.Split('?', '#')[0].TrimEnd('/');
        var match = ItemIdRegex.Match(path);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Относительную ссылку со страницы ("/spb/...") превращает в полную, без лишних параметров.</summary>
    public static string ToAbsolute(string href, bool dropQuery = false)
    {
        if (dropQuery)
            href = href.Split('?', '#')[0];

        return Uri.TryCreate(new Uri(BaseUrl), href, out var absolute) ? absolute.ToString() : href;
    }

    /// <summary>Текст из строки поиска Авито (параметр q=), например «airpods 4», или null.</summary>
    public static string? GetSearchText(Uri searchUrl) => GetQueryParameter(searchUrl, "q");

    private static string? GetQueryParameter(Uri url, string key)
    {
        foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair[0] == key && pair.Length == 2)
                return Uri.UnescapeDataString(pair[1].Replace('+', ' '));
        }

        return null;
    }

    /// <summary>Ставит, заменяет или (при value = null) убирает параметр в адресе, не трогая остальные.</summary>
    private static Uri SetQueryParameter(Uri url, string key, string? value)
    {
        var parts = url.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Split('=', 2)[0] != key)
            .ToList();

        if (value is not null)
            parts.Add($"{key}={Uri.EscapeDataString(value)}");

        var builder = new UriBuilder(url) { Query = string.Join("&", parts), Port = -1 };
        return builder.Uri;
    }
}
