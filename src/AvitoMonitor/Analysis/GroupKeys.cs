using System.Text.RegularExpressions;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Analysis;

/// <summary>
/// В какую группу попадает объявление.
/// <para>Group — узкая группа («256 ГБ», «2-к», «2019 г.»): с её медианой сравниваем цену.</para>
/// <para>Fallback — широкая группа («Все объявления» или название ссылки в мультипоиске):
/// используется, если в узкой группе слишком мало объявлений.</para>
/// </summary>
public sealed record GroupKeys(string Group, string Fallback)
{
    public const string AllListings = "Все объявления";
    public const string NoiseCancellingGroup = "С шумоподавлением (ANC)";
    private const string Separator = " · ";

    private static readonly Regex FirstNumber = new(@"\d+", RegexOptions.Compiled);

    public static GroupKeys For(Listing listing, SavedSearch search)
    {
        var detail = search.Category switch
        {
            SearchCategory.RealEstate => ListingFeatures.GetRoomsLabel(listing.Title),
            SearchCategory.Cars => ListingFeatures.GetCarYear(listing.Title) is { } year ? $"{year} г." : "Год не указан",
            // Телефоны — по памяти, наушники — с шумоподавлением или без
            _ => ListingFeatures.GetStorageLabel(listing.Title)
                 ?? (ListingFeatures.HasNoiseCancelling(listing.Title) ? NoiseCancellingGroup : "Без уточнения"),
        };

        // В мультипоиске (iPhone 15 + iPhone 16) нельзя смешивать модели — добавляем название ссылки
        if (!search.IsMulti)
            return new GroupKeys(detail, AllListings);

        var label = string.IsNullOrWhiteSpace(listing.SourceLabel) ? AllListings : listing.SourceLabel;
        return new GroupKeys(label + Separator + detail, label);
    }

    /// <summary>
    /// Порядок групп в отчёте: по названию ссылки, потом по числу (128 ГБ → 256 ГБ → 1 ТБ, 1-к → 2-к).
    /// </summary>
    public static IOrderedEnumerable<T> OrderByGroup<T>(IEnumerable<T> items, Func<T, string> keySelector) =>
        items.OrderBy(item => SplitPrefix(keySelector(item)).Prefix, StringComparer.CurrentCulture)
            .ThenBy(item => NumericWeight(SplitPrefix(keySelector(item)).Detail))
            .ThenBy(keySelector, StringComparer.CurrentCulture);

    private static (string Prefix, string Detail) SplitPrefix(string key)
    {
        var index = key.LastIndexOf(Separator, StringComparison.Ordinal);
        return index < 0 ? ("", key) : (key[..index], key[(index + Separator.Length)..]);
    }

    private static long NumericWeight(string detail)
    {
        if (detail is "Студия" or "Комната")
            return 0;

        var match = FirstNumber.Match(detail);
        if (!match.Success)
            return long.MaxValue;

        var number = long.Parse(match.Value);
        return detail.Contains("ТБ", StringComparison.Ordinal) ? number * 1024 : number;
    }
}
