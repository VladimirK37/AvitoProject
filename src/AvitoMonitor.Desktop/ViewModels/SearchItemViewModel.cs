using AvitoMonitor.Analysis;
using AvitoMonitor.Domain;
using AvitoMonitor.Export;
using AvitoMonitor.Storage;
using AvitoMonitor.Workflows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Desktop.ViewModels;

/// <summary>
/// Один сохранённый поиск: строка в списке слева и вся правая часть окна
/// (кнопки, таблица объявлений, рынок, редактирование).
/// </summary>
public sealed partial class SearchItemViewModel : ObservableObject
{
    public const int ListingsTab = 0;
    public const int MarketTab = 1;
    public const int EditorTab = 2;

    private readonly MonitorService _monitor;
    private readonly ReportService _reports;
    private readonly ILogger _logger;

    private IReadOnlyList<ListingRowViewModel> _allRows = [];
    private bool _loaded;
    private bool _reloadRequested;
    private string? _scanProgressText;

    public SearchItemViewModel(SavedSearch model, MonitorService monitor, ReportService reports,
        ISearchStore store, SearchCatalog catalog, ILogger logger)
    {
        Model = model;
        _monitor = monitor;
        _reports = reports;
        _logger = logger;

        Editor = new SearchEditorViewModel(model, store, catalog, logger);
        Editor.Saved += OnEditorSaved;

        RefreshTitle();
        RefreshStatus();
    }

    /// <summary>Пользователь подтвердил удаление поиска.</summary>
    public event Action<SearchItemViewModel>? DeleteConfirmed;

    public SavedSearch Model { get; }

    public SearchEditorViewModel Editor { get; }

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _categoryTitle = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(ContinueScanCommand), nameof(WatchCommand),
        nameof(ScanThenWatchCommand), nameof(StopCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private int _selectedTab;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _message;

    // ----- Таблица «Объявления» -----

    [ObservableProperty]
    private IReadOnlyList<ListingRowViewModel> _listings = [];

    [ObservableProperty]
    private string _listingsCountText = "";

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private bool _hideExcluded = true;

    [ObservableProperty]
    private bool _onlyPrivate;

    [ObservableProperty]
    private bool _onlyBelowMarket;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnHideExcludedChanged(bool value) => ApplyFilter();

    partial void OnOnlyPrivateChanged(bool value) => ApplyFilter();

    partial void OnOnlyBelowMarketChanged(bool value) => ApplyFilter();

    // ----- Вкладка «Рынок» -----

    [ObservableProperty]
    private IReadOnlyList<GroupRowViewModel> _groups = [];

    [ObservableProperty]
    private string _marketSummary = "";

    [ObservableProperty]
    private string _sellersSummary = "";

    // ----- Удаление -----

    [ObservableProperty]
    private bool _confirmDelete;

    // ===================================================================
    // Кнопки
    // ===================================================================

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void ScanThenWatch() => StartSafely(() => _monitor.StartScan(Model, restart: true, thenWatch: true));

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Scan() => StartSafely(() => _monitor.StartScan(Model, restart: true));

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void ContinueScan() => StartSafely(() => _monitor.StartScan(Model, restart: false));

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Watch() => StartSafely(() => _monitor.StartWatch(Model));

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private async Task StopAsync()
    {
        Message = "Останавливаю…";
        await _monitor.StopAsync(Model);
        Message = null;
    }

    /// <summary>Новый Excel-отчёт: файл в папке reports + запись в базе (виден на вкладке «Отчёты»).</summary>
    [RelayCommand]
    private async Task OpenExcelAsync()
    {
        Message = "Строю таблицу Excel…";
        try
        {
            var report = await Task.Run(() => _reports.CreateReport(Model));
            Message = SystemShell.Open(report.FilePath) ? null : $"Таблица сохранена: {report.FilePath} (программа для .xlsx не найдена)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "«{Search}»: не удалось построить таблицу — {Message}", Model.Name, ex.Message);
            Message = $"Не удалось построить таблицу: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task ReloadAsync() => LoadDataAsync();

    [RelayCommand]
    private void RequestDelete() => ConfirmDelete = true;

    [RelayCommand]
    private void CancelDelete() => ConfirmDelete = false;

    [RelayCommand]
    private void ConfirmDeleteSearch() => DeleteConfirmed?.Invoke(this);

    private bool CanStart() => !IsBusy;

    /// <summary>Запуск сбора после нажатия «Найти» в форме нового поиска.</summary>
    public void StartFromSearchForm(bool thenWatch)
    {
        if (IsBusy)
        {
            Message = "По этому поиску уже идёт работа — дождитесь окончания или нажмите «Стоп».";
            return;
        }

        StartSafely(() => _monitor.StartScan(Model, restart: true, thenWatch));
    }

    private void StartSafely(Action start)
    {
        try
        {
            Message = null;
            start();
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
        }
    }

    // ===================================================================
    // Обновление данных (вызывается главным окном по событиям MonitorService)
    // ===================================================================

    /// <summary>Загрузить данные при первом показе поиска.</summary>
    public void EnsureLoaded()
    {
        if (!_loaded)
            _ = LoadDataAsync();
    }

    /// <summary>Пересчитать рынок и таблицу (в фоне, чтобы окно не подвисало).</summary>
    public async Task LoadDataAsync()
    {
        if (IsLoading)
        {
            _reloadRequested = true;
            return;
        }

        IsLoading = true;
        try
        {
            var (analysis, rows) = await Task.Run(() => _reports.Analyze(Model));

            _allRows = rows.Select(row => new ListingRowViewModel(row)).ToList();
            Groups = GroupKeys.OrderByGroup(analysis.Market.Groups.Values, g => g.Key)
                .Select(g => new GroupRowViewModel(g, analysis.Market.PerSquareMeter))
                .ToList();

            var replicas = rows.Count(r => r.Exclusion?.Kind == ExclusionKind.Replica);
            var otherModels = rows.Count(r => r.Exclusion?.Kind == ExclusionKind.OtherModel);
            MarketSummary = $"Собрано объявлений: {rows.Count}. Не учтено в рыночной цене: копий — {replicas}, других моделей — {otherModels}. " +
                            (analysis.Market.PerSquareMeter ? "Сравнивается цена за м²." : "Сравнивается цена.");
            SellersSummary = "Продавцы: " + string.Join(", ", rows.GroupBy(r => r.Seller.Title)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} — {g.Count()}"));

            ApplyFilter();
            _loaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "«{Search}»: не удалось загрузить объявления — {Message}", Model.Name, ex.Message);
            Message = $"Не удалось загрузить объявления: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        if (_reloadRequested)
        {
            _reloadRequested = false;
            await LoadDataAsync();
        }
    }

    /// <summary>Статус в списке и над таблицей: идёт сбор, слежение или ничего.</summary>
    public void RefreshStatus()
    {
        var wasBusy = IsBusy;
        var job = _monitor.GetJob(Model);
        IsBusy = job != JobKind.None;
        Editor.IsLocked = IsBusy;

        StatusText = job switch
        {
            JobKind.Scanning => _scanProgressText ?? "Идёт сбор объявлений…",
            JobKind.Watching => _monitor.GetNextCheckAt(Model) is { } next
                ? $"Слежение · следующая проверка в {next:HH:mm}"
                : "Слежение · идёт проверка…",
            _ => IdleStatus(),
        };

        if (job != JobKind.Scanning)
            _scanProgressText = null;

        // Задача закончилась — показываем свежие данные
        if (wasBusy && !IsBusy && _loaded)
            _ = LoadDataAsync();
    }

    public void OnScanProgress(ScanProgress progress)
    {
        var total = progress.TotalOnAvito is { } t ? $" из ~{t}" : "";
        _scanProgressText = $"Идёт сбор · {progress.SourceLabel} · страница {progress.Page} · собрано {progress.TotalCollected}{total}";
        StatusText = _scanProgressText;
    }

    public void OnNewListing()
    {
        if (_loaded)
            _ = LoadDataAsync();
    }

    private string IdleStatus()
    {
        var unfinished = Model.Sources.Any(s => !s.ScanCompleted) && Model.LastScanAt is not null ? " · можно «Дособрать»" : "";
        return Model.LastScanAt is { } last
            ? $"Остановлено · последний сбор {last:dd.MM HH:mm}{unfinished}"
            : "Остановлено · сбор ещё не запускался";
    }

    private void ApplyFilter()
    {
        IEnumerable<ListingRowViewModel> rows = _allRows;

        if (HideExcluded)
            rows = rows.Where(r => !r.IsExcluded);
        if (OnlyPrivate)
            rows = rows.Where(r => !r.IsBusiness);
        if (OnlyBelowMarket)
            rows = rows.Where(r => r.Deviation < 0);

        var text = FilterText.Trim();
        if (text.Length > 0)
        {
            rows = rows.Where(r => r.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                                   || r.SellerName.Contains(text, StringComparison.OrdinalIgnoreCase)
                                   || r.Address.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        // Новый список целиком — таблица перерисуется один раз, а не на каждую строку
        Listings = rows.ToList();
        ListingsCountText = $"Показано {Listings.Count} из {_allRows.Count}";
    }

    private void OnEditorSaved()
    {
        RefreshTitle();
        RefreshStatus();
        _ = LoadDataAsync();
    }

    private void RefreshTitle()
    {
        Name = Model.Name;
        CategoryTitle = Model.Sources.Count > 1
            ? $"{Model.Category.ToRussian()} · мультипоиск, ссылок: {Model.Sources.Count}"
            : Model.Category.ToRussian();
    }
}
