using AvitoMonitor.Domain;

namespace AvitoMonitor.Analysis;

/// <summary>Черновик первого сообщения продавцу. Его можно скопировать как есть или поправить.</summary>
public static class MessageDraftBuilder
{
    public static string Build(Listing listing, SearchCategory category) => category switch
    {
        SearchCategory.RealEstate =>
            $"Здравствуйте! Пишу по объявлению «{listing.Title}». Подскажите, вы собственник? " +
            "Когда можно посмотреть квартиру?",

        SearchCategory.Cars =>
            $"Здравствуйте! «{listing.Title}» ещё продаётся? Сколько владельцев по ПТС, были ли ДТП? " +
            "Когда можно посмотреть машину?",

        _ =>
            $"Здравствуйте! «{listing.Title}» ещё актуально? Готов посмотреть сегодня. " +
            "Подскажите, есть ли нюансы по состоянию?",
    };
}
