using System.Text.RegularExpressions;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Analysis;

/// <summary>
/// Распознаёт копии брендовой техники. Прямо «копия» на Авито пишут редко — обычно
/// это «Оригинал» в кавычках, «Luxe», «Premium» или название чипа, на котором делают
/// клоны AirPods (Airoha, Huilian).
///
/// Для недвижимости и авто не применяется: там «люкс» и «premium» — обычные слова.
/// </summary>
public static class ReplicaDetector
{
    private static readonly string[] Negations = ["не копия", "не реплика", "не аналог"];

    private static readonly Regex Markers = new(
        // «Оригинал», "Original" — в кавычках, то есть «как бы оригинал»
        @"[""«“„']\s*(?:оригинал|original)[a-zа-я]*\s*[""»”“']" +
        // прямые слова и названия чипов клонов
        @"|\b(?:копия|копии|реплика|replica|аналог|люкс|luxe|lux|premium|airoha|huilian|fco)\b" +
        @"|1:1",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Признак копии из текста (например, «"оригинал"» или «airoha») или null.</summary>
    public static string? FindMarker(Listing listing, SearchCategory category)
    {
        if (category != SearchCategory.General)
            return null;

        var text = TextUtils.RemovePhrases(TextUtils.Normalize($"{listing.Title} {listing.Description}"), Negations);
        var match = Markers.Match(text);
        return match.Success ? match.Value.Trim() : null;
    }
}
