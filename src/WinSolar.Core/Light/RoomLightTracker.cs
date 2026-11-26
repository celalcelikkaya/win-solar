using WinSolar.Core.Scheduling;

namespace WinSolar.Core.Light;

/// <summary>Room is dark at or below <paramref name="DarkBelow"/> lx and light at or above <paramref name="LightAbove"/>.</summary>
public sealed record Thresholds(double DarkBelow, double LightAbove);

/// <summary>
/// Turns light-sensor readings into a room theme with hysteresis (two thresholds) and stability: a change counts only
/// after it has lasted <see cref="Stability"/>, so a hand shadow or a passing person never flips the theme.
/// </summary>
public sealed class RoomLightTracker(Thresholds thresholds)
{
    public static readonly TimeSpan Stability = TimeSpan.FromMinutes(1);

    private Theme? stable;
    private (Theme Theme, DateTimeOffset Since)? candidate;

    public Thresholds Thresholds { get; set; } = thresholds;

    public double? Lux { get; private set; }

    /// <summary>A change waiting to count, and the moment it will.</summary>
    public (Theme Theme, DateTimeOffset At)? Pending => candidate is { } c ? (c.Theme, c.Since + Stability) : null;

    public void Feed(DateTimeOffset at, double lux)
    {
        Lux = lux;
        Theme? reading = lux <= Thresholds.DarkBelow ? Theme.Dark : lux >= Thresholds.LightAbove ? Theme.Light : null;
        // A change must hold for the whole stability time: any reading off its side (back between the thresholds or
        // on the stable side) cancels it. Between the thresholds the stable theme itself never changes.
        if (reading != candidate?.Theme) candidate = null;
        if (reading is { } r && r != stable && candidate is null) candidate = (r, at);
    }

    public Theme? Stable(DateTimeOffset now)
    {
        if (candidate is { } c && now >= c.Since + Stability)
        {
            stable = c.Theme;
            candidate = null;
        }

        return stable;
    }
}
