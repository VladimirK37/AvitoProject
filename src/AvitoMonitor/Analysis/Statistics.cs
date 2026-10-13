namespace AvitoMonitor.Analysis;

public static class Statistics
{
    /// <summary>
    /// Медиана — «средняя» цена, устойчивая к выбросам: половина объявлений дешевле, половина дороже.
    /// В отличие от среднего арифметического, её не испортят пара объявлений «за 1 ₽» или «за 10 млн».
    /// </summary>
    public static decimal? Median(IEnumerable<decimal> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        if (sorted.Length == 0)
            return null;

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2m;
    }

    /// <summary>Цены дешевле этой доли от предварительной медианы считаются выбросами.</summary>
    public const decimal LowOutlierFactor = 0.4m;

    /// <summary>Цены дороже этой доли от предварительной медианы считаются выбросами.</summary>
    public const decimal HighOutlierFactor = 2.5m;

    /// <summary>
    /// Отбрасывает явные выбросы: в 2,5 раза дешевле или дороже предварительной медианы.
    /// Обычно это нераспознанные копии, аксессуары, битые вещи или ошибки в цене.
    /// Если после отбрасывания остаётся меньше 3 значений — возвращает всё как есть.
    /// </summary>
    public static List<decimal> WithoutOutliers(IReadOnlyCollection<decimal> values)
    {
        if (Median(values) is not { } rough)
            return [];

        var kept = values.Where(v => v >= rough * LowOutlierFactor && v <= rough * HighOutlierFactor).ToList();
        return kept.Count >= 3 ? kept : values.ToList();
    }
}
