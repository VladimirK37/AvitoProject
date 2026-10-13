using AvitoMonitor.Configuration;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.Telegram;

/// <summary>
/// Отправляет уведомления в ваш Telegram, если бот подключён.
/// Ошибки отправки не роняют программу: слежение за Авито продолжается, ошибка уходит в журнал.
/// </summary>
public sealed class TelegramNotifier(TelegramApi api, AppSettings settings, ILogger<TelegramNotifier> logger)
{
    // Telegram разрешает примерно одно сообщение в секунду в один чат
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1.1);

    // Уведомления могут прийти из нескольких поисков одновременно — отправляем по одному
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastSentAt = DateTime.MinValue;

    public bool IsEnabled => settings.TelegramConfigured;

    /// <summary>Текст последней ошибки отправки (если была).</summary>
    public string? LastError { get; private set; }

    /// <returns>true — отправлено; false — Telegram не подключён или произошла ошибка (см. LastError).</returns>
    public async Task<bool> TrySendAsync(string html, CancellationToken ct)
    {
        if (!IsEnabled)
            return false;

        await _gate.WaitAsync(ct);
        try
        {
            var wait = _lastSentAt + MinInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);

            await api.SendMessageAsync(settings.TelegramBotToken!, settings.TelegramChatId!.Value, html, ct);
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            logger.LogWarning(ex, "Не удалось отправить сообщение в Telegram: {Message}", ex.Message);
            return false;
        }
        finally
        {
            _lastSentAt = DateTime.UtcNow;
            _gate.Release();
        }
    }
}
