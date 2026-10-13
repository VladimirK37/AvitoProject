using AvitoMonitor.Domain;

namespace AvitoMonitor.Analysis;

/// <summary>
/// Ищет подозрительное в объявлении: слишком низкая цена, «битый / на запчасти»,
/// блокировка iCloud, просьбы о предоплате, копии (см. ReplicaDetector).
/// Списки слов можно дополнять — они в начале класса.
/// </summary>
public static class RedFlagDetector
{
    /// <summary>Фразы-отрицания: их вырезаем из текста до поиска, чтобы «не битый» не считался «битым».</summary>
    private static readonly string[] Negations =
    [
        "не битый", "не битая", "не бита", "небитый", "небитая", "не разбит", "без дефект", "без трещин",
        "без сколов", "без предоплат", "не заблокирован", "не залочен", "не копия", "не реплика", "не на запчасти",
    ];

    private static readonly string[] DefectWords =
    [
        "битый", "битая", "разбит", "на запчасти", "запчаст", "не включается", "не работает", "неисправ",
        "трещин", "дефект", "после воды", "утоплен", "под замену", "требует ремонт",
    ];

    private static readonly string[] LockWords =
    [
        "icloud", "айклауд", "залочен", "заблокирован", "блокировк", "привязан к аккаунту", "mdm",
    ];

    private static readonly string[] ScamWords =
    [
        "предоплат", "только доставк", "отправлю после оплаты", "переведите", "перевод на карту",
        "оплата на карту", "пишите в whatsapp", "пишите в ватсап", "пишите в телеграм", "пишите в тг",
        "звонки не принимаю",
    ];

    /// <param name="deviationPercent">Отклонение цены от медианы, %.</param>
    /// <param name="cheapThresholdPercent">С какого процента ниже рынка считать цену подозрительной (обычно 40).</param>
    /// <param name="category">Категория поиска: копии ищем только среди товаров.</param>
    public static IReadOnlyList<RedFlag> Detect(Listing listing, double? deviationPercent, int cheapThresholdPercent,
        SearchCategory category = SearchCategory.General)
    {
        var text = TextUtils.RemovePhrases(TextUtils.Normalize($"{listing.Title} {listing.Description}"), Negations);
        var flags = new List<RedFlag>();

        var defects = TextUtils.FindAll(text, DefectWords);
        if (defects.Count > 0)
            flags.Add(new RedFlag(FlagLevel.Warning, $"Битый / на запчасти / неисправен (в тексте: «{defects[0]}»)"));

        var locks = TextUtils.FindAll(text, LockWords);
        if (locks.Count > 0)
            flags.Add(new RedFlag(FlagLevel.Warning, $"Возможна блокировка аккаунта/iCloud (в тексте: «{locks[0]}»)"));

        var replica = ReplicaDetector.FindMarker(listing, category);
        if (replica is not null)
            flags.Add(new RedFlag(FlagLevel.Warning, $"Похоже на копию (в тексте: {replica}) — в рыночной цене не учитывается"));

        var scam = TextUtils.FindAll(text, ScamWords);
        if (scam.Count > 0)
            flags.Add(new RedFlag(FlagLevel.Danger, $"Признак мошенничества: «{scam[0]}» — не платите заранее"));

        if (deviationPercent is { } deviation)
        {
            if (deviation <= -cheapThresholdPercent)
            {
                // Дёшево, но честно написано, что битый или копия, — это объяснимо. Дёшево без причины — классический обман.
                if (defects.Count > 0 || locks.Count > 0)
                    flags.Add(new RedFlag(FlagLevel.Info, $"Цена {TextUtils.Percent(deviation)} к рынку — видимо, из-за дефекта"));
                else if (replica is not null)
                    flags.Add(new RedFlag(FlagLevel.Info, $"Цена {TextUtils.Percent(deviation)} к рынку оригиналов"));
                else
                    flags.Add(new RedFlag(FlagLevel.Danger, $"Подозрительно дёшево ({TextUtils.Percent(deviation)} к рынку) — возможен обман или копия"));
            }
            else if (deviation <= -15)
            {
                flags.Add(new RedFlag(FlagLevel.Info, $"Ниже рынка на {Math.Round(-deviation)}% — выгодно"));
            }
            else if (deviation >= 30)
            {
                flags.Add(new RedFlag(FlagLevel.Info, $"Дороже рынка на {Math.Round(deviation)}%"));
            }
        }

        if (listing.Price is null)
            flags.Add(new RedFlag(FlagLevel.Info, "Цена не указана"));

        return flags;
    }
}
