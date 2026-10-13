using System.Text;
using Avalonia;
using Microsoft.Extensions.Hosting;

namespace AvitoMonitor.Desktop;

/// <summary>
/// Точка входа:
/// 1) собираем «хост» — все службы программы (базы, браузер, слежение, Telegram, журнал);
/// 2) готовим базу PostgreSQL (миграции, перенос старых данных);
/// 3) запускаем окно Avalonia;
/// 4) после закрытия окна аккуратно останавливаем слежение и закрываем браузер.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        TrySetConsoleTitle("AvitoMonitor — журнал работы");

        var host = AppHost.Build(args);
        App.Services = host.Services;

        // База недоступна — покажем окно с инструкцией вместо главного окна
        App.StartupError = AppHost.InitializeDatabase(host.Services);
        if (App.StartupError is null)
            host.Start(); // здесь же продолжается слежение, включённое до перезапуска

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Слежение останавливается, но запоминается — при следующем запуске продолжится само
            if (App.StartupError is null)
                host.StopAsync().GetAwaiter().GetResult();

            ((IAsyncDisposable)host).DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // Используется и дизайнером разметки в Rider — не удалять
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void TrySetConsoleTitle(string title)
    {
        try
        {
            Console.Title = title;
        }
        catch (IOException)
        {
            // Консоли нет (например, запуск из IDE без неё) — не страшно
        }
    }
}
