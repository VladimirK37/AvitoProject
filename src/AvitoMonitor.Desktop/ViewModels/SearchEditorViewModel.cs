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
    public override string ToString() => Title;
}

/// <summary>Одна ссылка в форме редактирования поиска.</summary>
public sealed partial class SourceEditViewModel : ObservableObject
{
    public SourceEditViewModel(string? originalLabel, string label, string url)
    {
        OriginalLabel = originalLabel;
        _label = label;
        _url = url;
    }

    /// <summary>Как ссылка называлась до правки; null — ссылку только что добавили.</summary>
    public string? OriginalLabel { get; }

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _url;

    // Вставили ссылку в пустую строку — сразу предлагаем название
    partial void OnUrlChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(Label) && AvitoUrl.TryParseSearchUrl(value, out var url, out _))
            Label = AvitoUrl.SuggestName(url);
    }
}

/// <summary>
/// Вкладка «Изменить поиск»: название, категория и ссылки.
/// Правила сохранения (что происходит с собранными объявлениями) — в SearchEditor (Core).
/// </summary>
public sealed partial class SearchEditorViewModel : ObservableObject
{
    private readonly SavedSearch _search;
    private readonly SearchRepository _repository;
    private readonly SearchCatalog _catalog;
    private readonly ILogger _logger;

    public SearchEditorViewModel(SavedSearch search, SearchRepository repository, SearchCatalog catalog, ILogger logger)
    {
        _search = search;
        _repository = repository;
        _catalog = catalog;
        _logger = logger;
        Categories = Enum.GetValues<SearchCategory>().Select(c => new CategoryOption(c, c.ToRussian())).ToList();
        LoadFromSearch();
    }

    /// <summary>Поиск сохранён с изменениями — нужно обновить данные на других вкладках.</summary>
    public event Action? Saved;

    public IReadOnlyList<CategoryOption> Categories { get; }

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
    private void AddSource() => Sources.Add(new SourceEditViewModel(null, "", ""));

    [RelayCommand]
    private void RemoveSource(SourceEditViewModel source) => Sources.Remove(source);

    [RelayCommand]
    private void OpenUrl(SourceEditViewModel source) => SystemShell.Open(source.Url);

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
        var edits = Sources.Select(s => new SourceEdit(s.OriginalLabel, s.Label, s.Url)).ToList();
        var errors = SearchEditor.Validate(Name, edits);
        if (errors.Count > 0)
        {
            Errors = string.Join(Environment.NewLine, errors);
            Result = null;
            return;
        }

        var listings = _repository.LoadListings(_search.Id);
        var changes = SearchEditor.Apply(_search, Name, SelectedCategory!.Value, edits, listings);
        _repository.SaveListings(_search.Id, listings);
        _catalog.Save(_search);

        foreach (var change in changes)
            _logger.LogInformation("«{Search}»: {Change}", _search.Name, change);

        LoadFromSearch();
        Errors = null;
        Result = changes.Count == 0 ? "Изменений нет." : "Сохранено:" + Environment.NewLine + string.Join(Environment.NewLine, changes.Select(c => "• " + c));
        Saved?.Invoke();
    }

    private bool CanSave() => !IsLocked;

    private void LoadFromSearch()
    {
        Name = _search.Name;
        SelectedCategory = Categories.First(c => c.Value == _search.Category);
        Sources.Clear();
        foreach (var source in _search.Sources)
            Sources.Add(new SourceEditViewModel(source.Label, source.Label, source.Url));
    }
}
