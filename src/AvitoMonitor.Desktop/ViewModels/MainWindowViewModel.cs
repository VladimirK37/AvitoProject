using System.Collections.ObjectModel;
using Avalonia.Threading;
using AvitoMonitor.Avito;
using AvitoMonitor.Domain;
using AvitoMonitor.Export;
using AvitoMonitor.History;
using AvitoMonitor.Storage;
using AvitoMonitor.Workflows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Desktop.ViewModels;

/// <summary>
/// Главное окно: форма поиска, список поисков, лента новых объявлений, история, отчёты, журнал и настройки.
/// Подписывается на события фоновых задач (MonitorService) и переносит их в поток интерфейса.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public const int SearchesTab = 0;

    private const int FeedLimit = 500;

    private readonly SearchCatalog _catalog;
    private readonly ISearchStore _store;
    private readonly MonitorService _monitor;
    private readonly ReportService _reports;
    private readonly QueryHistoryService _history;
    private readonly ILogger<MainWindowViewModel> _logger;

    public MainWindowViewModel(SearchCatalog catalog, ISearchStore store, MonitorService monitor, ReportService reports,
        QueryHistoryService history, SettingsViewModel settings, LogViewModel log, HistoryViewModel historyTab,
        ReportsViewModel reportsTab, ILogger<MainWindowViewModel> logger)
    {
        _catalog = catalog;
        _store = store;
        _monitor = monitor;
        _reports = reports;
        _history = history;
        _logger = logger;
        Settings = settings;
        Log = log;
        HistoryTab = historyTab;
        ReportsTab = reportsTab;

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

        historyTab.UseRequested += (entry, run) => _ = UseHistoryEntryAsync(entry, run);
    }

    public ObservableCollection<SearchItemViewModel> Searches { get; } = new();

    public ObservableCollection<FeedItemViewModel> Feed { get; } = new();

    public SettingsViewModel Settings { get; }

    public LogViewModel Log { get; }

    public HistoryViewModel HistoryTab { get; }

    public ReportsViewModel ReportsTab { get; }

    [ObservableProperty]
    private int _mainTab;

    [ObservableProperty]
    private SearchItemViewModel? _selectedSearch;

    /// <summary>Жёлтая полоса сверху: «Авито просит пройти проверку».</summary>
    [ObservableProperty]
    private string? _banner;

    [ObservableProperty]
    private string _feedHeader = "Новые объявления";

    partial void OnSelectedSearchChanged(SearchItemViewModel? value) => value?.EnsureLoaded();

    // ===================================================================
    // Форма «Что ищем на Авито»
    // ===================================================================

    public IReadOnlyList<AvitoCity> Cities => AvitoCities.All;

    public IReadOnlyList<CategoryOption> Categories => CategoryOption.All;

    [ObservableProperty]
    private string _keywords = "";

    [ObservableProperty]
    private AvitoCity _selectedCity = AvitoCities.Find(AvitoCities.DefaultSlug);

    [ObservableProperty]
    private decimal? _priceMin;

    [ObservableProperty]
    private decimal? _priceMax;

    [ObservableProperty]
    private CategoryOption _selectedCategory = CategoryOption.Of(SearchCategory.General);

    [ObservableProperty]
    private bool _watchAfterScan = true;

    [ObservableProperty]
    private string? _searchFormError;

    /// <summary>Выбрали подсказку из истории — подставляем и город с ценой.</summary>
    [ObservableProperty]
    private QueryHistoryEntry? _selectedSuggestion;

    partial void OnSelectedSuggestionChanged(QueryHistoryEntry? value)
    {
        if (value is not null)
            FillForm(value);
    }

    /// <summary>Подсказки под полем поиска: история запросов (Elasticsearch или PostgreSQL).</summary>
    public Func<string?, CancellationToken, Task<IEnumerable<object>>> SuggestionsPopulator => LoadSuggestionsAsync;

    /// <summary>
    /// «Найти»: запоминает запрос в истории, создаёт поиск (или берёт такой же существующий)
    /// и запускает сбор. После сбора сам создаётся Excel-отчёт, а при галочке — включается слежение.
    /// </summary>
    [RelayCommand]
    private async Task FindAsync()
    {
        var query = Keywords.Trim();
        if (query.Length == 0)
        {
            SearchFormError = "Напишите, что искать, например: airpods 4";
            return;
        }

        var request = new SearchRequest(query, SelectedCity.Slug, SourceEditViewModel.ToInt(PriceMin), SourceEditViewModel.ToInt(PriceMax));
        if (request.PriceMin is { } min && request.PriceMax is { } max && min > max)
        {
            SearchFormError = "Цена «от» больше цены «до».";
            return;
        }

        SearchFormError = null;

        // Такой же поиск уже есть — используем его, чтобы не плодить дубликаты
        var item = Searches.FirstOrDefault(s => s.Model.Sources.Count == 1 && IsSameRequest(s.Model.Sources[0], request));
        if (item is null)
        {
            var search = new SavedSearch
            {
                Id = SavedSearch.NewId(),
                Name = query,
                Category = SelectedCategory.Value,
                CreatedAt = DateTime.Now,
                Sources =
                [
                    new SearchSource
                    {
                        Label = query,
                        Query = query,
                        City = request.City,
                        PriceMin = request.PriceMin,
                        PriceMax = request.PriceMax,
                        Url = request.ToUrl().ToString(),
                    },
                ],
            };

            _catalog.Add(search);
            item = CreateItem(search);
            Searches.Add(item);
            _logger.LogInformation("Новый поиск: {Request}", request.Describe());
        }

        SelectedSearch = item;
        item.SelectedTab = SearchItemViewModel.ListingsTab;
        MainTab = SearchesTab;

        try
        {
            await _history.RecordAsync(request, item.Model.Id);
            await HistoryTab.RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить запрос в историю: {Message}", ex.Message);
        }

        item.StartFromSearchForm(WatchAfterScan);
    }

    [RelayCommand]
    private void DismissBanner() => Banner = null;

    [RelayCommand]
    private void ClearFeed()
    {
        Feed.Clear();
        FeedHeader = "Новые объявления";
    }

    private async Task<IEnumerable<object>> LoadSuggestionsAsync(string? text, CancellationToken ct)
    {
        try
        {
            var (entries, _) = await _history.SearchAsync(text ?? "", 10, ct);
            return entries;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Не удалось получить подсказки из истории: {Message}", ex.Message);
            return [];
        }
    }

    private async Task UseHistoryEntryAsync(QueryHistoryEntry entry, bool run)
    {
        FillForm(entry);
        MainTab = SearchesTab;
        if (run)
            await FindAsync();
    }

    private void FillForm(QueryHistoryEntry entry)
    {
        Keywords = entry.Text;
        SelectedCity = AvitoCities.Find(entry.City);
        PriceMin = entry.PriceMin;
        PriceMax = entry.PriceMax;
    }

    private static bool IsSameRequest(SearchSource source, SearchRequest request) =>
        string.Equals(source.Query?.Trim(), request.Query, StringComparison.OrdinalIgnoreCase)
        && source.City == request.City
        && source.PriceMin == request.PriceMin
        && source.PriceMax == request.PriceMax;

    // ===================================================================

    private SearchItemViewModel CreateItem(SavedSearch search)
    {
        var item = new SearchItemViewModel(search, _monitor, _reports, _store, _catalog, _logger);
        item.DeleteConfirmed += item => _ = DeleteAsync(item);
        return item;
    }

    private async Task DeleteAsync(SearchItemViewModel item)
    {
        await _monitor.StopAsync(item.Model);
        _catalog.Delete(item.Model);
        _logger.LogInformation("Удалён поиск «{Search}» (отчёты по нему остались на вкладке «Отчёты»)", item.Model.Name);

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
