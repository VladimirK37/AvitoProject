namespace AvitoMonitor.Configuration;

/// <summary>
/// Все пути к файлам программы в одном месте.
///
/// Данные лежат в «Документы\AvitoMonitor», а не рядом с exe:
/// так они не пропадут при пересборке проекта и никогда не попадут в git
/// (в настройках хранится токен Telegram-бота).
/// Папку можно поменять переменной окружения AVITO_MONITOR_DATA.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SearchesDir);
        Directory.CreateDirectory(DebugDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(ReportsDir);
    }

    /// <summary>Корневая папка с данными.</summary>
    public string Root { get; }

    /// <summary>Настройки: токен бота, паузы, интервалы.</summary>
    public string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>
    /// Поиски в JSON-файлах — так программа хранила данные до перехода на PostgreSQL.
    /// При первом запуске с базой они переносятся туда автоматически.
    /// </summary>
    public string SearchesDir => Path.Combine(Root, "searches");

    /// <summary>Excel-отчёты: по файлу на каждый сбор (записи о них — в базе данных).</summary>
    public string ReportsDir => Path.Combine(Root, "reports");

    /// <summary>Профиль браузера программы (cookies Авито), отдельный от вашего обычного браузера.</summary>
    public string BrowserProfileDir => Path.Combine(Root, "browser-profile");

    /// <summary>Сюда сохраняется HTML страницы, если программа не смогла найти на ней объявления.</summary>
    public string DebugDir => Path.Combine(Root, "debug");

    /// <summary>Журнал работы программы: по файлу на день (logs\avito-2026-10-13.log).</summary>
    public string LogsDir => Path.Combine(Root, "logs");

    public static AppPaths CreateDefault()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("AVITO_MONITOR_DATA");
        var root = !string.IsNullOrWhiteSpace(fromEnvironment)
            ? fromEnvironment
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AvitoMonitor");

        return new AppPaths(root);
    }
}
