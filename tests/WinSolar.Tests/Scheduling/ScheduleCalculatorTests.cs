using WinSolar.Core.Scheduling;
using WinSolar.Core.Sun;
using static WinSolar.Tests.TestTime;

namespace WinSolar.Tests.Scheduling;

public class ScheduleCalculatorTests
{
    private static readonly ScheduleOptions Sun = new(ScheduleSource.Sun, 0, 0, new TimeOnly(7, 0), new TimeOnly(19, 0));

    [Fact]
    public void Before_sunset_is_light_and_next_is_sunset()
    {
        var s = ScheduleCalculator.Compute(At("2026-11-26T17:36", "Africa/Blantyre"), Blantyre, Sun);
        Assert.Equal(Theme.Light, s.Theme);
        Assert.Equal(TransitionKind.Sunset, s.Next!.Kind);
        Assert.Equal(Theme.Dark, s.Next.Theme);
        Assert.Equal(Phase.Dusk, s.Phase);
    }

    [Fact]
    public void Sunset_offset_moves_the_switch()
    {
        var now = At("2026-11-26T12:00", "Africa/Blantyre");
        var a = ScheduleCalculator.Compute(now, Blantyre, Sun);
        var b = ScheduleCalculator.Compute(now, Blantyre, Sun with { OffsetSunsetMin = 10 });
        Assert.Equal(TimeSpan.FromMinutes(10), b.Next!.At - a.Next!.At);
    }

    [Fact]
    public void Fixed_times_before_7_are_dark_with_next_light_at_7()
    {
        var s = ScheduleCalculator.Compute(At("2026-11-26T06:00", "Africa/Blantyre"), Blantyre, Sun with { Source = ScheduleSource.Fixed });
        Assert.Equal(Theme.Dark, s.Theme);
        Assert.Equal(At("2026-11-26T07:00", "Africa/Blantyre"), s.Next!.At);
    }

    [Fact]
    public void Polar_night_has_dark_theme_and_no_crash()
    {
        var s = ScheduleCalculator.Compute(At("2026-12-21T12:00", "Europe/Oslo"), Tromso, Sun);
        Assert.Equal(Theme.Dark, s.Theme);
        Assert.Equal(PolarState.Night, s.Today.Polar);
    }

    [Fact]
    public void Adjust_hook_moves_sunset_and_records_shift()
    {
        var s = ScheduleCalculator.Compute(At("2026-11-26T12:00", "Africa/Blantyre"), Blantyre, Sun,
            (t, k) => k == TransitionKind.Sunset ? t.AddMinutes(-15) : t);
        Assert.Equal(-15, s.Next!.ShiftMinutes);
    }

    [Fact]
    public void Progress_is_between_zero_and_one()
    {
        var s = ScheduleCalculator.Compute(At("2026-11-26T12:00", "Africa/Blantyre"), Blantyre, Sun);
        Assert.InRange(s.Progress, 0.0, 1.0);
        Assert.Equal(Phase.Day, s.Phase);
    }
}
