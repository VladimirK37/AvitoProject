using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AvitoMonitor.Avito;
using AvitoMonitor.Configuration;
using Microsoft.Extensions.Logging;

namespace AvitoMonitor.History;

/// <summary>
/// История запросов в Elasticsearch — для быстрого поиска «как в поисковике»:
/// с опечатками («airpds» найдёт «airpods»), по началу слова и с учётом русских окончаний.
///
/// Работаем через обычный HTTP (REST API Elasticsearch), без отдельной библиотеки.
/// Один документ = один уникальный запрос (слова + город + цены), в нём счётчик uses и время last_used_at.
///
/// Если Elasticsearch не запущен, методы возвращают false/null — программа продолжает работать,
/// а история ищется по PostgreSQL. Повторная попытка подключиться — через минуту.
/// </summary>
public sealed class ElasticQueryIndex(HttpClient http, AppSettings settings, ILogger<ElasticQueryIndex> logger)
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryPause = TimeSpan.FromMinutes(1);

    private bool _indexReady;

    /// <summary>Имя индекса из настроек (по умолчанию avito-queries).</summary>
    private string IndexName => settings.ElasticsearchIndex;
    private DateTime _unavailableUntil = DateTime.MinValue;

    /// <summary>null — ещё не обращались; true/false — результат последнего обращения.</summary>
    public bool? IsAvailable { get; private set; }

    public event Action? AvailabilityChanged;

    /// <summary>Записать использование запроса: новый документ или +1 к счётчику существующего.</summary>
    public async Task<bool> RecordUseAsync(SearchRequest request, DateTime usedAt, CancellationToken ct)
    {
        var body = new
        {
            script = new
            {
                source = "ctx._source.uses += 1; ctx._source.last_used_at = params.now",
                lang = "painless",
                @params = new { now = usedAt.ToUniversalTime() },
            },
            upsert = ToDocument(request, uses: 1, usedAt),
        };

        return await SendAsync(HttpMethod.Post, $"{IndexName}/_update/{DocumentId(request)}?retry_on_conflict=3", body, ct) is not null;
    }

    /// <summary>Записать документ целиком (используется при переносе истории из PostgreSQL).</summary>
    public async Task<bool> PutAsync(QueryHistoryEntry entry, CancellationToken ct)
    {
        var request = entry.ToRequest();
        return await SendAsync(HttpMethod.Put, $"{IndexName}/_doc/{DocumentId(request)}",
            ToDocument(request, entry.Uses, entry.LastUsedAt), ct) is not null;
    }

    /// <summary>Поиск по истории. null — Elasticsearch недоступен.</summary>
    public async Task<IReadOnlyList<QueryHistoryEntry>?> SearchAsync(string text, int size, CancellationToken ct)
    {
        text = text.Trim();

        // Пусто — последние запросы. Иначе — нечёткое совпадение слов ИЛИ совпадение по началу фразы.
        object query = text.Length == 0
            ? new { match_all = new { } }
            : new
            {
                @bool = new
                {
                    should = new object[]
                    {
                        new { match = new { text = new { query = text, fuzziness = "AUTO", @operator = "and" } } },
                        new { match_phrase_prefix = new { text = new { query = text } } },
                    },
                    minimum_should_match = 1,
                },
            };

        var body = new
        {
            size,
            query,
            sort = new object[] { new { _score = "desc" }, new { last_used_at = "desc" } },
        };

        var response = await SendAsync(HttpMethod.Post, $"{IndexName}/_search", body, ct);
        if (response is not { } json)
            return null;

        var entries = new List<QueryHistoryEntry>();
        foreach (var hit in json.GetProperty("hits").GetProperty("hits").EnumerateArray())
        {
            var source = hit.GetProperty("_source");
            entries.Add(new QueryHistoryEntry(
                source.GetProperty("text").GetString() ?? "",
                source.TryGetProperty("city", out var city) ? city.GetString() ?? AvitoCities.DefaultSlug : AvitoCities.DefaultSlug,
                IntOrNull(source, "price_min"),
                IntOrNull(source, "price_max"),
                source.TryGetProperty("uses", out var uses) ? uses.GetInt32() : 1,
                source.TryGetProperty("last_used_at", out var date) ? date.GetDateTime().ToLocalTime() : DateTime.MinValue));
        }

        return entries;
    }

    private async Task<JsonElement?> SendAsync(HttpMethod method, string path, object body, CancellationToken ct)
    {
        if (DateTime.UtcNow < _unavailableUntil)
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            if (!_indexReady)
                await EnsureIndexAsync(timeout.Token);

            using var request = new HttpRequestMessage(method, Url(path)) { Content = JsonContent.Create(body) };
            using var response = await http.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            SetAvailable(true);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Elasticsearch ответил HTTP {Status}: {Body}", (int)response.StatusCode, text.Length > 300 ? text[..300] : text);
                return null;
            }

            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            if (IsAvailable != false)
                logger.LogWarning("Elasticsearch недоступен ({Url}): {Message}. История запросов будет искаться по PostgreSQL.",
                    settings.ElasticsearchUrl, ex.Message);

            _unavailableUntil = DateTime.UtcNow + RetryPause;
            _indexReady = false;
            SetAvailable(false);
            return null;
        }
    }

    /// <summary>Создаёт индекс, если его ещё нет. Поле text анализируется по правилам русского языка.</summary>
    private async Task EnsureIndexAsync(CancellationToken ct)
    {
        using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, Url(IndexName)), ct);
        if (head.StatusCode == HttpStatusCode.NotFound)
        {
            var mapping = new
            {
                mappings = new
                {
                    properties = new Dictionary<string, object>
                    {
                        ["text"] = new { type = "text", analyzer = "russian", fields = new { raw = new { type = "keyword" } } },
                        ["city"] = new { type = "keyword" },
                        ["price_min"] = new { type = "integer" },
                        ["price_max"] = new { type = "integer" },
                        ["uses"] = new { type = "integer" },
                        ["last_used_at"] = new { type = "date" },
                    },
                },
            };

            using var put = await http.PutAsJsonAsync(Url(IndexName), mapping, ct);
            // 400 — индекс успели создать параллельно, это нормально
            if (!put.IsSuccessStatusCode && put.StatusCode != HttpStatusCode.BadRequest)
                throw new HttpRequestException($"не удалось создать индекс: HTTP {(int)put.StatusCode}");

            logger.LogInformation("Elasticsearch: создан индекс {Index}", IndexName);
        }
        else if (!head.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)head.StatusCode}");
        }

        _indexReady = true;
    }

    private void SetAvailable(bool available)
    {
        if (IsAvailable == available)
            return;

        IsAvailable = available;
        if (available)
            logger.LogInformation("Elasticsearch подключён: {Url}", settings.ElasticsearchUrl);
        AvailabilityChanged?.Invoke();
    }

    private string Url(string path) => $"{settings.ElasticsearchUrl.TrimEnd('/')}/{path}";

    private static object ToDocument(SearchRequest request, int uses, DateTime lastUsedAt) => new
    {
        text = request.Query.Trim(),
        city = request.City,
        price_min = request.PriceMin,
        price_max = request.PriceMax,
        uses,
        last_used_at = lastUsedAt.ToUniversalTime(),
    };

    /// <summary>Один и тот же запрос (без учёта регистра) — один документ.</summary>
    public static string DocumentId(SearchRequest request)
    {
        var key = $"{request.Query.Trim().ToLowerInvariant()}|{request.City}|{request.PriceMin}|{request.PriceMax}";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private static int? IntOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
