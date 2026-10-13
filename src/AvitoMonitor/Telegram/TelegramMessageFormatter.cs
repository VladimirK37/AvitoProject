using System.Text;
using AvitoMonitor.Analysis;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Telegram;

/// <summary>Как выглядит сообщение о новом объявлении в Telegram.</summary>
public static class TelegramMessageFormatter
{
    /// <summary>
    /// Пример:
    /// <code>
    /// 🆕 iPhone 15, 128 ГБ
    /// 💰 45 000 ₽ · медиана 60 000 ₽ · −25%
    /// 📦 Группа: 128 ГБ (по 120 объявлениям)
    /// 👤 Иван — Частник
    /// ⚠️ Подозрительно дёшево (−45% к рынку) — возможен обман
    /// 📍 Санкт-Петербург, м. Невский проспект · 🕒 5 минут назад
    /// ✉️ Черновик: Здравствуйте! ...
    /// 🔗 Открыть объявление
    /// </code>
    /// </summary>
    public static string NewListing(SavedSearch search, ListingAssessment assessment)
    {
        var listing = assessment.Listing;
        var text = new StringBuilder();

        text.AppendLine($"🆕 <b>{Escape(listing.Title)}</b>");
        text.AppendLine($"🔎 Поиск: {Escape(search.Name)}");

        var price = listing.Price is { } p ? TextUtils.Money(p) : "цена не указана";
        var priceLine = $"💰 <b>{Escape(price)}</b>";
        if (assessment.PerSquareMeter && assessment.ComparedValue is { } perMeter)
            priceLine += $" ({Escape(TextUtils.Metric(perMeter, true))})";
        if (assessment.Reference is { } reference && assessment.DeviationPercent is { } deviation)
            priceLine += $" · медиана {Escape(TextUtils.Metric(reference.Median, assessment.PerSquareMeter))} · <b>{TextUtils.Percent(deviation)}</b>";
        text.AppendLine(priceLine);

        if (assessment.Reference is { } stats)
        {
            var scope = assessment.ReferenceIsWholeSearch ? "сравнили со всей выдачей" : $"по {stats.Count} объявлениям";
            text.AppendLine($"📦 Группа: {Escape(assessment.GroupKey)} ({scope})");
        }

        var seller = string.IsNullOrWhiteSpace(listing.SellerName) ? "" : Escape(listing.SellerName) + " — ";
        text.AppendLine($"👤 {seller}{Escape(assessment.Seller.Title)}");
        if (assessment.Seller.IsBusiness && assessment.Seller.Reasons.Count > 0)
            text.AppendLine($"     <i>{Escape(string.Join("; ", assessment.Seller.Reasons))}</i>");

        foreach (var flag in assessment.Flags)
            text.AppendLine($"{FlagIcon(flag.Level)} {Escape(flag.Text)}");

        var place = string.Join(" · ", new[] { listing.Address, listing.PublishedText }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (place.Length > 0)
            text.AppendLine($"📍 {Escape(place)}");

        text.AppendLine($"✉️ Черновик: <code>{Escape(assessment.MessageDraft)}</code>");
        text.Append($"🔗 <a href=\"{Escape(listing.Url)}\">Открыть объявление</a>");

        return text.ToString();
    }

    /// <summary>Экранирует текст для HTML-разметки Telegram.</summary>
    public static string Escape(string? text) =>
        (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string FlagIcon(FlagLevel level) => level switch
    {
        FlagLevel.Danger => "🚨",
        FlagLevel.Warning => "⚠️",
        _ => "ℹ️",
    };
}
