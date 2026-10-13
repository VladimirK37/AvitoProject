namespace AvitoMonitor.Avito;

/// <summary>Город на Авито: адрес в ссылке (slug) и название.</summary>
public sealed record AvitoCity(string Slug, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Города для поиска. Slug — это часть адреса: avito.ru/<b>sankt-peterburg</b>?q=...
/// Нужного города нет — добавьте его сюда (slug видно в адресной строке на Авито).
/// </summary>
public static class AvitoCities
{
    public const string DefaultSlug = "sankt-peterburg";

    public static readonly IReadOnlyList<AvitoCity> All =
    [
        new("rossiya", "Вся Россия"),
        new("sankt-peterburg", "Санкт-Петербург"),
        new("leningradskaya_oblast", "Ленинградская область"),
        new("moskva", "Москва"),
        new("moskovskaya_oblast", "Московская область"),
        new("novosibirsk", "Новосибирск"),
        new("ekaterinburg", "Екатеринбург"),
        new("kazan", "Казань"),
        new("nizhniy_novgorod", "Нижний Новгород"),
        new("krasnoyarsk", "Красноярск"),
        new("chelyabinsk", "Челябинск"),
        new("samara", "Самара"),
        new("ufa", "Уфа"),
        new("rostov-na-donu", "Ростов-на-Дону"),
        new("krasnodar", "Краснодар"),
        new("omsk", "Омск"),
        new("voronezh", "Воронеж"),
        new("perm", "Пермь"),
        new("volgograd", "Волгоград"),
        new("tyumen", "Тюмень"),
    ];

    public static AvitoCity Find(string? slug) =>
        All.FirstOrDefault(city => city.Slug == slug) ?? All.First(city => city.Slug == DefaultSlug);

    public static string NameOf(string? slug) =>
        All.FirstOrDefault(city => city.Slug == slug)?.Name ?? slug ?? "";
}

/// <summary>
/// Что ищем на Авито: слова, город и (по желанию) цена от/до.
/// Ссылку на Авито программа составляет сама — копировать её из браузера не нужно.
/// </summary>
public sealed record SearchRequest(string Query, string City, int? PriceMin = null, int? PriceMax = null)
{
    /// <summary>Ссылка вида https://www.avito.ru/sankt-peterburg?q=airpods+4&amp;pmin=7000</summary>
    public Uri ToUrl() => AvitoUrl.BuildSearchUrl(this);

    /// <summary>«airpods 4» · Санкт-Петербург · от 7 000 ₽</summary>
    public string Describe()
    {
        var text = $"«{Query.Trim()}» · {AvitoCities.NameOf(City)}";
        if (PriceMin is not null)
            text += $" · от {PriceMin:N0} ₽";
        if (PriceMax is not null)
            text += $" · до {PriceMax:N0} ₽";
        return text;
    }
}
