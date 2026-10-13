using System.Collections.ObjectModel;
using Avalonia.Threading;
using AvitoMonitor.Avito;
using AvitoMonitor.Domain;
using AvitoMonitor.Export;
using AvitoMonitor.Storage;
using AvitoMonitor.Workflows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Desktop.ViewModels;

/// <summary>
/// Главное окно: список поисков, лента новых объявлений, журнал и настройки.
/// Подписывается на события фоновых задач (MonitorService) и переносит их в поток интерфейса.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int FeedLimit = 500;

    private readonly SearchCatalog _catalog;
    private readonly SearchRepository _repository;
    private readonly MonitorService _monitor;
    private readonly ReportService _reports;
    private readonly ILogger<MainWindowViewModel> _logger;

    public MainWindowViewModel(SearchCatalog catalog, SearchRepository repository, MonitorService monitor,
        ReportService reports, SettingsViewModel settings, LogViewModel log, ILogger<MainWindowViewModel> logger)
    {
        _catalog = catalog;
        _repository = repository;
        _monitor = monitor;
        _reports = reports;
        _logger = logger;
        Settings = settings;
        Log = log;

        foreach (var search in catalog.All)
            Searches.Add(CreateItem(search));
        SelectedSearch = Searches.FirstOrDefault();
        logger.LogInformation("Окно программы открыто, сохранённых поисков: {Count}", Searches.Count);

        // События приходят из фоновых потоков — интерфейс можно менять только из своего потока
        monitor.JobChanged += search => OnUi(() => Find(search)?.RefreshStatus());
        monitor.ScanProgressed += (search, progress) => OnUi(() => Find(search)?.OnScanProgress(progress));
        monitor.NewListingFound += (search, assessment) => OnUi(() => AddToFeed(search, assessment));
        monitor.CheckRequired += message => OnUi(() => Banner = message);
        monitor.CheckPassed += () => OnUi(() => Banner = null);
    }

    public ObservableCollection<SearchItemViewModel> Searches { get; } = new();

    public ObservableCollection<FeedItemViewModel> Feed { get; } = new();

    public SettingsViewModel Settings { get; }

    public LogViewModel Log { get; }

    [ObservableProperty]
    private SearchItemViewModel? _selectedSearch;

    [ObservableProperty]
    private string _newSearchUrl = "";

    [ObservableProperty]
    private string? _newSearchError;

    /// <summary>Жёлтая полоса сверху: «Авито просит пройти проверку».</summary>
    [ObservableProperty]
    private string? _banner;

    [ObservableProperty]
    private string _feedHeader = "Новые объявления";

    partial void OnSelectedSearchChanged(SearchItemViewModel? value) => value?.EnsureLoaded();

    [RelayCommand]
    private void AddSearch()
    {
        if (!AvitoUrl.TryParseSearchUrl(NewSearchUrl, out var url, out var error))
        {
            NewSearchError = error;
            return;
        }

        var name = AvitoUrl.SuggestName(url);
        var search = new SavedSearch
        {
            Id = SearchRepository.NewId(),
            Name = name,
            Category = AvitoUrl.DetectCategory(url),
            CreatedAt = DateTime.Now,
            Sources = [new SearchSource { Label = name, Url = url.ToString() }],
        };
        _catalog.Add(search);
        _logger.LogInformation("Добавлен поиск «{Search}» ({Category})", search.Name, search.Category.ToRussian());

        var item = CreateItem(search);
        Searches.Add(item);
        SelectedSearch = item;
        item.SelectedTab = SearchItemViewModel.EditorTab; // сразу дать поправить название и категорию

        NewSearchUrl = "";
        NewSearchError = null;
    }

    [RelayCommand]
    private void DismissBanner() => Banner = null;

    [RelayCommand]
    private void ClearFeed()
    {
        Feed.Clear();
        FeedHeader = "Новые объявления";
    }

    private SearchItemViewModel CreateItem(SavedSearch search)
    {
        var item = new SearchItemViewModel(search, _monitor, _reports, _repository, _catalog, _logger);
        item.DeleteConfirmed += item => _ = DeleteAsync(item);
        return item;
    }

    private async Task DeleteAsync(SearchItemViewModel item)
    {
        await _monitor.StopAsync(item.Model);
        _catalog.Delete(item.Model);
        _logger.LogInformation("Удалён поиск «{Search}»", item.Model.Name);

        Searches.Remove(item);
        SelectedSearch = Searches.FirstOrDefault();
    }

    private void AddToFeed(SavedSearch search, ListingAssessment assessment)
    {
        Feed.Insert(0, new FeedItemViewModel(search, assessment));
        while (Feed.Count > FeedLimit)
            Feed.RemoveAt(Feed.Count - 1);

        FeedHeader = $"Новые объявления ({Feed.Count})";
        Find(search)?.OnNewListing();
    }

    private SearchItemViewModel? Find(SavedSearch search) => Searches.FirstOrDefault(item => item.Model.Id == search.Id);

    private static void OnUi(Action action) => Dispatcher.UIThread.Post(action);
}
