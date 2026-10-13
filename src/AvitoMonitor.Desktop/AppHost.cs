using AvitoMonitor.Analysis;
using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using AvitoMonitor.Desktop.Logging;
using AvitoMonitor.Desktop.ViewModels;
using AvitoMonitor.Export;
using AvitoMonitor.History;
using AvitoMonitor.Persistence;
using AvitoMonitor.Storage;
using AvitoMonitor.Telegram;
using AvitoMonitor.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AvitoMonitor.Desktop;

/// <summary>
/// «Сборка» программы: какие службы есть и как они связаны (dependency injection).
/// Все службы — синглтоны: одна копия на всю программу.
/// </summary>
public static class AppHost
{
    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        var paths = AppPaths.CreateDefault();
        var settingsStore = new SettingsStore(paths.SettingsFile);
        var settings = settingsStore.Load();

        // Журнал: консоль + файл (logs\avito-ГГГГ-ММ-ДД.log) + вкладка «Журнал» в окне
        var logProvider = new AppLoggerProvider(paths.LogsDir);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logProvider);
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);

        var services = builder.Services;

        // Настройки
        services.AddSingleton(paths);
        services.AddSingleton(settingsStore);
        services.AddSingleton(settings);
        services.AddSingleton(logProvider);

        // Базы данных: PostgreSQL (всё) + Elasticsearch (поиск по истории запросов)
        services.AddDbContextFactory<AvitoDbContext>(options => options.UseNpgsql(settings.DatabaseConnectionString));
        services.AddSingleton<ISearchStore, PostgresSearchStore>();
        services.AddSingleton(new LegacyJsonSearchRepository(paths.SearchesDir));
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<SearchCatalog>();
        services.AddSingleton<ElasticQueryIndex>();
        services.AddSingleton<QueryHistoryService>();

        // Авито, Telegram, анализ, отчёты
        services.AddSingleton<AvitoBrowser>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
        services.AddSingleton<TelegramApi>();
        services.AddSingleton<TelegramNotifier>();
        services.AddSingleton<SearchAnalyzer>();
        services.AddSingleton<ExcelReportBuilder>();
        services.AddSingleton<ReportService>();

        // Сценарии и фоновые задачи
        services.AddSingleton<ScanWorkflow>();
        services.AddSingleton<WatchWorkflow>();
        services.AddSingleton<MonitorService>();
        services.AddHostedService<MonitorHostedService>();

        // Интерфейс
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        var host = builder.Build();
        host.Services.GetRequiredService<ILogger<App>>().LogInformation("AvitoMonitor запущен. Данные программы: {Folder}", paths.Root);
        return host;
    }

    /// <summary>
    /// Подготовить базу: миграции и перенос старых JSON-данных.
    /// Возвращает текст ошибки для пользователя или null, если всё в порядке.
    /// </summary>
    public static string? InitializeDatabase(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<App>>();
        try
        {
            services.GetRequiredService<DatabaseInitializer>().Initialize();
            return null;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            var settings = services.GetRequiredService<AppSettings>();
            var where = new NpgsqlConnectionStringBuilder(settings.DatabaseConnectionString) { Password = null }.ToString();
            logger.LogError(ex, "Не удалось подключиться к PostgreSQL ({Where}): {Message}", where, ex.Message);

            return "Не удалось подключиться к базе данных PostgreSQL." + Environment.NewLine + Environment.NewLine +
                   "Как запустить базы:" + Environment.NewLine +
                   "1. Откройте Docker Desktop и дождитесь, пока он запустится." + Environment.NewLine +
                   "2. В папке проекта выполните команду:  docker compose up -d" + Environment.NewLine +
                   "3. Запустите AvitoMonitor снова." + Environment.NewLine + Environment.NewLine +
                   $"Подключение: {where}" + Environment.NewLine +
                   "(меняется в DatabaseConnectionString в файле settings.json в папке данных)" + Environment.NewLine + Environment.NewLine +
                   $"Ошибка: {ex.Message}";
        }
    }
}

/// <summary>
/// При запуске: продолжает слежение и в фоне синхронизирует историю запросов с Elasticsearch.
/// При закрытии: останавливает все задачи.
/// </summary>
public sealed class MonitorHostedService(MonitorService monitor, QueryHistoryService history) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        monitor.ResumeWatching();
        _ = Task.Run(() => history.SyncElasticAsync(), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => monitor.StopAllAsync();
}
