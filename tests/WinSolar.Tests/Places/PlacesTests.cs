namespace WinSolar.Tests.Places;

public class PlacesTests
{
    private static readonly DateTimeOffset Nov26 = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Popular_has_the_twelve_demo_cities_with_blantyre_first()
    {
        Assert.Equal(12, WinSolar.Core.Places.Popular.Count);
        Assert.Equal("Blantyre", WinSolar.Core.Places.Popular[0].Name);
    }

    [Fact]
    public void Guess_by_exact_zone() =>
        Assert.Equal("Istanbul", WinSolar.Core.Places.Guess("Europe/Istanbul", TimeSpan.FromHours(3), Nov26).Name);

    [Fact]
    public void Guess_by_offset_when_zone_is_not_listed() =>
        Assert.Equal("Blantyre", WinSolar.Core.Places.Guess("Africa/Johannesburg", TimeSpan.FromHours(2), Nov26).Name);

    [Fact]
    public void Guess_falls_back_to_an_approximate_place()
    {
        var p = WinSolar.Core.Places.Guess("Asia/Kolkata", TimeSpan.FromHours(5.5), Nov26);
        Assert.Equal("Kolkata", p.Name);
        Assert.Equal("Asia/Kolkata", p.TimeZoneId);
        Assert.Equal(82.5, p.Lng);
    }
}
