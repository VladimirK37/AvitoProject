using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Desktop.Logging;

/// <summary>Одна запись журнала.</summary>
public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string LevelText => Level switch
    {
        LogLevel.Warning => "ВНИМАНИЕ",
        LogLevel.Error or LogLevel.Critical => "ОШИБКА",
        _ => "",
    };

    public bool IsWarning => Level == LogLevel.Warning;

    public bool IsError => Level >= LogLevel.Error;

    public string Display => LevelText.Length > 0 ? $"{Time:HH:mm:ss}  {LevelText}: {Message}" : $"{Time:HH:mm:ss}  {Message}";
}

/// <summary>
/// Журнал программы — бывшая консольная версия. Каждая запись идёт в три места:
/// <list type="bullet">
/// <item>в консоль (окно рядом с программой), с цветом для предупреждений и ошибок;</item>
/// <item>в файл logs\avito-ГГГГ-ММ-ДД.log (с полным текстом ошибок);</item>
/// <item>во вкладку «Журнал» в окне программы (через событие EntryWritten).</item>
/// </list>
/// </summary>
public sealed class AppLoggerProvider(string logsDir) : ILoggerProvider
{
    private const int RecentLimit = 500;

    private readonly object _writeLock = new();
    private readonly ConcurrentQueue<LogEntry> _recent = new();

    /// <summary>Новая запись (приходит из любого потока).</summary>
    public event Action<LogEntry>? EntryWritten;

    /// <summary>Последние записи — чтобы вкладка «Журнал» показала и то, что было до открытия окна.</summary>
    public IReadOnlyList<LogEntry> Recent => _recent.ToList();

    public ILogger CreateLogger(string categoryName) => new AppLogger(this);

    public void Dispose()
    {
    }

    internal void Write(LogEntry entry, Exception? exception)
    {
        lock (_writeLock)
        {
            WriteToConsole(entry);
            WriteToFile(entry, exception);
        }

        _recent.Enqueue(entry);
        while (_recent.Count > RecentLimit)
            _recent.TryDequeue(out _);

        EntryWritten?.Invoke(entry);
    }

    private static void WriteToConsole(LogEntry entry)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = entry.Level switch
        {
            LogLevel.Warning => ConsoleColor.Yellow,
            >= LogLevel.Error => ConsoleColor.Red,
            _ => previous,
        };
        Console.WriteLine(entry.Display);
        Console.ForegroundColor = previous;
    }

    private void WriteToFile(LogEntry entry, Exception? exception)
    {
        try
        {
            var file = Path.Combine(logsDir, $"avito-{entry.Time:yyyy-MM-dd}.log");
            var text = $"{entry.Time:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}{Environment.NewLine}";
            if (exception is not null)
                text += exception + Environment.NewLine;

            File.AppendAllText(file, text);
        }
        catch (IOException)
        {
            // Журнал не должен ронять программу
        }
    }

    private sealed class AppLogger(AppLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                provider.Write(new LogEntry(DateTime.Now, logLevel, formatter(state, exception)), exception);
        }
    }
}
