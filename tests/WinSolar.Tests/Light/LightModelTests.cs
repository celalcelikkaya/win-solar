using WinSolar.Core.Light;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Sun;
using static WinSolar.Tests.TestTime;

namespace WinSolar.Tests.Light;

public class LightModelTests
{
    private static readonly SunDay Day = SunCalculator.ForDate(new DateOnly(2026, 11, 26), Blantyre.Lat, Blantyre.Lng);
    private static readonly DateTimeOffset Sunset = Day.Sunset!.Value;
    private static readonly DateTimeOffset Sunrise = Day.Sunrise!.Value;

    [Fact]
    public void Clear_sky_keeps_the_sun_time() =>
        Assert.Equal(Sunset, LightModel.Adjust(Sunset, TransitionKind.Sunset, Blantyre, _ => 1, TimeSpan.FromMinutes(60)));

    [Fact]
    public void Storm_moves_sunset_by_the_whole_window() =>
        Assert.Equal(Sunset.AddMinutes(-30), LightModel.Adjust(Sunset, TransitionKind.Sunset, Blantyre, _ => 0.12, TimeSpan.FromMinutes(30)));

    [Fact]
    public void Storm_moves_sunrise_by_the_whole_window() =>
        Assert.Equal(Sunrise.AddMinutes(30), LightModel.Adjust(Sunrise, TransitionKind.Sunrise, Blantyre, _ => 0.12, TimeSpan.FromMinutes(30)));

    [Fact]
    public void Overcast_moves_sunset_earlier_inside_the_window()
    {
        var t = LightModel.Adjust(Sunset, TransitionKind.Sunset, Blantyre, _ => 0.3, TimeSpan.FromMinutes(60));
        Assert.InRange(t, Sunset.AddMinutes(-60), Sunset.AddMinutes(-1));
    }

    [Fact]
    public void Threshold_is_clear_sky_at_sunset() =>
        Assert.InRange(LightModel.Daylight(Sunset, Blantyre), LightModel.Threshold - 0.01, LightModel.Threshold + 0.01);

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(100, 0.3)]
    [InlineData(70, 0.59)]
    public void Factor_from_cloud_cover(int cloud, double factor) =>
        Assert.Equal(factor, LightModel.WeatherFactor(new HourWeather(default, cloud, false)), 2);

    [Fact]
    public void Storm_factor_is_0_12() =>
        Assert.Equal(0.12, LightModel.WeatherFactor(new HourWeather(default, 10, true)));

    [Theory]
    [InlineData(10, false, "Clear")]
    [InlineData(50, false, "Cloudy")]
    [InlineData(90, false, "Overcast")]
    [InlineData(90, true, "Storm")]
    public void Labels(int cloud, bool storm, string label) =>
        Assert.Equal(label, LightModel.WeatherLabel(new HourWeather(default, cloud, storm)));
}
