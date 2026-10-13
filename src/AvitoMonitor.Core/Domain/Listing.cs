namespace AvitoMonitor.Domain;

/// <summary>
/// Одно объявление с Авито — то, что удалось прочитать со страницы поиска
/// (и, если включено, со страницы самого объявления).
/// </summary>
public sealed class Listing
{
    /// <summary>Номер объявления на Авито (последние цифры в ссылке).</summary>
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Полная ссылка на объявление.</summary>
    public string Url { get; set; } = "";

    /// <summary>Цена в рублях; null — «цена не указана».</summary>
    public decimal? Price { get; set; }

    /// <summary>Описание: на странице поиска — короткий фрагмент, после открытия объявления — полностью.</summary>
    public string? Description { get; set; }

    public string? Address { get; set; }

    /// <summary>Как Авито пишет дату: «2 часа назад», «12 октября 14:30».</summary>
    public string? PublishedText { get; set; }

    public string? SellerName { get; set; }

    /// <summary>
    /// Ссылка на профиль продавца: /brands/i12345678 (обычный профиль — и у частников, и у компаний)
    /// или /brands/smart-butik (фирменная страница магазина).
    /// </summary>
    public string? SellerUrl { get; set; }

    /// <summary>Сколько отзывов у продавца. Сотни отзывов — признак магазина или перекупщика.</summary>
    public int? SellerReviews { get; set; }

    /// <summary>Подпись Авито о продавце со страницы объявления: «Частное лицо», «Компания», «Агентство»…</summary>
    public string? SellerLabel { get; set; }

    /// <summary>Значки Авито на карточке («Цена ниже рыночной», «Надёжный продавец», «Документы проверены»…).</summary>
    public string? Badges { get; set; }

    /// <summary>Из какой ссылки поиска пришло объявление (важно для мультипоиска).</summary>
    public string SourceLabel { get; set; } = "";

    /// <summary>Когда программа впервые увидела объявление.</summary>
    public DateTime FirstSeenAt { get; set; }

    /// <summary>Когда объявление последний раз встречалось в выдаче.</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>Открывали ли страницу объявления для подробной проверки.</summary>
    public bool DetailsLoaded { get; set; }
}
