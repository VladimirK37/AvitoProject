using AvitoMonitor.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AvitoMonitor.Avito;

/// <summary>Авито требует пройти проверку, а пройти её сейчас нельзя (например, браузер скрыт).</summary>
public sealed class AvitoCheckRequiredException(string message) : Exception(message);

/// <summary>
/// Открывает страницы Авито в настоящем браузере (по умолчанию Microsoft Edge) через Playwright.
///
/// Почему браузер, а не HttpClient: на простые HTTP-запросы Авито сразу отвечает
/// страницей «Доступ ограничен». У браузера программы свой профиль с cookies, а если
/// Авито всё-таки покажет проверку «я не робот» — вы проходите её сами в окне браузера,
/// программа просто ждёт и продолжает. Капчу программа не решает и не обходит.
///
/// Страницы открываются строго по одной (даже если сканируются и отслеживаются
/// несколько поисков сразу), с паузой DelayBetweenPagesSeconds между ними.
/// </summary>
public sealed class AvitoBrowser : IAsyncDisposable
{
    private static readonly TimeSpan CheckPollInterval = TimeSpan.FromSeconds(5);

    private readonly AppSettings _settings;
    private readonly AppPaths _paths;
    private readonly ILogger<AvitoBrowser> _logger;

    // Очередь из одного места: пока одна страница открывается, остальные ждут
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private IPage? _page;
    private DateTime _lastNavigationAt = DateTime.MinValue;

    public AvitoBrowser(AppSettings settings, AppPaths paths, ILogger<AvitoBrowser> logger)
    {
        _settings = settings;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>Авито показал проверку «я не робот» — нужно пройти её вручную в окне браузера.</summary>
    public event Action<string>? CheckRequired;

    /// <summary>Проверку прошли, работа продолжается.</summary>
    public event Action? CheckPassed;

    /// <summary>Файл с HTML последней страницы, на которой не нашлось объявлений (для разбора проблем).</summary>
    public string? LastDebugFile { get; private set; }

    /// <summary>Открывает страницу выдачи и возвращает найденные объявления.</summary>
    public async Task<SearchPage> LoadSearchPageAsync(Uri url, CancellationToken ct)
    {
        var html = await OpenAsync(url, waitForSelector: "[data-marker='item']", ct);
        var page = AvitoPageParser.ParseSearchPage(html);

        if (page.Items.Count == 0)
        {
            SaveDebugHtml("search", html);
            _logger.LogWarning("На странице не нашлось объявлений, HTML сохранён: {File}", LastDebugFile);
        }

        return page;
    }

    /// <summary>Открывает страницу объявления: полное описание и данные продавца.</summary>
    public async Task<ListingDetails> LoadListingDetailsAsync(string url, CancellationToken ct)
    {
        var html = await OpenAsync(new Uri(url), waitForSelector: "[data-marker='item-view/item-description']", ct);
        return AvitoPageParser.ParseListingPage(html);
    }

    /// <summary>Закрывает браузер. При следующем запросе он запустится заново (с новыми настройками).</summary>
    public async Task CloseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await CloseContextAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseContextAsync();
        _playwright?.Dispose();
        _playwright = null;
    }

    private async Task CloseContextAsync()
    {
        if (_context is not null)
        {
            try
            {
                await _context.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // Окно уже закрыли вручную — ничего страшного
            }
        }

        _context = null;
        _page = null;
    }

    private async Task<string> OpenAsync(Uri url, string waitForSelector, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var page = await GetPageAsync();

            // Обычно хватает одной попытки. Вторая и третья — если после проверки Авито снова показал заглушку.
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await WaitPoliteDelayAsync(ct);
                _logger.LogDebug("Открываю {Url}", url);
                await page.GotoAsync(url.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
                _lastNavigationAt = DateTime.UtcNow;

                if (!await IsCheckPageAsync(page))
                    break;

                await WaitUntilUserPassesCheckAsync(page, ct);
            }

            try
            {
                // Объявления обычно уже в HTML, но дадим странице пару секунд догрузиться
                await page.WaitForSelectorAsync(waitForSelector, new PageWaitForSelectorOptions { Timeout = 10_000 });
            }
            catch (PlaywrightException)
            {
                // Нужного элемента нет — разберём то, что есть
            }

            return await page.ContentAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WaitUntilUserPassesCheckAsync(IPage page, CancellationToken ct)
    {
        if (_settings.Headless)
        {
            throw new AvitoCheckRequiredException(
                "Авито просит пройти проверку «я не робот», а браузер запущен в скрытом режиме. " +
                "Выключите «Скрытый режим браузера» в настройках, чтобы пройти проверку вручную.");
        }

        const string message = "Авито просит подтвердить, что вы не робот. Переключитесь в окно браузера программы " +
                               "и пройдите проверку вручную — программа подождёт и продолжит сама.";
        _logger.LogWarning(message);
        await page.BringToFrontAsync();
        CheckRequired?.Invoke(message);

        while (await IsCheckPageAsync(page))
            await Task.Delay(CheckPollInterval, ct);

        _logger.LogInformation("Проверка Авито пройдена, продолжаю");
        CheckPassed?.Invoke();
    }

    private static async Task<bool> IsCheckPageAsync(IPage page)
    {
        try
        {
            var title = await page.TitleAsync();
            return AvitoPageParser.IsCheckPage(title, await page.ContentAsync());
        }
        catch (PlaywrightException)
        {
            // Страница как раз перезагружается (например, после проверки) — спросим ещё раз позже
            return true;
        }
    }

    private async Task WaitPoliteDelayAsync(CancellationToken ct)
    {
        // Не открываем страницы чаще, чем раз в DelayBetweenPagesSeconds секунд
        var pause = TimeSpan.FromSeconds(_settings.DelayBetweenPagesSeconds);
        var wait = _lastNavigationAt + pause - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct);
    }

    private async Task<IPage> GetPageAsync()
    {
        if (_page is { IsClosed: false })
            return _page;

        if (_context is null)
        {
            _playwright ??= await Playwright.CreateAsync();
            _logger.LogInformation("Запускаю браузер «{Channel}»{Mode}", _settings.BrowserChannel, _settings.Headless ? " в скрытом режиме" : "");

            try
            {
                _context = await _playwright.Chromium.LaunchPersistentContextAsync(_paths.BrowserProfileDir,
                    new BrowserTypeLaunchPersistentContextOptions
                    {
                        // null — встроенный Chromium от Playwright, иначе установленный Edge/Chrome
                        Channel = _settings.BrowserChannel is "chromium" or "" ? null : _settings.BrowserChannel,
                        Headless = _settings.Headless,
                        Locale = "ru-RU",
                        ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
                    });
            }
            catch (PlaywrightException ex)
            {
                throw new InvalidOperationException(
                    $"Не удалось запустить браузер «{_settings.BrowserChannel}». Проверьте, что он установлен, " +
                    "или выберите другой в настройках (см. README, раздел «Если что-то не работает»)." +
                    Environment.NewLine + ex.Message, ex);
            }

            // Если пользователь закроет окно браузера, при следующем запросе откроем новое
            var context = _context;
            context.Close += (_, _) =>
            {
                if (ReferenceEquals(_context, context))
                {
                    _context = null;
                    _page = null;
                }
            };
        }

        _page = _context.Pages.FirstOrDefault(p => !p.IsClosed) ?? await _context.NewPageAsync();
        return _page;
    }

    private void SaveDebugHtml(string kind, string html)
    {
        var file = Path.Combine(_paths.DebugDir, $"{kind}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        File.WriteAllText(file, html);
        LastDebugFile = file;
    }
}
