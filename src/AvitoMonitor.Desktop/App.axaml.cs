using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvitoMonitor.Desktop.ViewModels;
using AvitoMonitor.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AvitoMonitor.Desktop;

public partial class App : Application
{
    /// <summary>Службы программы (заполняется в Program.Main до запуска окна).</summary>
    public static IServiceProvider Services { get; set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = Services.GetRequiredService<MainWindowViewModel>() };

            // Ctrl+C в окне консоли — аккуратно закрыть и окно программы
            Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping
                .Register(() => Dispatcher.UIThread.Post(() => desktop.Shutdown()));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
