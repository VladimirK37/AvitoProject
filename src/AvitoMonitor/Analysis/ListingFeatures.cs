using System.Globalization;
using System.Text.RegularExpressions;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Analysis;

/// <summary>
/// Достаёт характеристики из заголовка объявления:
/// объём памяти («iPhone 15, 256 ГБ»), число комнат и площадь («2-к. квартира, 54 м²»), год авто.
/// </summary>
public static class ListingFeatures
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // «128 ГБ», «256GB», «512 гб»
    private static readonly Regex StorageGb = new(@"(?<!\d)(\d{2,4})\s?(?:гб|gb)(?![a-zа-я])", Options);

    // «1 ТБ», «2TB»
    private static readonly Regex StorageTb = new(@"(?<!\d)([12])\s?(?:тб|tb)(?![a-zа-я])", Options);

    // Реальные объёмы накопителей. Всё остальное («12 ГБ» оперативной памяти) пропускаем.
    private static readonly HashSet<int> KnownStorageSizes = [16, 32, 64, 128, 256, 512, 1024, 2048];

    // «2-к. квартира»
    private static readonly Regex Rooms = new(@"(?<!\d)(\d{1,2})-к\b", Options);

    // «54 м²», «54,5 м2», «54 кв.м»
    private static readonly Regex Area = new(@"(?<!\d)(\d{1,4}(?:[.,]\d{1,2})?)\s?(?:м²|м2|кв\.?\s?м)", Options);

    // Год выпуска авто: «Toyota Camry 2.5 AT, 2019, 85 000 км»
    private static readonly Regex Year = new(@"(?<!\d)(19[5-9]\d|20[0-4]\d)(?!\d)", Options);

    /// <summary>Объём памяти в гигабайтах (если указано несколько чисел — берём наибольшее, это накопитель).</summary>
    public static int? GetStorageGb(string title)
    {
        var sizes = StorageGb.Matches(title).Select(m => int.Parse(m.Groups[1].Value))
            .Concat(StorageTb.Matches(title).Select(m => int.Parse(m.Groups[1].Value) * 1024))
            .Where(KnownStorageSizes.Contains)
            .ToList();

        return sizes.Count > 0 ? sizes.Max() : null;
    }

    /// <summary>«128 ГБ», «1 ТБ» или null.</summary>
    public static string? GetStorageLabel(string title) => GetStorageGb(title) switch
    {
        null => null,
        >= 1024 and var gb => $"{gb / 1024} ТБ",
        var gb => $"{gb} ГБ",
    };

    /// <summary>«Студия», «1-к», «2-к», «Комната», «Своб. планировка» или «Другое».</summary>
    public static string GetRoomsLabel(string title)
    {
        var text = TextUtils.Normalize(title);

        // «Комната 15 м² в 3-к.» — это комната, а не трёшка, поэтому проверяем её первой
        if (text.StartsWith("комната", StringComparison.Ordinal))
            return "Комната";

        if (text.Contains("студия", StringComparison.Ordinal))
            return "Студия";

        if (text.Contains("своб. планировка", StringComparison.Ordinal) || text.Contains("свободная планировка", StringComparison.Ordinal))
            return "Своб. планировка";

        var match = Rooms.Match(text);
        return match.Success ? $"{match.Groups[1].Value}-к" : "Другое";
    }

    /// <summary>Площадь в м² (первая в заголовке — это площадь квартиры/дома).</summary>
    public static decimal? GetAreaM2(string title)
    {
        var match = Area.Match(title);
        if (!match.Success)
            return null;

        var number = match.Groups[1].Value.Replace(',', '.');
        return decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out var area) && area > 0 ? area : null;
    }

    public static int? GetCarYear(string title)
    {
        var match = Year.Match(title);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    /// <summary>
    /// Число, которое сравниваем с рынком: для недвижимости — цена за м²
    /// (иначе студия и трёшка несравнимы), для остального — просто цена.
    /// </summary>
    public static decimal? GetComparableValue(Listing listing, SearchCategory category)
    {
        if (listing.Price is not > 0)
            return null;

        if (category != SearchCategory.RealEstate)
            return listing.Price;

        var area = GetAreaM2(listing.Title);
        return area is > 0 ? Math.Round(listing.Price.Value / area.Value) : null;
    }
}
