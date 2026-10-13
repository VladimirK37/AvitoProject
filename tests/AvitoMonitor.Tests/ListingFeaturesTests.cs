using AvitoMonitor.Analysis;
using AvitoMonitor.Domain;

namespace AvitoMonitor.Tests;

public class ListingFeaturesTests
{
    [Theory]
    [InlineData("iPhone 15, 128 ГБ", "128 ГБ")]
    [InlineData("iPhone 15 Pro 256GB", "256 ГБ")]
    [InlineData("iPhone 15 Pro Max 1 ТБ", "1 ТБ")]
    [InlineData("Samsung S24 12/256 ГБ", "256 ГБ")]   // 12 ГБ оперативной памяти не считается
    [InlineData("Samsung S24 8 ГБ RAM, 512 гб", "512 ГБ")]
    [InlineData("iPhone 15 чёрный", null)]
    public void ExtractsStorage(string title, string? expected) =>
        Assert.Equal(expected, ListingFeatures.GetStorageLabel(title));

    [Theory]
    [InlineData("2-к. квартира, 54 м², 5/9 эт.", "2-к")]
    [InlineData("Квартира-студия, 25 м², 3/12 эт.", "Студия")]
    [InlineData("Комната 15 м² в 3-к., 2/5 эт.", "Комната")]
    [InlineData("Своб. планировка, 40 м², 2/5 эт.", "Своб. планировка")]
    [InlineData("Апартаменты у моря", "Другое")]
    public void ExtractsRooms(string title, string expected) =>
        Assert.Equal(expected, ListingFeatures.GetRoomsLabel(title));

    [Theory]
    [InlineData("2-к. квартира, 54,5 м², 5/9 эт.", 54.5)]
    [InlineData("Дом 120 м2 на участке 6 сот.", 120)]
    public void ExtractsArea(string title, double expected) =>
        Assert.Equal((decimal)expected, ListingFeatures.GetAreaM2(title));

    [Fact]
    public void ExtractsCarYear() =>
        Assert.Equal(2019, ListingFeatures.GetCarYear("Toyota Camry 2.5 AT, 2019, 85 000 км"));

    [Fact]
    public void RealEstateIsComparedPerSquareMeter()
    {
        var flat = new Listing { Title = "2-к. квартира, 50 м², 5/9 эт.", Price = 10_000_000 };

        Assert.Equal(200_000m, ListingFeatures.GetComparableValue(flat, SearchCategory.RealEstate));
        Assert.Equal(10_000_000m, ListingFeatures.GetComparableValue(flat, SearchCategory.General));
    }
}
