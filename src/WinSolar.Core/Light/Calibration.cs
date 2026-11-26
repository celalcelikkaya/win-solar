namespace WinSolar.Core.Light;

/// <summary>Hourly median room light by day and in the evening, newest last.</summary>
public sealed record Calibration(IReadOnlyList<double> Day, IReadOnlyList<double> Evening)
{
    public static Calibration Empty { get; } = new([], []);
}

/// <summary>Self-calibration: thresholds between this room's typical evening and daytime light.</summary>
public static class CalibrationMath
{
    public static readonly Thresholds Default = new(60, 150);
    public const int MinHours = 6;
    public const int Keep = 48;

    public static bool IsLearned(Calibration c) =>
        c.Day.Count >= MinHours && c.Evening.Count >= MinHours && Median(c.Day) >= 1.5 * Median(c.Evening);

    /// <summary>Dark below 35% and light above 65% of the way from evening to day light, measured in log space.</summary>
    public static Thresholds For(Calibration c)
    {
        if (!IsLearned(c)) return Default;
        double e = Math.Log(Median(c.Evening)), d = Math.Log(Median(c.Day));
        return new Thresholds(Math.Exp(e + 0.35 * (d - e)), Math.Exp(e + 0.65 * (d - e)));
    }

    public static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}

/// <summary>
/// Collects readings per UTC hour: daytime (sun ≥ 10°) and evening (sun ≤ −6°), ignoring twilight and a dark room
/// (&lt; 5 lx: lid closed or lights off). When an hour ends, each bucket with 3+ readings adds its median.
/// </summary>
public sealed class CalibrationLearner(Calibration start)
{
    private const double MinLux = 5;
    private const int MinReadings = 3;
    private readonly List<double> day = [];
    private readonly List<double> evening = [];
    private DateTimeOffset? hour;

    public Calibration Current { get; private set; } = start;

    /// <summary>Adds a reading; true when <see cref="Current"/> changed (save it).</summary>
    public bool Add(DateTimeOffset at, double lux, double sunElevation)
    {
        var thisHour = new DateTimeOffset(at.UtcTicks - at.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero);
        var changed = false;
        if (hour is { } h && h != thisHour)
        {
            var newDay = Close(day, Current.Day);
            var newEvening = Close(evening, Current.Evening);
            if (newDay != Current.Day || newEvening != Current.Evening)
            {
                Current = new Calibration(newDay, newEvening);
                changed = true;
            }
        }

        hour = thisHour;
        if (lux >= MinLux)
        {
            if (sunElevation >= 10) day.Add(lux);
            else if (sunElevation <= -6) evening.Add(lux);
        }

        return changed;
    }

    public void Reset()
    {
        Current = Calibration.Empty;
        day.Clear();
        evening.Clear();
    }

    private static IReadOnlyList<double> Close(List<double> bucket, IReadOnlyList<double> kept)
    {
        if (bucket.Count < MinReadings)
        {
            bucket.Clear();
            return kept;
        }

        var list = kept.Append(CalibrationMath.Median(bucket)).TakeLast(CalibrationMath.Keep).ToList();
        bucket.Clear();
        return list;
    }
}
