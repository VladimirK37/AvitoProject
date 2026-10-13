using AvitoMonitor.Avito;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Workflows;

/// <summary>Одна ссылка в форме редактирования. OriginalLabel — как она называлась до правки (null — новая ссылка).</summary>
public sealed record SourceEdit(string? OriginalLabel, string Label, string Url);

/// <summary>
/// Изменение сохранённого поиска так, чтобы собранные данные остались согласованными:
/// <list type="bullet">
/// <item>поменяли ссылку → объявления по старой ссылке удаляются, сбор начнётся с первой страницы;</item>
/// <item>переименовали ссылку → объявления остаются, у них меняется метка;</item>
/// <item>удалили ссылку → её объявления удаляются;</item>
/// <item>добавили ссылку → она будет собираться с первой страницы;</item>
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
            errors.Add("Нужна хотя бы одна ссылка.");

        for (var i = 0; i < sources.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(sources[i].Label))
                errors.Add($"Ссылка №{i + 1}: укажите короткое название.");

            if (!AvitoUrl.TryParseSearchUrl(sources[i].Url, out _, out var error))
                errors.Add($"Ссылка №{i + 1}: {error}");
        }

        var duplicate = sources.GroupBy(s => s.Label.Trim()).FirstOrDefault(g => g.Key.Length > 0 && g.Count() > 1);
        if (duplicate is not null)
            errors.Add($"Названия ссылок должны различаться: «{duplicate.Key}» встречается несколько раз.");

        return errors;
    }

    /// <summary>
    /// Применяет правки к поиску и его объявлениям (listings меняется на месте).
    /// Перед вызовом проверьте форму через <see cref="Validate"/>.
    /// </summary>
    /// <returns>Что изменилось — по строке на изменение (для журнала и интерфейса).</returns>
    public static List<string> Apply(SavedSearch search, string name, SearchCategory category,
        IReadOnlyList<SourceEdit> edits, IDictionary<string, Listing> listings)
    {
        var changes = new List<string>();

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

        // Сопоставляем правки со старыми ссылками по их прежнему названию
        var matched = edits
            .Where(edit => edit.OriginalLabel is not null)
            .Select(edit => (Edit: edit, Source: search.Sources.FirstOrDefault(s => s.Label == edit.OriginalLabel)))
            .Where(pair => pair.Source is not null)
            .ToList();

        // 1. Удалённые ссылки — вместе с их объявлениями
        foreach (var removed in search.Sources.Where(source => matched.All(pair => pair.Source != source)))
            changes.Add($"Удалена ссылка «{removed.Label}» и {RemoveListings(listings, removed.Label)} её объявлений");

        // 2. Изменённые адреса — старые объявления удаляем, сбор с начала
        foreach (var (edit, source) in matched)
        {
            var newUrl = Normalize(edit.Url);
            if (source!.Url == newUrl)
                continue;

            var removedCount = RemoveListings(listings, source.Label);
            source.Url = newUrl;
            source.NextPageToScan = 1;
            source.ScanCompleted = false;
            source.TotalCountOnAvito = null;
            changes.Add($"Ссылка «{edit.Label.Trim()}» изменена: удалено {removedCount} объявлений по старой ссылке, сбор начнётся заново");
        }

        // 3. Переименования — одним проходом, чтобы обмен названиями (А↔Б) тоже работал
        var renames = matched
            .Where(pair => pair.Source!.Label != pair.Edit.Label.Trim())
            .ToDictionary(pair => pair.Source!.Label, pair => pair.Edit.Label.Trim());
        foreach (var listing in listings.Values)
        {
            if (renames.TryGetValue(listing.SourceLabel, out var newLabel))
                listing.SourceLabel = newLabel;
        }

        foreach (var (edit, source) in matched)
        {
            var newLabel = edit.Label.Trim();
            if (source!.Label != newLabel)
            {
                changes.Add($"Ссылка «{source.Label}» переименована в «{newLabel}»");
                source.Label = newLabel;
            }
        }

        // 4. Итоговый список ссылок в порядке формы, новые — с первой страницы
        var sources = new List<SearchSource>();
        foreach (var edit in edits)
        {
            var existing = matched.FirstOrDefault(pair => pair.Edit == edit).Source;
            if (existing is not null)
            {
                sources.Add(existing);
                continue;
            }

            sources.Add(new SearchSource { Label = edit.Label.Trim(), Url = Normalize(edit.Url) });
            changes.Add($"Добавлена ссылка «{edit.Label.Trim()}»");
        }

        search.Sources = sources;
        return changes;
    }

    private static string Normalize(string url) =>
        AvitoUrl.TryParseSearchUrl(url, out var parsed, out _) ? parsed.ToString() : url.Trim();

    private static int RemoveListings(IDictionary<string, Listing> listings, string sourceLabel)
    {
        var ids = listings.Values.Where(listing => listing.SourceLabel == sourceLabel).Select(listing => listing.Id).ToList();
        foreach (var id in ids)
            listings.Remove(id);

        return ids.Count;
    }
}
