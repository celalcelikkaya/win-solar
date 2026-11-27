using WinSolar.Core.Presentation;
using WinSolar.Core.Scheduling;
using static WinSolar.Tests.TestTime;

namespace WinSolar.Tests.Presentation;

public class FormatTests
{
    [Fact]
    public void Duration_formats()
    {
        Assert.Equal("1 h 40 m", Format.Duration(100));
        Assert.Equal("5 min", Format.Duration(5));
    }

    [Fact]
    public void Time_24h_and_12h()
    {
        var t = new DateTimeOffset(2026, 11, 26, 15, 55, 0, TimeSpan.Zero); // 17:55 in Blantyre
        Assert.Equal("17:55", Format.Time(t, "Africa/Blantyre", false));
        Assert.Equal("5:55 PM", Format.Time(t, "Africa/Blantyre", true));
    }

    [Fact]
    public void When_says_tomorrow()
    {
        var now = new DateTimeOffset(2026, 11, 26, 18, 0, 0, TimeSpan.Zero);
        Assert.EndsWith(" tomorrow", Format.When(now.AddHours(10), now, "Africa/Blantyre", false));
        Assert.DoesNotContain("tomorrow", Format.When(now.AddMinutes(30), now, "Africa/Blantyre", false));
    }

    // Review Focus 1: the place's zone decides, not the PC's.
    [Fact]
    public void Uses_place_zone_not_pc_zone()
    {
        var opt = new ScheduleOptions(ScheduleSource.Sun, 0, 0, new TimeOnly(7, 0), new TimeOnly(19, 0));
        var s = ScheduleCalculator.Compute(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero), Istanbul, opt);
        var localSunset = TimeZoneInfo.ConvertTime(s.TodayDark!.At, Zones.Find("Europe/Istanbul"));
        Assert.InRange(localSunset.Hour, 16, 17); // Istanbul late-November sunset ≈ 16:40
    }

    [Theory]
    [InlineData(500L, "1 KB")]
    [InlineData(1_500_000L, "1.5 MB")]
    [InlineData(48_000_000L, "48 MB")]
    [InlineData(2_400_000_000L, "2.4 GB")]
    public void Size_is_short(long bytes, string expected) => Assert.Equal(expected, Format.Size(bytes));
}
