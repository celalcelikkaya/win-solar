using WinSolar.Core.Decisions;
using WinSolar.Core.Engine;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;
using WinSolar.Tests.Fakes;
using static WinSolar.Tests.TestTime;

namespace WinSolar.Tests.Engine;

public class SolarEngineTests
{
    private const string Tz = "Africa/Blantyre";
    private static readonly DateTimeOffset Noon = At("2026-11-26T12:00", Tz);

    /// <summary>Today's sunset in Blantyre (≈ 17:55), as the engine computes it.</summary>
    private static readonly DateTimeOffset Sunset =
        ScheduleCalculator.Compute(Noon, Blantyre, new AppSettings().ToScheduleOptions()).Next!.At;

    private sealed class Rig
    {
        public FakeClock Clock { get; }
        public FakeTheme Theme { get; }
        public FakeNotifier Notifier { get; } = new();
        public FakeFullScreen FullScreen { get; } = new();
        public MemoryStore Store { get; }
        public SolarEngine Engine { get; }

        public Rig(DateTimeOffset now, Theme windowsTheme, AppSettings? settings = null, AppState? state = null)
        {
            Clock = new FakeClock(now);
            Theme = new FakeTheme(windowsTheme);
            Store = new MemoryStore(settings ?? new AppSettings { Place = Blantyre }, state);
            Engine = new SolarEngine(Clock, Theme, Notifier, FullScreen, Store, Blantyre, new FakePower(), new FakeRoom(), new FakeWeather());
        }

        public EngineSnapshot TickAt(DateTimeOffset t)
        {
            Clock.Now = t;
            return Engine.Tick();
        }
    }

    [Fact]
    public void Applies_due_switch_once()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.TickAt(Sunset.AddMinutes(1));
        r.TickAt(Sunset.AddMinutes(2));
        Assert.Equal(1, r.Theme.Applies);
        Assert.Equal(Theme.Dark, r.Theme.Current);
        Assert.Equal((Theme.Dark, Cause.Sunset), Assert.Single(r.Notifier.Switches));
    }

    [Fact]
    public void Quiet_start_when_theme_already_right()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        Assert.Equal(0, r.Theme.Applies);
        Assert.Empty(r.Notifier.Switches);
    }

    [Fact]
    public void Start_applies_with_Start_cause_and_no_notification()
    {
        var r = new Rig(Noon, Theme.Dark);
        var snap = r.Engine.Start();
        Assert.Equal(1, r.Theme.Applies);
        Assert.Equal(Theme.Light, snap.Applied);
        Assert.Empty(r.Notifier.Switches);
    }

    [Fact]
    public void HeadsUp_once_five_minutes_before()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.TickAt(Sunset.AddMinutes(-6));
        r.TickAt(Sunset.AddMinutes(-5));
        r.TickAt(Sunset.AddMinutes(-4));
        var shown = Assert.Single(r.Notifier.HeadsUps);
        Assert.Equal(5, shown.Minutes);
        Assert.Equal(Theme.Dark, shown.Next.Theme);
    }

    [Fact]
    public void No_headsUp_in_manual_or_hold_or_fullscreen()
    {
        var manual = new Rig(Noon, Theme.Light, state: new AppState(Mode.Light));
        manual.Engine.Start();
        manual.TickAt(Sunset.AddMinutes(-4));
        Assert.Empty(manual.Notifier.HeadsUps);

        var held = new Rig(Noon, Theme.Light, state: new AppState(Hold: new Hold(Theme.Light, Sunset.AddHours(1), HoldKind.Pause)));
        held.Engine.Start();
        held.TickAt(Sunset.AddMinutes(-4));
        Assert.Empty(held.Notifier.HeadsUps);

        var full = new Rig(Noon, Theme.Light);
        full.Engine.Start();
        full.FullScreen.On = true;
        full.TickAt(Sunset.AddMinutes(-4));
        Assert.Empty(full.Notifier.HeadsUps);
    }

    [Fact]
    public void Snooze_holds_current_theme_and_is_saved()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.TickAt(Sunset.AddMinutes(-4));
        r.Engine.Snooze();
        Assert.Equal(Sunset.AddMinutes(15), r.Store.State.Hold!.Until);
        Assert.True(r.Notifier.Dismissed >= 1);

        r.TickAt(Sunset.AddMinutes(1));
        Assert.Equal(Theme.Light, r.Theme.Current);
        r.TickAt(Sunset.AddMinutes(16));
        Assert.Equal(Theme.Dark, r.Theme.Current);
        Assert.Equal(Cause.Sunset, r.Notifier.Switches[^1].Cause); // the hold ran out; the schedule is what switches
    }

    [Fact]
    public void FullScreen_defers_then_applies_on_exit()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.FullScreen.On = true;
        var snap = r.TickAt(Sunset.AddMinutes(1));
        Assert.Equal(0, r.Theme.Applies);
        Assert.True(snap.WaitingForFullScreen);

        r.FullScreen.On = false;
        snap = r.TickAt(Sunset.AddMinutes(30));
        Assert.False(snap.WaitingForFullScreen);
        Assert.Equal((Theme.Dark, Cause.FullScreenEnded), Assert.Single(r.Notifier.Switches));
    }

    [Fact]
    public void Manual_applies_immediately_even_in_fullscreen()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.FullScreen.On = true;
        r.Engine.SetMode(Mode.Dark);
        Assert.Equal(Theme.Dark, r.Theme.Current);
        Assert.Empty(r.Notifier.Switches);
    }

    [Fact]
    public void Manual_ends_at_next_switch()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.Engine.SetMode(Mode.Dark);
        Assert.Equal(Sunset, r.Store.State.ManualUntil);
        var snap = r.TickAt(Sunset.AddMinutes(1));
        Assert.Equal(Mode.Auto, snap.Mode);
        Assert.Equal(Theme.Dark, snap.Applied);
    }

    // Review Focus 3: asleep across the switch.
    [Fact]
    public void Wake_after_missed_switch_applies_once_without_headsUp()
    {
        var r = new Rig(Sunset.AddMinutes(-30), Theme.Light);
        r.Engine.Start();
        r.TickAt(Sunset.AddHours(2));
        Assert.Equal(1, r.Theme.Applies);
        Assert.Empty(r.Notifier.HeadsUps);
    }

    // Review Focus 5: PC was off overnight.
    [Fact]
    public void Expired_hold_and_manual_are_cleared_on_start()
    {
        var stale = new AppState(Mode.Dark, Noon.AddHours(-3), null, Theme.Dark);
        var r = new Rig(Noon, Theme.Dark, state: stale);
        var snap = r.Engine.Start();
        Assert.Equal(Mode.Auto, snap.Mode);
        Assert.Null(snap.ManualUntil);
        Assert.Equal(Theme.Light, snap.Applied);

        var staleHold = new AppState(Hold: new Hold(Theme.Dark, Noon.AddHours(-1), HoldKind.Snooze));
        var r2 = new Rig(Noon, Theme.Light, state: staleHold);
        Assert.Null(r2.Engine.Start().Hold);
        Assert.Null(r2.Store.State.Hold);
    }

    // Review Focus 2: polar night.
    [Fact]
    public void Polar_night_never_sets_a_due_time_in_the_past()
    {
        var now = At("2026-12-21T12:00", "Europe/Oslo");
        var r = new Rig(now, Theme.Dark, new AppSettings { Place = Tromso });
        var snap = r.Engine.Start();
        Assert.Equal(Theme.Dark, snap.Applied);
        Assert.Equal(now + NextDue.MaxWait, r.Engine.NextDue);
    }

    [Fact]
    public void Shortcut_flips_then_returns_to_auto()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();

        var first = r.Engine.Shortcut();
        Assert.Equal(Theme.Dark, first.Theme);
        Assert.StartsWith("Auto again at ", first.Detail);
        Assert.Equal(Mode.Dark, r.Store.State.Mode);

        var second = r.Engine.Shortcut();
        Assert.Equal(Theme.Light, second.Theme);
        Assert.Equal("Back to Auto", second.Detail);
        Assert.Equal(Mode.Auto, r.Store.State.Mode);
        Assert.Empty(r.Notifier.Switches);
    }

    [Fact]
    public void SwitchNow_applies_next_theme_immediately_and_quietly()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.TickAt(Sunset.AddMinutes(-4));
        r.Engine.SwitchNow();
        Assert.Equal(Theme.Dark, r.Theme.Current);
        Assert.Empty(r.Notifier.Switches);
        r.TickAt(Sunset.AddMinutes(1));
        Assert.Equal(1, r.Theme.Applies);
    }

    [Fact]
    public void Theme_changed_outside_is_respected()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.Theme.SetOutside(Theme.Dark);
        var (theme, _) = r.Engine.Shortcut();
        Assert.Equal(Theme.Light, theme);
    }

    [Fact]
    public void SwitchNow_ends_when_schedule_catches_up()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.TickAt(Sunset.AddMinutes(-30));
        r.Engine.SwitchNow();
        var after = r.TickAt(Sunset.AddMinutes(1));
        Assert.Null(after.Hold);
        Assert.Equal(Theme.Dark, r.Theme.Current);
        Assert.Equal(1, r.Theme.Applies);
    }

    [Fact]
    public void Auto_from_manual_is_quiet()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.Engine.SetMode(Mode.Dark);
        r.Engine.SetMode(Mode.Auto);
        Assert.Equal(Theme.Light, r.Theme.Current);
        Assert.Empty(r.Notifier.Switches);
    }

    [Fact]
    public void Save_failure_does_not_throw()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.Store.FailNextSaves = 1;
        r.Engine.SetMode(Mode.Dark);
        Assert.Equal(Mode.Auto, r.Store.State.Mode);
        r.TickAt(Noon.AddMinutes(1));
        Assert.Equal(Mode.Dark, r.Store.State.Mode);
    }

    [Fact]
    public void Apply_failure_is_reported()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.Theme.FailApply = true;
        var snap = r.TickAt(Sunset.AddMinutes(1));
        Assert.Equal(Theme.Light, snap.Applied);
        Assert.Equal("Could not change the Windows theme", snap.Error);
        r.Theme.FailApply = false;
        snap = r.TickAt(Sunset.AddMinutes(2));
        Assert.Equal(Theme.Dark, snap.Applied);
        Assert.Null(snap.Error);
    }

    [Fact]
    public void Outside_change_is_not_reverted_until_the_next_switch()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.Theme.SetOutside(Theme.Dark);
        Assert.Equal(Theme.Dark, r.TickAt(Noon.AddMinutes(1)).Applied);
        Assert.Equal(0, r.Theme.Applies);
        var tomorrowSunrise = ScheduleCalculator.Compute(Sunset.AddMinutes(1), Blantyre, new AppSettings().ToScheduleOptions()).Next!.At;
        r.TickAt(tomorrowSunrise.AddMinutes(1));
        Assert.Equal(Theme.Light, r.Theme.Current);
    }

    [Fact]
    public void Outside_change_during_a_pause_is_kept()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.Engine.Pause(TimeSpan.FromHours(1));
        r.Theme.SetOutside(Theme.Dark);
        Assert.Equal(Theme.Dark, r.TickAt(Noon.AddMinutes(1)).Applied);
        Assert.Equal(0, r.Theme.Applies);
    }

    [Fact]
    public void Outside_change_in_manual_mode_is_kept_until_the_mode_changes()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.Engine.SetMode(Mode.Light);
        r.Theme.SetOutside(Theme.Dark);
        Assert.Equal(Theme.Dark, r.TickAt(Noon.AddMinutes(1)).Applied);
        Assert.Equal(0, r.Theme.Applies);
        r.Engine.SetMode(Mode.Auto);
        Assert.Equal(Theme.Light, r.Theme.Current);
    }

    [Fact]
    public void A_tick_during_a_switch_does_not_switch_twice()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.Theme.DuringApply = () => r.Engine.Tick();
        r.Engine.SetMode(Mode.Dark);
        Assert.Equal(1, r.Theme.Applies);
        Assert.Equal(Theme.Dark, r.Theme.Current);
        Assert.Equal(Theme.Dark, r.TickAt(Noon.AddMinutes(1)).Applied);
        Assert.Equal(1, r.Theme.Applies);
    }
}
