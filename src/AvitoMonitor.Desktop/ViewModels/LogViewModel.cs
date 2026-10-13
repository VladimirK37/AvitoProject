using System.Collections.ObjectModel;
using Avalonia.Threading;
using AvitoMonitor.Configuration;
using AvitoMonitor.Desktop.Logging;
using CommunityToolkit.Mvvm.Input;

namespace AvitoMonitor.Desktop.ViewModels;

/// <summary>Вкладка «Журнал»: то же, что в окне консоли, новые записи сверху.</summary>
public sealed partial class LogViewModel
{
    private const int Limit = 2000;

    private readonly AppPaths _paths;

    public LogViewModel(AppLoggerProvider provider, AppPaths paths)
    {
        _paths = paths;

        // То, что успело записаться до открытия окна
        foreach (var entry in provider.Recent.Reverse())
            Entries.Add(entry);

        provider.EntryWritten += entry => Dispatcher.UIThread.Post(() => Add(entry));
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();

    [RelayCommand]
    private void OpenLogsFolder() => SystemShell.Open(_paths.LogsDir);

    [RelayCommand]
    private void Clear() => Entries.Clear();

    private void Add(LogEntry entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > Limit)
            Entries.RemoveAt(Entries.Count - 1);
    }
}
