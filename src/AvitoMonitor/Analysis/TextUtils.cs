using System.Globalization;
using System.Text.RegularExpressions;

namespace AvitoMonitor.Analysis;

/// <summary>Мелкие помощники для работы с текстом и форматирования чисел.</summary>
public static class TextUtils
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Убирает лишние пробелы и переносы строк.</summary>
    public static string Clean(string? text) => text is null ? "" : Whitespace.Replace(text, " ").Trim();

    /// <summary>Текст для поиска ключевых слов: нижний регистр, «ё» → «е», без лишних пробелов.</summary>
    public static string Normalize(string? text) => Clean(text).ToLowerInvariant().Replace('ё', 'е');

    /// <summary>Вырезает фразы из текста (например, «не битый», чтобы не сработало слово «битый»).</summary>
    public static string RemovePhrases(string normalizedText, IEnumerable<string> phrases)
    {
        foreach (var phrase in phrases)
            normalizedText = normalizedText.Replace(phrase, " ", StringComparison.Ordinal);

        return normalizedText;
    }

    /// <summary>Какие слова из списка встречаются в тексте.</summary>
    public static List<string> FindAll(string normalizedText, IEnumerable<string> words) =>
        words.Where(word => normalizedText.Contains(word, StringComparison.Ordinal)).ToList();

    /// <summary>«45 000 ₽» → 45000. Если цифр нет («Цена не указана») — null.</summary>
    public static decimal? ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return digits.Length is > 0 and <= 15 ? decimal.Parse(digits, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>45000 → «45 000 ₽».</summary>
    public static string Money(decimal value) => value.ToString("#,0", Russian) + " ₽";

    /// <summary>Цена или цена за м² — в зависимости от того, что сравниваем.</summary>
    public static string Metric(decimal value, bool perSquareMeter) =>
        perSquareMeter ? Money(value) + "/м²" : Money(value);

    /// <summary>−24.6 → «−25%», 12 → «+12%».</summary>
    public static string Percent(double value)
    {
        var rounded = Math.Round(value);
        return rounded switch
        {
            > 0 => $"+{rounded}%",
            < 0 => $"−{Math.Abs(rounded)}%",
            _ => "0%",
        };
    }
}
