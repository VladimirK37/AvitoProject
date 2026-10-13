namespace AvitoMonitor.Domain;

/// <summary>
/// Тип поиска. От него зависит группировка и что именно сравнивается с рынком:
/// <list type="bullet">
/// <item>Товары — группы по объёму памяти (128 ГБ, 256 ГБ…), сравниваем цену;</item>
/// <item>Недвижимость — группы по числу комнат, сравниваем цену за квадратный метр;</item>
/// <item>Автомобили — группы по году выпуска, сравниваем цену.</item>
/// </list>
/// </summary>
public enum SearchCategory
{
    General,
    RealEstate,
    Cars,
}

public static class SearchCategoryExtensions
{
    public static string ToRussian(this SearchCategory category) => category switch
    {
        SearchCategory.General => "Товары (телефоны, техника и прочее)",
        SearchCategory.RealEstate => "Недвижимость",
        SearchCategory.Cars => "Автомобили",
        _ => category.ToString(),
    };
}
