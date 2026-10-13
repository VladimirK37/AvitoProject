namespace AvitoMonitor.Domain;

/// <summary>
/// Итог анализа одного объявления: с чем сравнили, насколько дешевле рынка,
/// какие есть подозрения и кто продавец.
/// </summary>
public sealed record ListingAssessment(
    Listing Listing,
    // Группа, например «256 ГБ» или «2-к»
    string GroupKey,
    // Что сравнивали: цену или (для недвижимости) цену за м²
    decimal? ComparedValue,
    bool PerSquareMeter,
    // Статистика группы, с медианой которой сравнили; null — сравнить не с чем
    GroupStats? Reference,
    // true — в группе мало объявлений, поэтому сравнили со всей выдачей
    bool ReferenceIsWholeSearch,
    // Отклонение от медианы в процентах: −25 значит «на 25% дешевле рынка»
    double? DeviationPercent,
    IReadOnlyList<RedFlag> Flags,
    SellerVerdict Seller,
    // Черновик первого сообщения продавцу
    string MessageDraft);

/// <summary>Статистика цен в группе объявлений.</summary>
public sealed record GroupStats(string Key, int Count, decimal Median, decimal Min, decimal Max);

public enum FlagLevel
{
    /// <summary>Просто к сведению: «ниже рынка на 18%».</summary>
    Info,

    /// <summary>Стоит обратить внимание: «битый», «iCloud».</summary>
    Warning,

    /// <summary>Похоже на обман.</summary>
    Danger,
}

/// <summary>Пометка к объявлению, например «Подозрительно дёшево — возможен обман».</summary>
public sealed record RedFlag(FlagLevel Level, string Text);

public enum SellerKind
{
    /// <summary>Частное лицо (товары, авто).</summary>
    Private,

    /// <summary>Собственник (недвижимость).</summary>
    Owner,

    /// <summary>Магазин или компания, которые честно так и указаны на Авито.</summary>
    Shop,

    /// <summary>Агентство недвижимости, честно указанное на Авито.</summary>
    Agency,

    /// <summary>Записан как частник, но по признакам — магазин или перекупщик.</summary>
    HiddenShop,

    /// <summary>Записан как собственник, но по признакам — агент.</summary>
    HiddenAgent,
}

/// <summary>Вывод о продавце и причины, почему программа так решила.</summary>
public sealed record SellerVerdict(SellerKind Kind, IReadOnlyList<string> Reasons)
{
    public string Title => Kind switch
    {
        SellerKind.Private => "Частник",
        SellerKind.Owner => "Собственник",
        SellerKind.Shop => "Магазин / компания",
        SellerKind.Agency => "Агентство",
        SellerKind.HiddenShop => "Похоже на магазин под видом частника",
        SellerKind.HiddenAgent => "Похоже на агента под видом собственника",
        _ => Kind.ToString(),
    };

    /// <summary>Продавец — бизнес (открыто или скрыто).</summary>
    public bool IsBusiness => Kind is SellerKind.Shop or SellerKind.Agency or SellerKind.HiddenShop or SellerKind.HiddenAgent;
}
