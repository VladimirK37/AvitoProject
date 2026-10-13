using System.Text.RegularExpressions;

namespace AvitoMonitor.Analysis;

/// <summary>
/// Проверяет, та ли модель в объявлении, что в поисковом запросе.
/// Авито по запросу «airpods 4» показывает и AirPods Pro 3, а по «iphone 15» — iPhone 15 Pro Max.
/// Их цены нельзя смешивать с ценами нужной модели.
///
/// Правила простые:
/// <list type="bullet">
/// <item>все числа из запроса должны быть в заголовке («airpods 4» → нужна «4»);</item>
/// <item>в заголовке не должно быть слов другой версии (pro, max, mini…), если их нет в запросе.</item>
/// </list>
/// </summary>
public static class ModelMatcher
{
    // Слова и числа по отдельности: «AirPods4» → «airpods», «4»; «s24» → «s», «24»
    private static readonly Regex Token = new(@"[a-zа-я]+|\d+", RegexOptions.Compiled);

    private static readonly string[] VariantWords =
        ["pro", "про", "max", "макс", "mini", "мини", "plus", "плюс", "ultra", "ультра", "lite", "лайт"];

    /// <summary>Почему объявление — другая модель, или null, если модель та же (или запроса нет).</summary>
    public static string? FindMismatch(string title, string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return null;

        var queryTokens = Tokens(searchText);
        var titleTokens = Tokens(title);

        var missingNumber = queryTokens.FirstOrDefault(token => char.IsAsciiDigit(token[0]) && !titleTokens.Contains(token));
        if (missingNumber is not null)
            return $"в заголовке нет «{missingNumber}» из запроса";

        var extraWord = VariantWords.FirstOrDefault(word => titleTokens.Contains(word) && !queryTokens.Contains(word));
        return extraWord is null ? null : $"в заголовке «{extraWord}», а в запросе этого нет";
    }

    private static HashSet<string> Tokens(string text) =>
        Token.Matches(TextUtils.Normalize(text)).Select(match => match.Value).ToHashSet();
}
