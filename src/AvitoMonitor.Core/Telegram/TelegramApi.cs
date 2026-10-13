using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AvitoMonitor.Telegram;

public sealed class TelegramException(string message) : Exception(message);

/// <summary>Чат, в который бот будет писать.</summary>
public sealed record TelegramChat(long Id, string Name);

/// <summary>
/// Минимальный клиент Telegram Bot API на обычном HttpClient — без сторонних библиотек.
/// Нужны всего три метода: getMe (проверить токен), getUpdates (поймать /start) и sendMessage.
/// Документация API: https://core.telegram.org/bots/api
/// </summary>
public sealed class TelegramApi(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Проверяет токен и возвращает логин бота (без @).</summary>
    public async Task<string> GetBotUsernameAsync(string token, CancellationToken ct)
    {
        var me = await CallAsync(token, "getMe", new Dictionary<string, object?>(), ct);
        return me.GetProperty("username").GetString() ?? "";
    }

    /// <summary>
    /// Ждёт, пока вы откроете бота и нажмёте «Запустить» (/start), и возвращает ваш чат.
    /// Так программа узнаёт, куда слать уведомления, — chat_id вводить руками не нужно.
    /// </summary>
    public async Task<TelegramChat?> WaitForStartCommandAsync(string token, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        long? offset = null;

        while (DateTime.UtcNow < deadline)
        {
            // «Длинный опрос»: Telegram держит запрос до 20 секунд, пока не придёт сообщение
            var updates = await CallAsync(token, "getUpdates", new Dictionary<string, object?>
            {
                ["offset"] = offset,
                ["timeout"] = 20,
                ["allowed_updates"] = new[] { "message" },
            }, ct);

            foreach (var update in updates.EnumerateArray())
            {
                offset = update.GetProperty("update_id").GetInt64() + 1;

                if (!update.TryGetProperty("message", out var message)
                    || !message.TryGetProperty("text", out var text)
                    || text.GetString()?.StartsWith("/start", StringComparison.Ordinal) != true)
                {
                    continue;
                }

                var chat = message.GetProperty("chat");
                var name = chat.TryGetProperty("first_name", out var firstName) ? firstName.GetString()
                    : chat.TryGetProperty("title", out var title) ? title.GetString()
                    : null;

                // Подтверждаем получение, чтобы Telegram не прислал это сообщение повторно
                await CallAsync(token, "getUpdates", new Dictionary<string, object?> { ["offset"] = offset, ["timeout"] = 0 }, ct);

                return new TelegramChat(chat.GetProperty("id").GetInt64(), name ?? "");
            }
        }

        return null;
    }

    /// <summary>Отправляет сообщение. Текст в формате HTML: &lt;b&gt;жирный&lt;/b&gt;, &lt;a href=...&gt;ссылка&lt;/a&gt;.</summary>
    public async Task SendMessageAsync(string token, long chatId, string html, CancellationToken ct)
    {
        await CallAsync(token, "sendMessage", new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
            ["link_preview_options"] = new Dictionary<string, object?> { ["is_disabled"] = true },
        }, ct);
    }

    private async Task<JsonElement> CallAsync(string token, string method, Dictionary<string, object?> payload, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var response = await http.PostAsJsonAsync($"https://api.telegram.org/bot{token}/{method}", payload, JsonOptions, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            JsonElement root;
            try
            {
                root = JsonDocument.Parse(body).RootElement;
            }
            catch (JsonException)
            {
                throw new TelegramException($"Telegram ответил HTTP {(int)response.StatusCode} без JSON");
            }

            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                return root.GetProperty("result");

            // 429 — слишком часто пишем, Telegram сам говорит, сколько подождать
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3
                && root.TryGetProperty("parameters", out var parameters)
                && parameters.TryGetProperty("retry_after", out var retryAfter))
            {
                await Task.Delay(TimeSpan.FromSeconds(retryAfter.GetInt32() + 1), ct);
                continue;
            }

            var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
            throw new TelegramException(description ?? $"Telegram ответил HTTP {(int)response.StatusCode}");
        }
    }
}
