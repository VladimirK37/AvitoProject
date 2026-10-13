using System.Diagnostics;
using AvitoMonitor.Analysis;
using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using AvitoMonitor.Domain;
using AvitoMonitor.Export;
using AvitoMonitor.Storage;
using AvitoMonitor.Telegram;
using AvitoMonitor.Workflows;
using Spectre.Console;

namespace AvitoMonitor.ConsoleUi;

/// <summary>
/// Консольный интерфейс: меню, вопросы пользователю и вывод результатов.
/// Вся «настоящая» работа — в Workflows, Analysis, Avito; здесь только диалог с человеком.
/// </summary>
public sealed class ConsoleApp(
    AppPaths paths,
    AppSettings settings,
    SettingsStore settingsStore,
    SearchRepository repository,
    AvitoBrowser browser,
    TelegramApi telegramApi,
    TelegramNotifier telegram,
    ScanWorkflow scanWorkflow,
    WatchWorkflow watchWorkflow,
    ReportService reports) : IWatchObserver
{
    // ===================================================================
    // Главное меню
    // ===================================================================

    public async Task RunMenuAsync()
    {
        AnsiConsole.Write(new FigletText("AvitoMonitor").Color(Color.Green));
        AnsiConsole.MarkupLine("Слежу за Авито, считаю рыночную цену и присылаю выгодные объявления в Telegram.");
        AnsiConsole.MarkupLine($"[grey]Данные программы: {Markup.Escape(paths.Root)}[/]");

        while (true)
        {
            var items = new (string Text, Func<Task> Action)[]
            {
                ("Новый поиск по ссылке", () => NewSearchAsync(multi: false)),
                ("Мультипоиск — несколько ссылок в одном поиске (например, iPhone 15, 16 и 17)", () => NewSearchAsync(multi: true)),
                ("Мои поиски", MySearchesAsync),
                (telegram.IsEnabled ? "Настройки (Telegram подключён)" : "Настройки и подключение Telegram", SettingsAsync),
            };

            var index = Menu.Choose("Главное меню", items.Select(item => item.Text).Append("Выход").ToList());
            if (index == items.Length)
                return;

            await SafeRunAsync(items[index].Action);
        }
    }

    /// <summary>Запуск без меню: AvitoMonitor --watch-all (например, на сервере).</summary>
    public async Task WatchAllWithoutMenuAsync()
    {
        var searches = repository.GetAll();
        if (searches.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Нет сохранённых поисков. Запустите программу без параметров и создайте поиск через меню.[/]");
            return;
        }

        await SafeRunAsync(() => RunWatchAsync(searches));
    }

    // ===================================================================
    // Новый поиск
    // ===================================================================

    private async Task NewSearchAsync(bool multi)
    {
        var sources = multi ? AskSeveralSources() : [AskSingleSource()];
        if (sources.Count == 0)
            return;

        var firstUrl = new Uri(sources[0].Url);

        // Открываем первую страницу: проверяем ссылку и показываем, сколько там объявлений
        AnsiConsole.MarkupLine("Проверяю ссылку — открываю страницу Авито в браузере...");
        var firstPage = await browser.LoadSearchPageAsync(firstUrl, CancellationToken.None);
        if (firstPage.Items.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]На странице не нашлось объявлений.[/] Возможные причины: по фильтру ничего нет, " +
                                   "ссылка не на выдачу или Авито поменял вёрстку.");
            if (browser.LastDebugFile is not null)
                AnsiConsole.MarkupLine($"[grey]HTML страницы сохранён для разбора: {Markup.Escape(browser.LastDebugFile)}[/]");
            if (!Ask.YesNo("Всё равно сохранить поиск?", defaultValue: false))
                return;
        }
        else
        {
            var total = firstPage.TotalCount is { } count ? $"{count}" : "неизвестно сколько";
            AnsiConsole.MarkupLine($"[green]Ссылка работает.[/] Объявлений по поиску: [bold]{total}[/], на первой странице: {firstPage.Items.Count}.");
        }

        var category = AskCategory(AvitoUrl.DetectCategory(firstUrl));
        var name = Ask.Text("Название поиска:",
            defaultValue: multi ? string.Join(" + ", sources.Select(s => s.Label)) : sources[0].Label);

        var search = new SavedSearch
        {
            Id = SearchRepository.NewId(),
            Name = name,
            Category = category,
            Sources = sources,
            CreatedAt = DateTime.Now,
        };
        repository.Save(search);
        AnsiConsole.MarkupLine($"Поиск «{Markup.Escape(search.Name)}» сохранён — он есть в «Мои поиски».");

        var totalHint = firstPage.TotalCount is { } t ? $" (≈{t})" : "";
        var choice = Menu.Choose("Что делаем?",
        [
            $"Сканировать все текущие объявления{totalHint} — таблица и рыночные цены",
            "Следить за новыми объявлениями — уведомления в Telegram",
            "Сначала сканировать, потом следить (рекомендуется)",
            "Пока ничего",
        ]);

        switch (choice)
        {
            case 0:
                await RunScanAsync(search, restart: true);
                break;
            case 1:
                await RunWatchAsync([search]);
                break;
            case 2:
                if (await RunScanAsync(search, restart: true))
                    await RunWatchAsync([search]);
                break;
        }
    }

    private static SearchSource AskSingleSource()
    {
        AnsiConsole.MarkupLine("Откройте Авито, настройте поиск и фильтры (город, цена, модель...), скопируйте ссылку из адресной строки.");
        var url = AskSearchUrl("Ссылка на поиск:", allowEmpty: false)!;
        return new SearchSource { Url = url.ToString(), Label = AvitoUrl.SuggestName(url) };
    }

    private static List<SearchSource> AskSeveralSources()
    {
        AnsiConsole.MarkupLine("Добавляйте ссылки по одной — например, отдельно на iPhone 15, iPhone 16 и iPhone 17.");
        AnsiConsole.MarkupLine("Цены будут сравниваться внутри каждой ссылки отдельно. [grey]Пустая строка — закончить.[/]");

        var sources = new List<SearchSource>();
        while (true)
        {
            var url = AskSearchUrl($"Ссылка №{sources.Count + 1}:", allowEmpty: true);
            if (url is null)
            {
                if (sources.Count > 0)
                    break;

                AnsiConsole.MarkupLine("[yellow]Нужна хотя бы одна ссылка.[/]");
                continue;
            }

            var label = Ask.Text("Короткое название для неё:", defaultValue: AvitoUrl.SuggestName(url));
            sources.Add(new SearchSource { Url = url.ToString(), Label = label });
        }

        return sources;
    }

    private static Uri? AskSearchUrl(string question, bool allowEmpty)
    {
        var text = Ask.Text(question, allowEmpty: allowEmpty,
            validate: input => AvitoUrl.TryParseSearchUrl(input, out _, out var error) ? null : error);

        return AvitoUrl.TryParseSearchUrl(text, out var url, out _) ? url : null;
    }

    private static SearchCategory AskCategory(SearchCategory detected)
    {
        var all = Enum.GetValues<SearchCategory>();
        var options = all.Select(c => c == detected ? $"{c.ToRussian()} — определено по ссылке" : c.ToRussian()).ToList();
        AnsiConsole.MarkupLine("[grey]От категории зависит группировка: товары — по объёму памяти, недвижимость — по комнатам (цена за м²), авто — по году.[/]");
        return all[Menu.Choose("Категория поиска", options)];
    }

    // ===================================================================
    // Мои поиски
    // ===================================================================

    private async Task MySearchesAsync()
    {
        while (true)
        {
            var searches = repository.GetAll();
            if (searches.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Сохранённых поисков пока нет. Начните с «Новый поиск по ссылке».[/]");
                return;
            }

            var table = new Table().Border(TableBorder.Rounded)
                .AddColumns("№", "Название", "Категория", "Ссылок", "Собрано", "Сканирование", "Последняя проверка");
            for (var i = 0; i < searches.Count; i++)
            {
                var s = searches[i];
                var collected = repository.LoadListings(s.Id).Count;
                var scanState = s.Sources.All(src => src.ScanCompleted) ? "завершено" : s.LastScanAt is null ? "не было" : "не до конца";
                table.AddRow(
                    $"{i + 1}",
                    Markup.Escape(s.Name),
                    Markup.Escape(s.Category.ToRussian()),
                    $"{s.Sources.Count}",
                    $"{collected}",
                    scanState,
                    s.LastWatchAt?.ToString("dd.MM HH:mm") ?? "—");
            }

            AnsiConsole.Write(table);

            var options = searches.Select(s => s.Name).Append("Следить за ВСЕМИ поисками сразу").Append("Назад").ToList();
            var index = Menu.Choose("Выберите поиск", options);
            if (index == searches.Count + 1)
                return;

            if (index == searches.Count)
                await SafeRunAsync(() => RunWatchAsync(searches));
            else
                await SearchActionsAsync(searches[index]);
        }
    }

    private async Task SearchActionsAsync(SavedSearch search)
    {
        while (true)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[green]{Markup.Escape(search.Name)}[/]").LeftJustified());
            foreach (var source in search.Sources)
            {
                var progress = source.ScanCompleted ? "собрано полностью" : $"следующая страница для сбора: {source.NextPageToScan}";
                AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(source.Label)}: {progress}[/]");
            }

            var items = new (string Text, Func<Task> Action)[]
            {
                ("Следить за новыми объявлениями", () => RunWatchAsync([search])),
                ("Дособрать (продолжить сканирование с места остановки)", () => RunScanAsync(search, restart: false)),
                ("Пересканировать всё с начала", () => RunScanAsync(search, restart: true)),
                ("Показать сводку: рыночные цены по группам", () => { ShowSummary(search); return Task.CompletedTask; }),
                ("Открыть таблицу Excel", () => { OpenReport(search); return Task.CompletedTask; }),
                ("Переименовать", () => { Rename(search); return Task.CompletedTask; }),
            };

            var index = Menu.Choose("Что сделать?", items.Select(i => i.Text).Append("Удалить поиск").Append("Назад").ToList());
            if (index == items.Length + 1)
                return;

            if (index == items.Length)
            {
                if (Ask.YesNo($"Удалить «{search.Name}» вместе с собранными объявлениями?", defaultValue: false))
                {
                    repository.Delete(search);
                    AnsiConsole.MarkupLine("Поиск удалён.");
                    return;
                }

                continue;
            }

            await SafeRunAsync(items[index].Action);
        }
    }

    private void Rename(SavedSearch search)
    {
        search.Name = Ask.Text("Новое название:", defaultValue: search.Name);
        repository.Save(search);
    }

    // ===================================================================
    // Сканирование, слежение, отчёты
    // ===================================================================

    /// <returns>true — сканирование дошло до конца (не остановлено).</returns>
    private async Task<bool> RunScanAsync(SavedSearch search, bool restart)
    {
        AnsiConsole.MarkupLine($"Сканирую «{Markup.Escape(search.Name)}». Между страницами пауза {settings.DelayBetweenPagesSeconds} сек, чтобы не нагружать Авито.");
        AnsiConsole.MarkupLine("[grey]Остановить: Ctrl+C. Собранное сохранится, потом можно «Дособрать».[/]");

        var completed = false;
        using (var stop = new CtrlCScope())
        {
            try
            {
                await scanWorkflow.RunAsync(search, restart, progress =>
                {
                    var total = progress.TotalOnAvito is { } t ? $" из ~{t}" : "";
                    AnsiConsole.MarkupLine(
                        $"  [{(progress.ItemsOnPage == 0 ? "yellow" : "green")}]{Markup.Escape(progress.SourceLabel)}[/] · страница {progress.Page}: " +
                        $"{progress.ItemsOnPage} объявл., новых {progress.Added}, собрано всего {progress.TotalCollected}{total}");
                }, stop.Token);
                completed = true;
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Сканирование остановлено. Продолжить можно через «Мои поиски → Дособрать».[/]");
            }
        }

        ShowSummary(search);
        var path = reports.BuildExcel(search);
        AnsiConsole.MarkupLine($"Таблица Excel: [link]{Markup.Escape(path)}[/]");
        if (Ask.YesNo("Открыть таблицу?", defaultValue: false))
            OpenFile(path);

        return completed;
    }

    private async Task RunWatchAsync(IReadOnlyList<SavedSearch> searches)
    {
        if (!telegram.IsEnabled)
            AnsiConsole.MarkupLine("[yellow]Telegram не подключён — новые объявления будут видны только здесь. Подключить: «Настройки».[/]");

        AnsiConsole.MarkupLine($"Слежу за: [bold]{Markup.Escape(string.Join(", ", searches.Select(s => s.Name)))}[/]. " +
                               $"Проверка каждые {settings.WatchIntervalMinutes} мин.");
        AnsiConsole.MarkupLine("[grey]Остановить слежение и вернуться в меню: Ctrl+C.[/]");

        using var stop = new CtrlCScope();
        try
        {
            await watchWorkflow.RunAsync(searches, this, stop.Token);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Слежение остановлено.[/]");
        }
    }

    private void ShowSummary(SavedSearch search)
    {
        var (analysis, rows) = reports.Analyze(search);
        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Объявлений ещё не собрано.[/]");
            return;
        }

        var unit = analysis.Market.PerSquareMeter ? " (за м²)" : "";
        var table = new Table().Border(TableBorder.Rounded).Title($"Рыночные цены: {Markup.Escape(search.Name)}")
            .AddColumns("Группа", "Объявлений", $"Медиана{unit}", "Минимум", "Максимум");
        foreach (var group in GroupKeys.OrderByGroup(analysis.Market.Groups.Values, g => g.Key))
        {
            table.AddRow(
                Markup.Escape(group.Key),
                $"{group.Count}",
                $"[bold]{Markup.Escape(TextUtils.Money(group.Median))}[/]",
                Markup.Escape(TextUtils.Money(group.Min)),
                Markup.Escape(TextUtils.Money(group.Max)));
        }

        AnsiConsole.Write(table);

        var sellers = string.Join(", ", rows.GroupBy(r => r.Seller.Title).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}: {g.Count()}"));
        AnsiConsole.MarkupLine($"Продавцы: {Markup.Escape(sellers)}");

        // Самые выгодные без признаков обмана
        var bestDeals = rows
            .Where(r => r.DeviationPercent < -10 && r.Flags.All(f => f.Level != FlagLevel.Danger))
            .OrderBy(r => r.DeviationPercent)
            .Take(5)
            .ToList();
        if (bestDeals.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold]Самые выгодные (без признаков обмана):[/]");
            foreach (var deal in bestDeals)
                PrintAssessment(deal, header: "ВЫГОДНО");
        }
    }

    private void OpenReport(SavedSearch search)
    {
        var path = reports.BuildExcel(search);
        AnsiConsole.MarkupLine($"Таблица: [link]{Markup.Escape(path)}[/]");
        OpenFile(path);
    }

    private static void OpenFile(string path)
    {
        try
        {
            // Откроется программой, которая в Windows назначена для .xlsx (Excel, LibreOffice...)
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AnsiConsole.MarkupLine("[yellow]Не нашлось программы для открытия .xlsx. Откройте файл вручную " +
                                   "(например, загрузите в Google Таблицы).[/]");
        }
    }

    // ===================================================================
    // IWatchObserver — что показывать во время слежения
    // ===================================================================

    public void OnInfo(string message) =>
        AnsiConsole.MarkupLine($"[grey]{DateTime.Now:HH:mm:ss}[/] {Markup.Escape(message)}");

    public void OnNewListing(SavedSearch search, ListingAssessment assessment) =>
        PrintAssessment(assessment, header: "НОВОЕ");

    public void OnError(SavedSearch search, Exception exception)
    {
        ErrorLog.Write(exception, $"Слежение за «{search.Name}»");
        AnsiConsole.MarkupLine($"[red]{DateTime.Now:HH:mm:ss} «{Markup.Escape(search.Name)}»: {Markup.Escape(exception.Message)}[/]");
    }

    private static void PrintAssessment(ListingAssessment a, string header)
    {
        var listing = a.Listing;
        var price = listing.Price is { } p ? TextUtils.Money(p) : "цена не указана";
        var market = a.Reference is { } reference && a.DeviationPercent is { } deviation
            ? $" ({TextUtils.Percent(deviation)} к медиане {TextUtils.Metric(reference.Median, a.PerSquareMeter)})"
            : "";

        AnsiConsole.MarkupLine($"[green bold]{header}[/] [bold]{Markup.Escape(listing.Title)}[/] — {Markup.Escape(price + market)}");
        AnsiConsole.MarkupLine($"   Группа: {Markup.Escape(a.GroupKey)} · Продавец: {Markup.Escape(a.Seller.Title)}");
        foreach (var flag in a.Flags)
        {
            var color = flag.Level switch { FlagLevel.Danger => "red", FlagLevel.Warning => "yellow", _ => "grey" };
            AnsiConsole.MarkupLine($"   [{color}]• {Markup.Escape(flag.Text)}[/]");
        }

        AnsiConsole.MarkupLine($"   [blue]{Markup.Escape(listing.Url)}[/]");
    }

    // ===================================================================
    // Настройки
    // ===================================================================

    private async Task SettingsAsync()
    {
        while (true)
        {
            PrintSettings();

            var items = new (string Text, Func<Task> Action)[]
            {
                ("Подключить Telegram-бота", ConnectTelegramAsync),
                ("Отправить тестовое сообщение в Telegram", SendTelegramTestAsync),
                ("Отключить Telegram", () => Change(() => { settings.TelegramBotToken = null; settings.TelegramChatId = null; })),
                ("Пауза между страницами Авито", () => Change(() => settings.DelayBetweenPagesSeconds = Ask.Number("Пауза, секунд (3–120):", 3, 120, settings.DelayBetweenPagesSeconds))),
                ("Как часто проверять новые объявления", () => Change(() => settings.WatchIntervalMinutes = Ask.Number("Интервал, минут (2–240):", 2, 240, settings.WatchIntervalMinutes))),
                ("Порог «подозрительно дёшево»", () => Change(() => settings.SuspiciouslyCheapPercent = Ask.Number("На сколько % ниже медианы (10–90):", 10, 90, settings.SuspiciouslyCheapPercent))),
                ("Открывать новые объявления для подробной проверки: вкл/выкл", () => Change(() => settings.OpenNewListingsForDetails = !settings.OpenNewListingsForDetails)),
                ("Выбрать браузер", ChooseBrowserAsync),
                ("Скрытый режим браузера: вкл/выкл", async () => { settings.Headless = !settings.Headless; SaveSettings(); await browser.CloseAsync(); }),
                ("Открыть папку с данными", () => { OpenFile(paths.Root); return Task.CompletedTask; }),
            };

            var index = Menu.Choose("Настройки", items.Select(i => i.Text).Append("Назад").ToList());
            if (index == items.Length)
                return;

            await SafeRunAsync(items[index].Action);
        }
    }

    private void PrintSettings()
    {
        var table = new Table().Border(TableBorder.Rounded).AddColumns("Настройка", "Значение");
        table.AddRow("Telegram", settings.TelegramConfigured ? "[green]подключён[/]" : "[yellow]не подключён[/]");
        table.AddRow("Пауза между страницами", $"{settings.DelayBetweenPagesSeconds} сек");
        table.AddRow("Проверка новых объявлений", $"каждые {settings.WatchIntervalMinutes} мин");
        table.AddRow("«Подозрительно дёшево»", $"на {settings.SuspiciouslyCheapPercent}% ниже медианы и больше");
        table.AddRow("Подробная проверка новых", settings.OpenNewListingsForDetails ? "вкл" : "выкл");
        table.AddRow("Браузер", Markup.Escape(settings.BrowserChannel));
        table.AddRow("Скрытый режим браузера", settings.Headless ? "вкл" : "выкл");
        table.AddRow("Папка с данными", Markup.Escape(paths.Root));
        AnsiConsole.Write(table);
    }

    private Task Change(Action change)
    {
        change();
        SaveSettings();
        return Task.CompletedTask;
    }

    private void SaveSettings()
    {
        settingsStore.Save(settings);
        AnsiConsole.MarkupLine("[green]Сохранено.[/]");
    }

    private async Task ChooseBrowserAsync()
    {
        string[] channels = ["msedge", "chrome", "chromium"];
        var index = Menu.Choose("Браузер",
        [
            "Microsoft Edge (есть в любой Windows 10/11) — рекомендуется",
            "Google Chrome (должен быть установлен)",
            "Chromium от Playwright (нужно скачать, см. docs/troubleshooting.md)",
        ]);
        settings.BrowserChannel = channels[index];
        SaveSettings();
        await browser.CloseAsync(); // следующий запрос откроет уже выбранный браузер
    }

    private async Task ConnectTelegramAsync()
    {
        AnsiConsole.MarkupLine("Сначала создайте бота в Telegram (подробно — в docs/telegram-bot.md):");
        AnsiConsole.MarkupLine("  1. Найдите в Telegram [bold]@BotFather[/] и отправьте ему [bold]/newbot[/].");
        AnsiConsole.MarkupLine("  2. Придумайте имя бота и логин (логин должен заканчиваться на [bold]bot[/]).");
        AnsiConsole.MarkupLine("  3. BotFather пришлёт токен вида [grey]1234567890:AAH...[/] — скопируйте его сюда.");

        var token = Ask.Secret("Токен бота (Enter без ввода — отмена):");
        if (token.Length == 0)
            return;

        string username;
        try
        {
            username = await telegramApi.GetBotUsernameAsync(token, CancellationToken.None);
        }
        catch (Exception ex) when (ex is TelegramException or HttpRequestException)
        {
            AnsiConsole.MarkupLine($"[red]Telegram не принял токен: {Markup.Escape(ex.Message)}[/]");
            return;
        }

        AnsiConsole.MarkupLine($"Бот [bold]@{Markup.Escape(username)}[/] найден.");
        AnsiConsole.MarkupLine($"Теперь откройте [blue]https://t.me/{Markup.Escape(username)}[/] и нажмите «Запустить» (или отправьте /start).");
        AnsiConsole.MarkupLine("[grey]Жду до 3 минут... (Ctrl+C — отменить)[/]");

        TelegramChat? chat;
        using (var stop = new CtrlCScope())
            chat = await telegramApi.WaitForStartCommandAsync(token, TimeSpan.FromMinutes(3), stop.Token);

        if (chat is null)
        {
            AnsiConsole.MarkupLine("[red]Не дождался /start. Попробуйте ещё раз.[/]");
            return;
        }

        settings.TelegramBotToken = token;
        settings.TelegramChatId = chat.Id;
        SaveSettings();

        await telegramApi.SendMessageAsync(token, chat.Id,
            "✅ <b>AvitoMonitor подключён.</b>\nСюда будут приходить новые объявления с Авито.", CancellationToken.None);
        AnsiConsole.MarkupLine($"[green]Готово! Привет, {Markup.Escape(chat.Name)} — проверьте Telegram, бот прислал сообщение.[/]");
    }

    private async Task SendTelegramTestAsync()
    {
        if (!telegram.IsEnabled)
        {
            AnsiConsole.MarkupLine("[yellow]Сначала подключите Telegram-бота.[/]");
            return;
        }

        var sent = await telegram.TrySendAsync("🔔 Тестовое сообщение от AvitoMonitor — всё работает.", CancellationToken.None);
        AnsiConsole.MarkupLine(sent ? "[green]Отправлено.[/]" : $"[red]Не отправилось: {Markup.Escape(telegram.LastError ?? "")}[/]");
    }

    // ===================================================================

    /// <summary>Выполняет пункт меню; любая ошибка показывается коротко, подробности — в errors.log.</summary>
    private static async Task SafeRunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Остановлено.[/]");
        }
        catch (Exception ex) when (ex is not EndOfStreamException) // конец ввода — выходим из программы
        {
            ErrorLog.Write(ex, "Ошибка в меню");
            AnsiConsole.MarkupLine($"[red]Ошибка: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine("[grey]Подробности записаны в errors.log в папке данных.[/]");
        }
    }
}
