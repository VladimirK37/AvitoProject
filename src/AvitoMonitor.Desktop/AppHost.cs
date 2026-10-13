using AvitoMonitor.Analysis;
using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using AvitoMonitor.Desktop.Logging;
using AvitoMonitor.Desktop.ViewModels;
using AvitoMonitor.Export;
using AvitoMonitor.Storage;
using AvitoMonitor.Telegram;
using AvitoMonitor.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

        // Настройки и данные
        services.AddSingleton(paths);
        services.AddSingleton(settingsStore);
        services.AddSingleton(settings);
        services.AddSingleton(logProvider);
        services.AddSingleton(new SearchRepository(paths.SearchesDir));
        services.AddSingleton<SearchCatalog>();

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
        services.AddSingleton<MainWindowViewModel>();

        var host = builder.Build();
        host.Services.GetRequiredService<ILogger<App>>().LogInformation("AvitoMonitor запущен. Данные программы: {Folder}", paths.Root);
        return host;
    }
}

/// <summary>При запуске продолжает слежение, при закрытии — останавливает все задачи.</summary>
public sealed class MonitorHostedService(MonitorService monitor) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        monitor.ResumeWatching();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => monitor.StopAllAsync();
}
