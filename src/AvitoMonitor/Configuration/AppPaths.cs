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
    }

    /// <summary>Корневая папка с данными.</summary>
    public string Root { get; }

    /// <summary>Настройки: токен бота, паузы, интервалы.</summary>
    public string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>Сохранённые поиски: в каждой подпапке ссылка, собранные объявления и Excel-отчёт.</summary>
    public string SearchesDir => Path.Combine(Root, "searches");

    /// <summary>Профиль браузера программы (cookies Авито), отдельный от вашего обычного браузера.</summary>
    public string BrowserProfileDir => Path.Combine(Root, "browser-profile");

    /// <summary>Сюда сохраняется HTML страницы, если программа не смогла найти на ней объявления.</summary>
    public string DebugDir => Path.Combine(Root, "debug");

    /// <summary>Журнал ошибок.</summary>
    public string ErrorLogFile => Path.Combine(Root, "errors.log");

    public static AppPaths CreateDefault()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("AVITO_MONITOR_DATA");
        var root = !string.IsNullOrWhiteSpace(fromEnvironment)
            ? fromEnvironment
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AvitoMonitor");

        return new AppPaths(root);
    }
}
