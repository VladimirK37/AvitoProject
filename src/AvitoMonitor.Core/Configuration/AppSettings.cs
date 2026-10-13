using System.Text.Json.Serialization;

namespace AvitoMonitor.Configuration;

/// <summary>
/// Настройки программы. Хранятся в settings.json в папке данных,
/// меняются через меню «Настройки» или вручную в файле.
/// </summary>
public sealed class AppSettings
{
    // ----- Базы данных (запускаются через docker compose, см. README) -----

    /// <summary>
    /// PostgreSQL: поиски, объявления, история запросов и записи об отчётах.
    /// Порт 5433, чтобы не мешать PostgreSQL, который может быть установлен на компьютере.
    /// </summary>
    public string DatabaseConnectionString { get; set; } =
        "Host=localhost;Port=5433;Database=avito;Username=avito;Password=avito_local";

    /// <summary>Elasticsearch: полнотекстовый поиск по истории запросов. Если недоступен — ищем по PostgreSQL.</summary>
    public string ElasticsearchUrl { get; set; } = "http://localhost:9200";

    /// <summary>Индекс Elasticsearch с историей запросов.</summary>
    public string ElasticsearchIndex { get; set; } = "avito-queries";
    // ----- Telegram -----

    /// <summary>Токен бота от @BotFather (вида 123456789:AA...).</summary>
    public string? TelegramBotToken { get; set; }

    /// <summary>Чат, куда бот шлёт уведомления. Определяется сам, когда вы нажимаете /start у бота.</summary>
    public long? TelegramChatId { get; set; }

    [JsonIgnore]
    public bool TelegramConfigured => !string.IsNullOrWhiteSpace(TelegramBotToken) && TelegramChatId is not null;

    // ----- Браузер -----

    /// <summary>
    /// Какой браузер запускать: "msedge" (есть в любой Windows 10/11), "chrome"
    /// или "chromium" (встроенный браузер Playwright, его нужно скачать отдельно — см. docs).
    /// </summary>
    public string BrowserChannel { get; set; } = "msedge";

    /// <summary>
    /// Скрытый режим: браузер работает без окна. Удобно на сервере,
    /// но тогда вы не сможете пройти проверку «я не робот», если Авито её покажет.
    /// </summary>
    public bool Headless { get; set; }

    // ----- Темп работы (не нагружаем Авито) -----

    /// <summary>Пауза между открытиями страниц Авито, секунд.</summary>
    public int DelayBetweenPagesSeconds { get; set; } = 8;

    /// <summary>Как часто проверять новые объявления в режиме слежения, минут.</summary>
    public int WatchIntervalMinutes { get; set; } = 5;

    /// <summary>Сколько страниц выдачи максимум собирать при сканировании (Авито больше 100 не отдаёт).</summary>
    public int MaxPagesPerScan { get; set; } = 100;

    /// <summary>
    /// Открывать ли каждое новое объявление при слежении, чтобы прочитать полное
    /// описание и данные продавца. Точнее определяет магазины и агентов, но это лишние запросы.
    /// </summary>
    public bool OpenNewListingsForDetails { get; set; } = true;

    /// <summary>
    /// Не присылать в Telegram копии («Оригинал» в кавычках, Luxe, Airoha…) и другие модели
    /// (искали AirPods 4 — попались AirPods Pro). В консоли и Excel они всё равно видны с пометкой.
    /// </summary>
    public bool HideReplicasAndOtherModels { get; set; } = true;

    // ----- Анализ -----

    /// <summary>Насколько процентов ниже медианы цена считается «подозрительно дешёвой».</summary>
    public int SuspiciouslyCheapPercent { get; set; } = 40;

    /// <summary>
    /// Сколько объявлений должно быть в группе, чтобы доверять её медиане.
    /// Если меньше — сравниваем с медианой всей выдачи.
    /// </summary>
    public int MinGroupSizeForMedian { get; set; } = 5;
}
