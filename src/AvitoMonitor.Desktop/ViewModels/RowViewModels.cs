using AvitoMonitor.Analysis;
using AvitoMonitor.Domain;
using CommunityToolkit.Mvvm.Input;

namespace AvitoMonitor.Desktop.ViewModels;

/// <summary>Строка таблицы «Объявления»: всё уже отформатировано для показа.</summary>
public sealed partial class ListingRowViewModel
{
    public ListingRowViewModel(ListingAssessment assessment)
    {
        var listing = assessment.Listing;

        Group = assessment.GroupKey;
        Title = listing.Title;
        Url = listing.Url;
        Price = listing.Price;
        PriceText = listing.Price is { } price ? TextUtils.Money(price) : "—";
        Median = assessment.Reference?.Median;
        MedianText = assessment.Reference is { } reference ? TextUtils.Metric(reference.Median, assessment.PerSquareMeter) : "—";
        Deviation = assessment.DeviationPercent;
        DeviationText = assessment.DeviationPercent is { } deviation ? TextUtils.Percent(deviation) : "—";
        IsCheap = assessment.DeviationPercent <= -15;
        IsExpensive = assessment.DeviationPercent >= 20;
        Flags = string.Join("; ", assessment.Flags.Select(flag => flag.Text));
        HasDanger = assessment.Flags.Any(flag => flag.Level == FlagLevel.Danger);
        SellerName = listing.SellerName ?? "";
        Reviews = listing.SellerReviews;
        SellerKind = assessment.Seller.Title;
        SellerReasons = string.Join("; ", assessment.Seller.Reasons);
        IsBusiness = assessment.Seller.IsBusiness;
        Address = listing.Address ?? "";
        Published = listing.PublishedText ?? "";
        FirstSeen = listing.FirstSeenAt;
        FirstSeenText = listing.FirstSeenAt.ToString("dd.MM HH:mm");
        IsExcluded = assessment.Exclusion is not null;
    }

    public string Group { get; }
    public string Title { get; }
    public string Url { get; }
    public decimal? Price { get; }
    public string PriceText { get; }
    public decimal? Median { get; }
    public string MedianText { get; }
    public double? Deviation { get; }
    public string DeviationText { get; }
    public bool IsCheap { get; }
    public bool IsExpensive { get; }
    public string Flags { get; }
    public bool HasDanger { get; }
    public string SellerName { get; }
    public int? Reviews { get; }
    public string SellerKind { get; }
    public string SellerReasons { get; }
    public bool IsBusiness { get; }
    public string Address { get; }
    public string Published { get; }
    public DateTime FirstSeen { get; }
    public string FirstSeenText { get; }

    /// <summary>Копия или другая модель — не учитывается в рыночной цене.</summary>
    public bool IsExcluded { get; }

    [RelayCommand]
    private void Open() => SystemShell.Open(Url);
}

/// <summary>Строка таблицы «Рынок».</summary>
public sealed class GroupRowViewModel(GroupStats stats, bool perSquareMeter)
{
    public string Group { get; } = stats.Key;
    public int Count { get; } = stats.Count;
    public decimal Median { get; } = stats.Median;
    public string MedianText { get; } = TextUtils.Metric(stats.Median, perSquareMeter);
    public string MinText { get; } = TextUtils.Metric(stats.Min, perSquareMeter);
    public string MaxText { get; } = TextUtils.Metric(stats.Max, perSquareMeter);
    public int Outliers { get; } = stats.Outliers;
}

/// <summary>Карточка во вкладке «Новые объявления».</summary>
public sealed partial class FeedItemViewModel
{
    public FeedItemViewModel(SavedSearch search, ListingAssessment assessment)
    {
        var listing = assessment.Listing;
        Title = listing.Title;
        Url = listing.Url;

        var price = listing.Price is { } p ? TextUtils.Money(p) : "цена не указана";
        PriceLine = assessment.Reference is { } reference && assessment.DeviationPercent is { } deviation
            ? $"{price} · медиана {TextUtils.Metric(reference.Median, assessment.PerSquareMeter)} · {TextUtils.Percent(deviation)}"
            : price;

        var seller = string.IsNullOrWhiteSpace(listing.SellerName) ? assessment.Seller.Title : $"{listing.SellerName} — {assessment.Seller.Title}";
        var address = string.IsNullOrWhiteSpace(listing.Address) ? "" : $" · {listing.Address}";
        InfoLine = $"{DateTime.Now:HH:mm} · {search.Name} · {assessment.GroupKey} · {seller}{address}";

        Flags = string.Join("; ", assessment.Flags.Select(flag => flag.Text));
        HasDanger = assessment.Flags.Any(flag => flag.Level == FlagLevel.Danger);
    }

    public string Title { get; }
    public string Url { get; }
    public string PriceLine { get; }
    public string InfoLine { get; }
    public string Flags { get; }
    public bool HasDanger { get; }

    [RelayCommand]
    private void Open() => SystemShell.Open(Url);
}
