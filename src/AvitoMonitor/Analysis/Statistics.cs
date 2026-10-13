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
}
