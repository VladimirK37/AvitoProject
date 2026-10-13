using AvitoMonitor.Avito;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Workflows;

/// <summary>
/// Один запрос в форме редактирования.
/// Обычно это поиск по словам (Request), для старых поисков — готовая ссылка (Url).
/// OriginalLabel — как запрос назывался до правки (null — запрос только что добавили).
/// </summary>
public sealed record SourceEdit(string? OriginalLabel, string Label, SearchRequest? Request, string? Url = null)
{
    public static SourceEdit ForKeywords(string? originalLabel, string label, SearchRequest request) =>
        new(originalLabel, label, request);

    public static SourceEdit ForUrl(string? originalLabel, string label, string url) =>
        new(originalLabel, label, null, url);
}

/// <summary>Что изменилось после сохранения — чтобы записать это в базу.</summary>
/// <param name="Changes">Описание изменений, по строке на каждое (для журнала и окна).</param>
/// <param name="RemovedListingIds">Объявления, которые нужно удалить из базы.</param>
/// <param name="RelabeledListings">Объявления, у которых поменялось название запроса.</param>
public sealed record SearchEditResult(List<string> Changes, List<string> RemovedListingIds, List<Listing> RelabeledListings);

/// <summary>
/// Изменение сохранённого поиска так, чтобы собранные данные остались согласованными:
/// <list type="bullet">
/// <item>поменяли слова, город, цену или ссылку → объявления по старому запросу удаляются, сбор начнётся заново;</item>
/// <item>переименовали запрос → объявления остаются, у них меняется метка;</item>
/// <item>удалили запрос → его объявления удаляются;</item>
/// <item>добавили запрос → он будет собираться с первой страницы;</item>
/// <item>поменяли название или категорию → данные не трогаем, рынок просто пересчитается.</item>
/// </list>
/// </summary>
public static class SearchEditor
{
    /// <summary>Проверка формы. Пустой список — всё в порядке.</summary>
    public static List<string> Validate(string name, IReadOnlyList<SourceEdit> sources)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
            errors.Add("Укажите название поиска.");

        if (sources.Count == 0)
            errors.Add("Нужен хотя бы один запрос.");

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var number = $"Запрос №{i + 1}";

            if (string.IsNullOrWhiteSpace(source.Label))
                errors.Add($"{number}: укажите короткое название.");

            if (source.Request is { } request)
            {
                if (string.IsNullOrWhiteSpace(request.Query))
                    errors.Add($"{number}: напишите, что искать.");
                if (request.PriceMin < 0 || request.PriceMax < 0)
                    errors.Add($"{number}: цена не может быть отрицательной.");
                if (request.PriceMin is { } min && request.PriceMax is { } max && min > max)
                    errors.Add($"{number}: цена «от» больше цены «до».");
            }
            else if (!AvitoUrl.TryParseSearchUrl(source.Url, out _, out var error))
            {
                errors.Add($"{number}: {error}");
            }
        }

        var duplicate = sources.GroupBy(s => s.Label.Trim()).FirstOrDefault(g => g.Key.Length > 0 && g.Count() > 1);
        if (duplicate is not null)
            errors.Add($"Названия запросов должны различаться: «{duplicate.Key}» встречается несколько раз.");

        return errors;
    }

    /// <summary>
    /// Применяет правки к поиску и его объявлениям (listings меняется на месте).
    /// Перед вызовом проверьте форму через <see cref="Validate"/>.
    /// </summary>
    public static SearchEditResult Apply(SavedSearch search, string name, SearchCategory category,
        IReadOnlyList<SourceEdit> edits, IDictionary<string, Listing> listings)
    {
        var result = new SearchEditResult([], [], []);
        var changes = result.Changes;

        name = name.Trim();
        if (search.Name != name)
        {
            changes.Add($"Название: «{search.Name}» → «{name}»");
            search.Name = name;
        }

        if (search.Category != category)
        {
            changes.Add($"Категория: {search.Category.ToRussian()} → {category.ToRussian()}");
            search.Category = category;
        }

        // Сопоставляем правки со старыми запросами по их прежнему названию
        var matched = edits
            .Where(edit => edit.OriginalLabel is not null)
            .Select(edit => (Edit: edit, Source: search.Sources.FirstOrDefault(s => s.Label == edit.OriginalLabel)))
            .Where(pair => pair.Source is not null)
            .ToList();

        // 1. Удалённые запросы — вместе с их объявлениями
        foreach (var removed in search.Sources.Where(source => matched.All(pair => pair.Source != source)))
            changes.Add($"Удалён запрос «{removed.Label}» и {RemoveListings(listings, removed.Label, result)} его объявлений");

        // 2. Изменился сам запрос — старые объявления удаляем, сбор с начала
        foreach (var (edit, source) in matched)
        {
            var newUrl = UrlOf(edit);
            CopyRequest(edit, source!);
            if (source!.Url == newUrl)
                continue;

            var removedCount = RemoveListings(listings, source.Label, result);
            source.Url = newUrl;
            source.NextPageToScan = 1;
            source.ScanCompleted = false;
            source.TotalCountOnAvito = null;
            changes.Add($"Запрос «{edit.Label.Trim()}» изменён ({Describe(edit)}): удалено {removedCount} объявлений, сбор начнётся заново");
        }

        // 3. Переименования — одним проходом, чтобы обмен названиями (А↔Б) тоже работал
        var renames = matched
            .Where(pair => pair.Source!.Label != pair.Edit.Label.Trim())
            .ToDictionary(pair => pair.Source!.Label, pair => pair.Edit.Label.Trim());
        foreach (var listing in listings.Values)
        {
            if (renames.TryGetValue(listing.SourceLabel, out var newLabel))
            {
                listing.SourceLabel = newLabel;
                result.RelabeledListings.Add(listing);
            }
        }

        foreach (var (edit, source) in matched)
        {
            var newLabel = edit.Label.Trim();
            if (source!.Label != newLabel)
            {
                changes.Add($"Запрос «{source.Label}» переименован в «{newLabel}»");
                source.Label = newLabel;
            }
        }

        // 4. Итоговый список запросов в порядке формы, новые — с первой страницы
        var sources = new List<SearchSource>();
        foreach (var edit in edits)
        {
            var existing = matched.FirstOrDefault(pair => pair.Edit == edit).Source;
            if (existing is not null)
            {
                sources.Add(existing);
                continue;
            }

            var source = new SearchSource { Label = edit.Label.Trim(), Url = UrlOf(edit) };
            CopyRequest(edit, source);
            sources.Add(source);
            changes.Add($"Добавлен запрос «{source.Label}» ({Describe(edit)})");
        }

        search.Sources = sources;
        return result;
    }

    private static string UrlOf(SourceEdit edit) =>
        edit.Request is { } request
            ? request.ToUrl().ToString()
            : AvitoUrl.TryParseSearchUrl(edit.Url, out var parsed, out _) ? parsed.ToString() : edit.Url?.Trim() ?? "";

    private static void CopyRequest(SourceEdit edit, SearchSource source)
    {
        source.Query = edit.Request?.Query.Trim();
        source.City = edit.Request?.City;
        source.PriceMin = edit.Request?.PriceMin;
        source.PriceMax = edit.Request?.PriceMax;
    }

    private static string Describe(SourceEdit edit) => edit.Request?.Describe() ?? "ссылка";

    private static int RemoveListings(IDictionary<string, Listing> listings, string sourceLabel, SearchEditResult result)
    {
        var ids = listings.Values.Where(listing => listing.SourceLabel == sourceLabel).Select(listing => listing.Id).ToList();
        foreach (var id in ids)
            listings.Remove(id);

        result.RemovedListingIds.AddRange(ids);
        return ids.Count;
    }
}
