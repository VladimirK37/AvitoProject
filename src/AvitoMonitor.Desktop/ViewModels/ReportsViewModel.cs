using Avalonia.Threading;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;
using AvitoMonitor.Export;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Desktop.ViewModels;

/// <summary>Строка таблицы «Отчёты».</summary>
public sealed class ReportRowViewModel(ReportRecord record)
{
    public ReportRecord Record { get; } = record;
    public DateTime CreatedAt { get; } = record.CreatedAt;
    public string CreatedText { get; } = record.CreatedAt.ToString("dd.MM.yyyy HH:mm");
    public string SearchName { get; } = record.SearchName;
    public string Description { get; } = record.Description;
    public int ListingsCount { get; } = record.ListingsCount;
    public int ExcludedCount { get; } = record.ExcludedCount;
    public string Summary { get; } = record.Summary;
    public string FilePath { get; } = record.FilePath;
    public string FileName { get; } = Path.GetFileName(record.FilePath);
}

/// <summary>
/// Вкладка «Отчёты»: все Excel-отчёты из базы (новые сверху).
/// Отчёт создаётся сам после каждого сбора и по кнопке «Открыть в Excel».
/// </summary>
public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly ReportService _reports;
    private readonly AppPaths _paths;
    private readonly ILogger<ReportsViewModel> _logger;
    private List<ReportRowViewModel> _all = [];

    public ReportsViewModel(ReportService reports, AppPaths paths, ILogger<ReportsViewModel> logger)
    {
        _reports = reports;
        _paths = paths;
        _logger = logger;

        // Отчёт может появиться из фоновой задачи — переносим в поток интерфейса
        reports.ReportCreated += report => Dispatcher.UIThread.Post(() =>
        {
            _all.Insert(0, new ReportRowViewModel(report));
            ApplyFilter();
        });

        _ = LoadAsync();
    }

    [ObservableProperty]
    private IReadOnlyList<ReportRowViewModel> _items = [];

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private string _statusText = "";

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var reports = await Task.Run(() => _reports.GetReports());
            _all = reports.Select(r => new ReportRowViewModel(r)).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось загрузить отчёты: {Message}", ex.Message);
            StatusText = $"Не удалось загрузить отчёты: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Open(ReportRowViewModel row)
    {
        if (!File.Exists(row.FilePath))
        {
            StatusText = $"Файл не найден: {row.FilePath}. Возможно, его удалили или переместили.";
            return;
        }

        if (!SystemShell.Open(row.FilePath))
            StatusText = $"Не нашлось программы для .xlsx. Файл: {row.FilePath}";
    }

    [RelayCommand]
    private void OpenFolder() => SystemShell.Open(_paths.ReportsDir);

    [RelayCommand]
    private void Delete(ReportRowViewModel row)
    {
        try
        {
            _reports.DeleteReport(row.Record, deleteFile: true);
            _all.Remove(row);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText = $"Не удалось удалить отчёт: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var text = FilterText.Trim();
        Items = text.Length == 0
            ? _all.ToList()
            : _all.Where(r => r.SearchName.Contains(text, StringComparison.OrdinalIgnoreCase)
                              || r.Description.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();

        StatusText = $"Отчётов: {Items.Count}. Файлы лежат в {_paths.ReportsDir}";
    }
}
