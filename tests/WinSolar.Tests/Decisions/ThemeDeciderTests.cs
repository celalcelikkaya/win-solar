using WinSolar.Core.Decisions;
using WinSolar.Core.Scheduling;
using static WinSolar.Tests.TestTime;

namespace WinSolar.Tests.Decisions;

public class ThemeDeciderTests
{
    private static readonly ScheduleOptions Sun = new(ScheduleSource.Sun, 0, 0, new TimeOnly(7, 0), new TimeOnly(19, 0));
    internal static Schedule LightSchedule => ScheduleCalculator.Compute(At("2026-11-26T12:00", "Africa/Blantyre"), Blantyre, Sun);
    internal static Schedule DarkSchedule => ScheduleCalculator.Compute(At("2026-11-26T21:00", "Africa/Blantyre"), Blantyre, Sun);
    private static Hold AnyHold(Theme t) => new(t, At("2026-11-27T00:00", "Africa/Blantyre"), HoldKind.Snooze);

    [Fact]
    public void Manual_beats_everything() =>
        Assert.Equal(new Decision(Theme.Dark, Cause.Manual),
            ThemeDecider.Decide(new DecisionInput(Mode.Dark, AnyHold(Theme.Light), true, Theme.Light, true, LightSchedule)));

    [Fact]
    public void Battery_beats_hold_and_schedule() =>
        Assert.Equal(Cause.Battery,
            ThemeDecider.Decide(new DecisionInput(Mode.Auto, AnyHold(Theme.Light), true, null, false, LightSchedule)).Cause);

    [Fact]
    public void Hold_beats_room_and_schedule() =>
        Assert.Equal(Theme.Light,
            ThemeDecider.Decide(new DecisionInput(Mode.Auto, AnyHold(Theme.Light), false, Theme.Dark, true, DarkSchedule)).Theme);

    [Fact]
    public void SwitchNow_hold_has_its_own_cause() =>
        Assert.Equal(Cause.SwitchNow,
            ThemeDecider.Decide(new DecisionInput(Mode.Auto, new Hold(Theme.Dark, DateTimeOffset.MaxValue, HoldKind.SwitchNow), false, null, false, LightSchedule)).Cause);

    [Fact]
    public void Room_only_counts_inside_light_window()
    {
        Assert.Equal(Cause.Room, ThemeDecider.Decide(new DecisionInput(Mode.Auto, null, false, Theme.Dark, true, LightSchedule)).Cause);
        Assert.Equal(Theme.Light, ThemeDecider.Decide(new DecisionInput(Mode.Auto, null, false, Theme.Dark, false, LightSchedule)).Theme);
    }

    [Fact]
    public void Schedule_cause_is_previous_transition_kind() =>
        Assert.Equal(Cause.Sunset, ThemeDecider.Decide(new DecisionInput(Mode.Auto, null, false, null, false, DarkSchedule)).Cause);

    [Theory]
    [InlineData(Cause.Sunset, true)]
    [InlineData(Cause.Battery, true)]
    [InlineData(Cause.Manual, false)]
    [InlineData(Cause.Shortcut, false)]
    [InlineData(Cause.SwitchNow, false)]
    public void Defers_only_automatic_switches(Cause cause, bool expected) =>
        Assert.Equal(expected, ThemeDecider.ShouldDefer(cause, true, true));

    [Fact]
    public void Never_defers_outside_full_screen_or_when_waiting_is_off()
    {
        Assert.False(ThemeDecider.ShouldDefer(Cause.Sunset, false, true));
        Assert.False(ThemeDecider.ShouldDefer(Cause.Sunset, true, false));
    }
}
