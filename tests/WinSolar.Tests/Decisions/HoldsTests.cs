using WinSolar.Core.Decisions;
using WinSolar.Core.Scheduling;
using static WinSolar.Tests.Decisions.ThemeDeciderTests;

namespace WinSolar.Tests.Decisions;

public class HoldsTests
{
    [Fact]
    public void Snooze_is_next_plus_15_min()
    {
        var s = LightSchedule;
        var h = Holds.Snooze(s, Theme.Light)!;
        Assert.Equal(s.Next!.At.AddMinutes(15), h.Until);
        Assert.Equal(Theme.Light, h.Theme);
        Assert.Equal(HoldKind.Snooze, h.Kind);
    }

    [Fact]
    public void Skip_is_until_the_switch_after_next()
    {
        var s = LightSchedule;
        Assert.Equal(s.After!.At, Holds.Skip(s, Theme.Light)!.Until);
    }

    [Fact]
    public void Skip_without_after_is_12_hours()
    {
        var s = LightSchedule with { After = null };
        Assert.Equal(s.Next!.At.AddHours(12), Holds.Skip(s, Theme.Light)!.Until);
    }

    [Fact]
    public void SwitchNow_takes_next_theme_with_a_one_day_safety_cap()
    {
        var s = LightSchedule;
        var h = Holds.SwitchNow(s)!;
        Assert.Equal(Theme.Dark, h.Theme);
        Assert.Equal(s.Next!.At.AddDays(1), h.Until);
    }

    [Fact]
    public void ManualUntil_is_next_switch_or_null_when_off()
    {
        var s = LightSchedule;
        Assert.Equal(s.Next!.At, Holds.ManualUntil(s, true));
        Assert.Null(Holds.ManualUntil(s, false));
    }

    [Fact]
    public void Nothing_to_snooze_without_a_next_switch() =>
        Assert.Null(Holds.Snooze(LightSchedule with { Next = null }, Theme.Light));
}
