using WinSolar.Core.Sun;
using static WinSolar.Tests.TestTime;

namespace WinSolar.Tests.Sun;

public class SunCalculatorTests
{
    [Fact]
    public void Blantyre_2026_11_26()
    {
        var d = SunCalculator.ForDate(new DateOnly(2026, 11, 26), -15.7861, 35.0058);
        Assert.Equal(PolarState.None, d.Polar);
        AssertNear(new TimeOnly(4, 58), Local(d.Sunrise, "Africa/Blantyre"), 1);
        AssertNear(new TimeOnly(17, 55), Local(d.Sunset, "Africa/Blantyre"), 1);
        AssertNear(new TimeOnly(11, 27), Local(d.Noon, "Africa/Blantyre"), 1);
    }

    [Fact]
    public void Tromso_midwinter_is_polar_night() =>
        Assert.Equal(PolarState.Night, SunCalculator.ForDate(new DateOnly(2026, 12, 21), 69.6496, 18.957).Polar);

    [Fact]
    public void Tromso_midsummer_is_polar_day() =>
        Assert.Equal(PolarState.Day, SunCalculator.ForDate(new DateOnly(2026, 6, 21), 69.6496, 18.957).Polar);

    [Fact]
    public void Elevation_is_about_minus_0_8_at_sunset()
    {
        var d = SunCalculator.ForDate(new DateOnly(2026, 11, 26), -15.7861, 35.0058);
        Assert.InRange(SunCalculator.Elevation(d.Sunset!.Value, -15.7861, 35.0058), -1.5, 0.0);
    }
}
