using System.Collections.ObjectModel;
using AvitoMonitor.Avito;
using AvitoMonitor.Domain;
using AvitoMonitor.Storage;
using AvitoMonitor.Workflows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Desktop.ViewModels;

/// <summary>Пункт выпадающего списка категорий.</summary>
public sealed record CategoryOption(SearchCategory Value, string Title)
{
    public static IReadOnlyList<CategoryOption> All { get; } =
        Enum.GetValues<SearchCategory>().Select(c => new CategoryOption(c, c.ToRussian())).ToList();

    public static CategoryOption Of(SearchCategory category) => All.First(c => c.Value == category);

    public override string ToString() => Title;
}

/// <summary>
/// Один запрос в форме редактирования: слова, город, цена.
/// Старые поиски, созданные по готовой ссылке, редактируются как ссылка (IsKeyword = false).
/// </summary>
public sealed partial class SourceEditViewModel : ObservableObject
{
    /// <param name="source">Существующий запрос или null — новый запрос по словам.</param>
    public SourceEditViewModel(SearchSource? source, string? defaultCity = null)
    {
        OriginalLabel = source?.Label;
        IsKeyword = source is null || source.IsKeywordSearch;
        _label = source?.Label ?? "";
        _query = source?.Query ?? "";
        _selectedCity = AvitoCities.Find(source?.City ?? defaultCity);
        _priceMin = source?.PriceMin;
        _priceMax = source?.PriceMax;
        _url = source?.Url ?? "";
    }

    /// <summary>Как запрос назывался до правки; null — запрос только что добавили.</summary>
    public string? OriginalLabel { get; }

    /// <summary>true — поиск по словам; false — старый поиск по готовой ссылке.</summary>
    public bool IsKeyword { get; }

    public bool IsUrl => !IsKeyword;

    public IReadOnlyList<AvitoCity> Cities => AvitoCities.All;

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _query;

    [ObservableProperty]
    private AvitoCity _selectedCity;

    [ObservableProperty]
    private decimal? _priceMin;

    [ObservableProperty]
    private decimal? _priceMax;

    [ObservableProperty]
    private string _url;

    // Название запроса по умолчанию — сами слова: меняем его вслед за словами, пока его не переименовали вручную
    partial void OnQueryChanged(string? oldValue, string newValue)
    {
        if (string.IsNullOrWhiteSpace(Label) || Label == oldValue?.Trim())
            Label = newValue.Trim();
    }

    public SearchRequest ToRequest() => new(Query, SelectedCity.Slug, ToInt(PriceMin), ToInt(PriceMax));

    public SourceEdit ToEdit() => IsKeyword
        ? SourceEdit.ForKeywords(OriginalLabel, Label, ToRequest())
        : SourceEdit.ForUrl(OriginalLabel, Label, Url);

    /// <summary>Какую страницу Авито откроет кнопка «Открыть на Авито».</summary>
    public string? PreviewUrl => IsKeyword
        ? string.IsNullOrWhiteSpace(Query) ? null : ToRequest().ToUrl().ToString()
        : Url;

    public static int? ToInt(decimal? value) => value is { } v and > 0 ? (int)v : null;
}

/// <summary>
/// Вкладка «Изменить поиск»: название, категория и запросы.
/// Правила сохранения (что происходит с собранными объявлениями) — в SearchEditor (Core).
/// </summary>
public sealed partial class SearchEditorViewModel : ObservableObject
{
    private readonly SavedSearch _search;
    private readonly ISearchStore _store;
    private readonly SearchCatalog _catalog;
    private readonly ILogger _logger;

    public SearchEditorViewModel(SavedSearch search, ISearchStore store, SearchCatalog catalog, ILogger logger)
    {
        _search = search;
        _store = store;
        _catalog = catalog;
        _logger = logger;
        LoadFromSearch();
    }

    /// <summary>Поиск сохранён с изменениями — нужно обновить данные на других вкладках.</summary>
    public event Action? Saved;

    public IReadOnlyList<CategoryOption> Categories => CategoryOption.All;

    public ObservableCollection<SourceEditViewModel> Sources { get; } = new();

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private CategoryOption? _selectedCategory;

    [ObservableProperty]
    private string? _errors;

    [ObservableProperty]
    private string? _result;

    /// <summary>Идёт сбор или слежение — сохранять нельзя, пока не нажмут «Стоп».</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLocked;

    [RelayCommand]
    private void AddSource() => Sources.Add(new SourceEditViewModel(null, Sources.FirstOrDefault()?.SelectedCity.Slug));

    [RelayCommand]
    private void RemoveSource(SourceEditViewModel source) => Sources.Remove(source);

    [RelayCommand]
    private void OpenOnAvito(SourceEditViewModel source)
    {
        if (source.PreviewUrl is { } url)
            SystemShell.Open(url);
    }

    [RelayCommand]
    private void Reset()
    {
        LoadFromSearch();
        Errors = null;
        Result = null;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var edits = Sources.Select(s => s.ToEdit()).ToList();
        var errors = SearchEditor.Validate(Name, edits);
        if (errors.Count > 0)
        {
            Errors = string.Join(Environment.NewLine, errors);
            Result = null;
            return;
        }

        var listings = _store.LoadListings(_search.Id);
        var result = SearchEditor.Apply(_search, Name, SelectedCategory!.Value, edits, listings);
        _store.DeleteListings(_search.Id, result.RemovedListingIds);
        _store.UpsertListings(_search.Id, result.RelabeledListings);
        _catalog.Save(_search);

        foreach (var change in result.Changes)
            _logger.LogInformation("«{Search}»: {Change}", _search.Name, change);

        LoadFromSearch();
        Errors = null;
        Result = result.Changes.Count == 0
            ? "Изменений нет."
            : "Сохранено:" + Environment.NewLine + string.Join(Environment.NewLine, result.Changes.Select(c => "• " + c));
        Saved?.Invoke();
    }

    private bool CanSave() => !IsLocked;

    private void LoadFromSearch()
    {
        Name = _search.Name;
        SelectedCategory = CategoryOption.Of(_search.Category);
        Sources.Clear();
        foreach (var source in _search.Sources)
            Sources.Add(new SourceEditViewModel(source));
    }
}
