using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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

    /// <summary>Не удалось подготовить базу — текст для пользователя (иначе null).</summary>
    public static string? StartupError { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (StartupError is not null)
            {
                desktop.MainWindow = CreateStartupErrorWindow(StartupError);
            }
            else
            {
                desktop.MainWindow = new MainWindow { DataContext = Services.GetRequiredService<MainWindowViewModel>() };

                // Ctrl+C в окне консоли — аккуратно закрыть и окно программы
                Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping
                    .Register(() => Dispatcher.UIThread.Post(() => desktop.Shutdown()));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Простое окно «база недоступна — вот что сделать».</summary>
    private static Window CreateStartupErrorWindow(string message)
    {
        var close = new Button { Content = "Закрыть", HorizontalAlignment = HorizontalAlignment.Right };
        var window = new Window
        {
            Title = "AvitoMonitor — нет подключения к базе данных",
            Width = 760,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new DockPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    close,
                    new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 },
                },
            },
        };
        DockPanel.SetDock(close, Dock.Bottom);
        close.Click += (_, _) => window.Close();
        return window;
    }
}
