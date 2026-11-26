using System.Globalization;
using WinSolar.Core.Light;
using static WinSolar.Tests.TestTime;

namespace WinSolar.Tests.Light;

public class WeatherTests
{
    private const string Json = """
        { "latitude": -15.75, "hourly_units": { "time": "iso8601" },
          "hourly": { "time": ["2026-11-26T15:00", "2026-11-26T16:00", "2026-11-26T17:00"],
                      "cloud_cover": [10, 80, 100], "weather_code": [3, 61, 95] } }
        """;

    private static readonly DateTimeOffset H15 = new(2026, 11, 26, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parse_reads_hours_in_utc()
    {
        var hours = OpenMeteo.Parse(Json);
        Assert.Equal(3, hours.Count);
        Assert.Equal(new HourWeather(H15, 10, false), hours[0]);
        Assert.Equal(new HourWeather(H15.AddHours(1), 80, false), hours[1]);
        Assert.Equal(new HourWeather(H15.AddHours(2), 100, true), hours[2]);
    }

    [Fact]
    public void Parse_rejects_garbage()
    {
        Assert.Throws<FormatException>(() => OpenMeteo.Parse("<html>"));
        Assert.Throws<FormatException>(() => OpenMeteo.Parse("""{ "error": true, "reason": "x" }"""));
    }

    [Fact]
    public void Forecast_picks_the_hour_containing_t()
    {
        var f = new WeatherForecast(OpenMeteo.Parse(Json), H15);
        Assert.Equal(80, f.At(H15.AddMinutes(90), H15)!.CloudCover);
        Assert.Null(f.At(H15.AddHours(3), H15));
    }

    [Fact]
    public void Stale_forecast_is_unavailable()
    {
        var f = new WeatherForecast(OpenMeteo.Parse(Json), H15);
        Assert.NotNull(f.At(H15, H15.AddHours(5)));
        Assert.Null(f.At(H15, H15.AddHours(7)));
    }

    [Fact]
    public void Forecast_url_uses_invariant_numbers()
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            Assert.Contains("latitude=-15.7861&longitude=35.0058", OpenMeteo.ForecastUrl(Blantyre));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }
}
