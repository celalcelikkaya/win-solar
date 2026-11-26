using WinSolar.Core.Scheduling;
using WinSolar.Core.Sun;

namespace WinSolar.Core.Light;

/// <summary>
/// Outdoor light model from the spec (and the demo): light ≈ max(0, sin(elevation + 6°)) × weather factor. A switch
/// happens where that crosses the clear-sky value at sunset, never more than maxShift from the sun event.
/// </summary>
public static class LightModel
{
    private const double Rad = Math.PI / 180;

    /// <summary>Clear-sky outdoor light at sunset (elevation −0.833°).</summary>
    public static readonly double Threshold = Math.Sin(5.167 * Rad);

    public static double Daylight(DateTimeOffset at, Place place) =>
        Math.Max(0, Math.Sin((SunCalculator.Elevation(at, place.Lat, place.Lng) + 6) * Rad));

    /// <summary>Clear 1, about 0.6 at 70% cloud, overcast 0.3; a thunderstorm 0.12.</summary>
    public static double WeatherFactor(HourWeather w) =>
        w.Storm ? 0.12 : 1 - 0.7 * Math.Pow(Math.Clamp(w.CloudCover, 0, 100) / 100.0, 1.5);

    public static string WeatherLabel(HourWeather w) =>
        w.Storm ? "Storm" : w.CloudCover < 25 ? "Clear" : w.CloudCover < 75 ? "Cloudy" : "Overcast";

    /// <summary>
    /// The <see cref="ScheduleCalculator"/> adjust hook: a darker sky moves sunset earlier and sunrise later, by at
    /// most <paramref name="maxShift"/>. <paramref name="factorAt"/> gives the weather factor at an instant.
    /// </summary>
    public static DateTimeOffset Adjust(DateTimeOffset sunEvent, TransitionKind kind, Place place,
        Func<DateTimeOffset, double> factorAt, TimeSpan maxShift)
    {
        if (factorAt(sunEvent) >= 1) return sunEvent;
        var step = TimeSpan.FromMinutes(1);
        if (kind == TransitionKind.Sunset)
        {
            for (var t = sunEvent - maxShift; t <= sunEvent; t += step)
                if (Daylight(t, place) * factorAt(t) < Threshold) return t;
            return sunEvent;
        }

        for (var t = sunEvent; t <= sunEvent + maxShift; t += step)
            if (Daylight(t, place) * factorAt(t) >= Threshold) return t;
        return sunEvent + maxShift;
    }

    /// <summary>Inside the light window: within maxShift of an astronomical sunrise or sunset.</summary>
    public static bool InWindow(DateTimeOffset now, IReadOnlyList<DateTimeOffset> sunEvents, TimeSpan maxShift) =>
        sunEvents.Any(e => (now - e).Duration() <= maxShift);

    public static IEnumerable<DateTimeOffset> WindowEdges(IReadOnlyList<DateTimeOffset> sunEvents, TimeSpan maxShift) =>
        sunEvents.SelectMany(e => new[] { e - maxShift, e + maxShift });
}
