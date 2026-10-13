using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using AvitoMonitor.History;
using Avalonia.Threading;
using Npgsql;
using AvitoMonitor.Telegram;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Desktop.ViewModels;

/// <summary>Пункт списка «Браузер».</summary>
public sealed record BrowserOption(string Value, string Title)
{
    public override string ToString() => Title;
}

/// <summary>Вкладка «Настройки»: Telegram, темп работы с Авито, браузер.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly AppPaths _paths;
    private readonly AvitoBrowser _browser;
    private readonly TelegramApi _telegramApi;
    private readonly TelegramNotifier _telegram;
    private readonly QueryHistoryService _history;
    private readonly ILogger<SettingsViewModel> _logger;
    private CancellationTokenSource? _connectCancellation;

    public SettingsViewModel(AppSettings settings, SettingsStore store, AppPaths paths, AvitoBrowser browser,
        TelegramApi telegramApi, TelegramNotifier telegram, QueryHistoryService history, ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _store = store;
        _paths = paths;
        _browser = browser;
        _telegramApi = telegramApi;
        _telegram = telegram;
        _history = history;
        _logger = logger;
        LoadFromSettings();

        history.Elastic.AvailabilityChanged += () => Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(ElasticStatus)));
    }

    // ===================================================================
    // Базы данных (подключение меняется в settings.json, нужен перезапуск)
    // ===================================================================

    public string DatabaseStatus =>
        "PostgreSQL: " + new NpgsqlConnectionStringBuilder(_settings.DatabaseConnectionString) { Password = null };

    public string ElasticStatus => $"Elasticsearch ({_settings.ElasticsearchUrl}): " + _history.Elastic.IsAvailable switch
    {
        true => "подключён — поиск по истории понимает опечатки",
        false => "недоступен — история ищется по PostgreSQL",
        null => "ещё не проверялся",
    };

    [ObservableProperty]
    private string? _elasticSyncResult;

    /// <summary>Проверить Elasticsearch и переложить в него историю из PostgreSQL.</summary>
    [RelayCommand]
    private async Task SyncElasticAsync()
    {
        ElasticSyncResult = "Синхронизирую…";
        var count = await _history.SyncElasticAsync();
        OnPropertyChanged(nameof(ElasticStatus));
        ElasticSyncResult = _history.Elastic.IsAvailable == true
            ? $"Готово: запросов в индексе — {count}."
            : "Elasticsearch недоступен. Запустите его: docker compose up -d";
    }

    public IReadOnlyList<BrowserOption> Browsers { get; } =
    [
        new("msedge", "Microsoft Edge (есть в Windows, рекомендуется)"),
        new("chrome", "Google Chrome (должен быть установлен)"),
        new("chromium", "Chromium от Playwright (нужно скачать, см. README)"),
    ];

    public string DataFolder => _paths.Root;

    // ===================================================================
    // Работа с Авито
    // ===================================================================

    [ObservableProperty]
    private decimal? _delaySeconds;

    [ObservableProperty]
    private decimal? _watchIntervalMinutes;

    [ObservableProperty]
    private decimal? _cheapPercent;

    [ObservableProperty]
    private bool _openNewListingsForDetails;

    [ObservableProperty]
    private bool _hideReplicasAndOtherModels;

    [ObservableProperty]
    private BrowserOption? _selectedBrowser;

    [ObservableProperty]
    private bool _headless;

    [ObservableProperty]
    private string? _saveResult;

    [RelayCommand]
    private async Task SaveAsync()
    {
        var browser = SelectedBrowser?.Value ?? "msedge";
        var browserChanged = browser != _settings.BrowserChannel || Headless != _settings.Headless;

        _settings.DelayBetweenPagesSeconds = (int)(DelaySeconds ?? 8);
        _settings.WatchIntervalMinutes = (int)(WatchIntervalMinutes ?? 5);
        _settings.SuspiciouslyCheapPercent = (int)(CheapPercent ?? 40);
        _settings.OpenNewListingsForDetails = OpenNewListingsForDetails;
        _settings.HideReplicasAndOtherModels = HideReplicasAndOtherModels;
        _settings.BrowserChannel = browser;
        _settings.Headless = Headless;
        _store.Save(_settings);

        // Браузер перезапустится с новыми настройками при следующем запросе к Авито
        if (browserChanged)
            await _browser.CloseAsync();

        _logger.LogInformation("Настройки сохранены");
        SaveResult = $"Сохранено в {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    private void OpenDataFolder() => SystemShell.Open(_paths.Root);

    // ===================================================================
    // Telegram
    // ===================================================================

    [ObservableProperty]
    private string _telegramToken = "";

    [ObservableProperty]
    private string _telegramStatus = "";

    [ObservableProperty]
    private string? _botLink;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectTelegramCommand))]
    private bool _isConnecting;

    public string TelegramConnectedText => _settings.TelegramConfigured
        ? "✅ Telegram подключён — новые объявления приходят в ваш чат."
        : "Telegram не подключён — новые объявления видны только в программе.";

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectTelegramAsync()
    {
        var token = TelegramToken.Trim();
        if (token.Length == 0)
        {
            TelegramStatus = "Вставьте токен, который прислал @BotFather.";
            return;
        }

        IsConnecting = true;
        _connectCancellation = new CancellationTokenSource();
        try
        {
            TelegramStatus = "Проверяю токен…";
            var username = await _telegramApi.GetBotUsernameAsync(token, _connectCancellation.Token);
            BotLink = $"https://t.me/{username}";
            TelegramStatus = $"Бот @{username} найден. Нажмите «Открыть бота» и в Telegram нажмите «Запустить» (Start). Жду до 3 минут…";

            var chat = await _telegramApi.WaitForStartCommandAsync(token, TimeSpan.FromMinutes(3), _connectCancellation.Token);
            if (chat is null)
            {
                TelegramStatus = "Не дождался /start. Нажмите «Подключить» ещё раз.";
                return;
            }

            _settings.TelegramBotToken = token;
            _settings.TelegramChatId = chat.Id;
            _store.Save(_settings);

            await _telegramApi.SendMessageAsync(token, chat.Id,
                "✅ <b>AvitoMonitor подключён.</b>\nСюда будут приходить новые объявления с Авито.", CancellationToken.None);

            TelegramToken = "";
            TelegramStatus = $"Готово! Привет, {chat.Name} — бот прислал вам сообщение.";
            OnPropertyChanged(nameof(TelegramConnectedText));
            _logger.LogInformation("Telegram-бот @{Bot} подключён", username);
        }
        catch (OperationCanceledException)
        {
            TelegramStatus = "Подключение отменено.";
        }
        catch (Exception ex) when (ex is TelegramException or HttpRequestException)
        {
            TelegramStatus = $"Telegram ответил ошибкой: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanConnect() => !IsConnecting;

    [RelayCommand]
    private void CancelConnect() => _connectCancellation?.Cancel();

    [RelayCommand]
    private void OpenBot()
    {
        if (BotLink is not null)
            SystemShell.Open(BotLink);
    }

    [RelayCommand]
    private async Task SendTestAsync()
    {
        var sent = await _telegram.TrySendAsync("🔔 Тестовое сообщение от AvitoMonitor — всё работает.", CancellationToken.None);
        TelegramStatus = sent
            ? "Тестовое сообщение отправлено."
            : $"Не отправилось: {_telegram.LastError ?? "Telegram не подключён"}";
    }

    [RelayCommand]
    private void DisconnectTelegram()
    {
        _settings.TelegramBotToken = null;
        _settings.TelegramChatId = null;
        _store.Save(_settings);
        TelegramStatus = "Telegram отключён.";
        OnPropertyChanged(nameof(TelegramConnectedText));
        _logger.LogInformation("Telegram отключён");
    }

    private void LoadFromSettings()
    {
        DelaySeconds = _settings.DelayBetweenPagesSeconds;
        WatchIntervalMinutes = _settings.WatchIntervalMinutes;
        CheapPercent = _settings.SuspiciouslyCheapPercent;
        OpenNewListingsForDetails = _settings.OpenNewListingsForDetails;
        HideReplicasAndOtherModels = _settings.HideReplicasAndOtherModels;
        SelectedBrowser = Browsers.FirstOrDefault(b => b.Value == _settings.BrowserChannel) ?? Browsers[0];
        Headless = _settings.Headless;
    }
}
