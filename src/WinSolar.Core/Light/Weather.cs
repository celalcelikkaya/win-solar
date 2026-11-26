using System.Globalization;
using System.Text.Json;

namespace WinSolar.Core.Light;

/// <summary>One forecast hour starting at <paramref name="Hour"/> (UTC). Storm = WMO weather code 95–99.</summary>
public sealed record HourWeather(DateTimeOffset Hour, int CloudCover, bool Storm);

/// <summary>An hourly forecast that is trusted for <see cref="MaxAge"/> after it was fetched.</summary>
public sealed class WeatherForecast(IReadOnlyList<HourWeather> hours, DateTimeOffset fetchedAt)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    public DateTimeOffset FetchedAt { get; } = fetchedAt;

    public HourWeather? At(DateTimeOffset t, DateTimeOffset now)
    {
        if (now - FetchedAt > MaxAge) return null;
        foreach (var h in hours)
            if (h.Hour <= t && t < h.Hour.AddHours(1)) return h;
        return null;
    }
}

/// <summary>Open-Meteo forecast API (free, no key): hourly cloud cover and weather code.</summary>
public static class OpenMeteo
{
    public static string ForecastUrl(Place p) => string.Create(CultureInfo.InvariantCulture,
        $"https://api.open-meteo.com/v1/forecast?latitude={p.Lat:0.####}&longitude={p.Lng:0.####}&hourly=cloud_cover,weather_code&timezone=UTC&past_days=1&forecast_days=2");

    public static IReadOnlyList<HourWeather> Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var hourly = doc.RootElement.GetProperty("hourly");
            var times = hourly.GetProperty("time");
            var clouds = hourly.GetProperty("cloud_cover");
            var codes = hourly.GetProperty("weather_code");
            var n = times.GetArrayLength();
            if (clouds.GetArrayLength() != n || codes.GetArrayLength() != n) throw new FormatException("Forecast arrays differ in length.");
            var list = new List<HourWeather>(n);
            for (var i = 0; i < n; i++)
            {
                if (clouds[i].ValueKind != JsonValueKind.Number) continue; // missing hours are null
                var at = DateTime.ParseExact(times[i].GetString()!, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                var code = codes[i].ValueKind == JsonValueKind.Number ? codes[i].GetInt32() : 0;
                list.Add(new HourWeather(new DateTimeOffset(at, TimeSpan.Zero), (int)Math.Round(clouds[i].GetDouble()), code is >= 95 and <= 99));
            }

            return list;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new FormatException("Unexpected Open-Meteo response.", e);
        }
    }
}
