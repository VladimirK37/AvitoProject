using AvitoMonitor.History;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Desktop.ViewModels;

/// <summary>
/// Вкладка «История запросов»: всё, что вводили в поиск.
/// Поиск по истории — через Elasticsearch (с опечатками), а если он не запущен — через PostgreSQL.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly QueryHistoryService _history;
    private readonly ILogger<HistoryViewModel> _logger;
    private CancellationTokenSource? _searchCancellation;

    public HistoryViewModel(QueryHistoryService history, ILogger<HistoryViewModel> logger)
    {
        _history = history;
        _logger = logger;
        _ = LoadAsync(CancellationToken.None);
    }

    /// <summary>Пользователь выбрал запрос: подставить в форму поиска (Run = сразу искать).</summary>
    public event Action<QueryHistoryEntry, bool>? UseRequested;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private IReadOnlyList<QueryHistoryEntry> _entries = [];

    [ObservableProperty]
    private string _statusText = "";

    // Ищем, пока человек печатает, но не чаще раза в 0,3 секунды
    partial void OnFilterTextChanged(string value) => _ = LoadDebouncedAsync();

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(CancellationToken.None);

    [RelayCommand]
    private void Use(QueryHistoryEntry entry) => UseRequested?.Invoke(entry, false);

    [RelayCommand]
    private void Run(QueryHistoryEntry entry) => UseRequested?.Invoke(entry, true);

    private async Task LoadDebouncedAsync()
    {
        _searchCancellation?.Cancel();
        var cancellation = _searchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, cancellation.Token);
            await LoadAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Человек продолжил печатать — этот поиск больше не нужен
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            var (entries, fromElastic) = await _history.SearchAsync(FilterText, 200, ct);
            Entries = entries;
            StatusText = (fromElastic
                ? "Поиск через Elasticsearch (понимает опечатки и окончания)"
                : "Elasticsearch недоступен — поиск по PostgreSQL (только точное совпадение части текста)") + $" · найдено: {entries.Count}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Не удалось загрузить историю запросов: {Message}", ex.Message);
            StatusText = $"Не удалось загрузить историю: {ex.Message}";
        }
    }
}
