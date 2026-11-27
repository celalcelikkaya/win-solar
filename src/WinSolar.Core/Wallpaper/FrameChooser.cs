using WinSolar.Core.Scheduling;
using WinSolar.Core.Sun;

namespace WinSolar.Core.Wallpaper;

/// <summary>Which frame of a dynamic wallpaper shows now, and when that changes.</summary>
public static class FrameChooser
{
    /// <summary>
    /// A frame on the other half of the day (morning vs afternoon) counts as this many degrees further away, so the
    /// evening picture does not show in the morning at the same sun height, yet the night frames still win at night.
    /// </summary>
    private const double OtherHalfOfDay = 15;

    private static readonly TimeSpan SolarStep = TimeSpan.FromMinutes(1);

    public static int Frame(DynamicSchedule schedule, DateTimeOffset now, Place place, Theme theme) => schedule.Kind switch
    {
        DynamicKind.Solar => Solar(schedule.Solar, now, place),
        DynamicKind.Time => Time(schedule.Time, DayFraction(now, place)),
        _ => (theme == Theme.Dark ? schedule.Dark : schedule.Light) ?? 0,
    };

    /// <summary>
    /// The next moment the frame changes (a solar set with no change in a day: a day from now); null for theme-only
    /// sets (they change with the theme).
    /// </summary>
    public static DateTimeOffset? NextChange(DynamicSchedule schedule, DateTimeOffset now, Place place, Theme theme)
    {
        switch (schedule.Kind)
        {
            case DynamicKind.Time:
            {
                var starts = schedule.Time.Select(t => t.DayFraction).Distinct().ToList();
                if (starts.Count < 2) return null;
                var zone = Zones.Find(place.TimeZoneId);
                var date = Zones.LocalDate(now, zone);
                var f = DayFraction(now, place);
                foreach (var t in starts)
                {
                    var at = At(date, t, zone);
                    if (t > f && at > now) return at;
                }

                return At(date.AddDays(1), starts[0], zone);
            }
            case DynamicKind.Solar:
            {
                var current = Frame(schedule, now, place, theme);
                for (var at = now + SolarStep; at <= now.AddDays(1); at += SolarStep)
                    if (Frame(schedule, at, place, theme) != current) return at;
                return now.AddDays(1); // no change today (polar night or day): look again tomorrow, never stop
            }
            default:
                return null;
        }
    }

    private static int Solar(IReadOnlyList<SolarFrame> frames, DateTimeOffset now, Place place)
    {
        var altitude = SunCalculator.Elevation(now, place.Lat, place.Lng);
        var morning = SunCalculator.Azimuth(now, place.Lat, place.Lng) < 180;
        return frames
            .MinBy(f => Math.Abs(f.Altitude - altitude) + (f.Azimuth % 360 < 180 == morning ? 0 : OtherHalfOfDay))!
            .Index;
    }

    private static int Time(IReadOnlyList<TimeFrame> frames, double fraction) =>
        (frames.LastOrDefault(t => t.DayFraction <= fraction) ?? frames[^1]).Index;

    private static double DayFraction(DateTimeOffset now, Place place) =>
        TimeZoneInfo.ConvertTime(now, Zones.Find(place.TimeZoneId)).TimeOfDay.TotalDays;

    private static DateTimeOffset At(DateOnly date, double fraction, TimeZoneInfo zone)
    {
        var seconds = (long)Math.Round(fraction * 86_400);
        return seconds >= 86_400
            ? Zones.ToInstant(date.AddDays(1), TimeOnly.MinValue, zone)
            : Zones.ToInstant(date, TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(seconds)), zone);
    }
}
