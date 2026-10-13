using System.Text;
using AvitoMonitor.Analysis;
using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using AvitoMonitor.ConsoleUi;
using AvitoMonitor.Export;
using AvitoMonitor.Storage;
using AvitoMonitor.Telegram;
using AvitoMonitor.Workflows;
using Spectre.Console;

// ---------------------------------------------------------------------
// Точка входа. Здесь только «сборка» программы из частей — вся логика в классах.
//
//   dotnet run                 — интерактивное меню
//   dotnet run -- --watch-all  — сразу следить за всеми сохранёнными поисками (для сервера)
// ---------------------------------------------------------------------

Console.OutputEncoding = Encoding.UTF8;
try
{
    Console.InputEncoding = Encoding.UTF8; // чтобы можно было вводить русские названия
}
catch (IOException)
{
    // Ввод перенаправлен (не настоящая консоль) — оставляем как есть
}

var paths = AppPaths.CreateDefault();
ErrorLog.Init(paths.ErrorLogFile);

var settingsStore = new SettingsStore(paths.SettingsFile);
var settings = settingsStore.Load();
var repository = new SearchRepository(paths.SearchesDir);

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var telegramApi = new TelegramApi(http);
var telegram = new TelegramNotifier(telegramApi, settings);

// Если Авито попросит пройти проверку «я не робот» — пишем в консоль и в Telegram
await using var browser = new AvitoBrowser(settings, paths, async message =>
{
    AnsiConsole.MarkupLine($"[yellow bold]{Markup.Escape(message)}[/]");
    await telegram.TrySendAsync("⚠️ " + TelegramMessageFormatter.Escape(message), CancellationToken.None);
});

var analyzer = new SearchAnalyzer(settings);
var scanWorkflow = new ScanWorkflow(browser, repository, settings);
var watchWorkflow = new WatchWorkflow(browser, repository, settings, analyzer, telegram);
var reports = new ReportService(repository, analyzer, new ExcelReportBuilder());

var app = new ConsoleApp(paths, settings, settingsStore, repository, browser, telegramApi, telegram,
    scanWorkflow, watchWorkflow, reports);

try
{
    if (args.Contains("--watch-all"))
        await app.WatchAllWithoutMenuAsync();
    else
        await app.RunMenuAsync();
}
catch (EndOfStreamException)
{
    // Ввод закончился (например, его перенаправили из файла) — просто выходим
}
