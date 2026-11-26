using WinSolar.Core;
using WinSolar.Core.Decisions;
using WinSolar.Core.Engine;
using WinSolar.Core.Light;
using WinSolar.Core.Power;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;
using WinSolar.Tests.Fakes;
using static WinSolar.Tests.TestTime;

namespace WinSolar.Tests.Engine;

public class RealLightEngineTests
{
    private const string Tz = "Africa/Blantyre";
    private static readonly DateTimeOffset Noon = At("2026-11-26T12:00", Tz);
    private static readonly DateTimeOffset Sunset =
        ScheduleCalculator.Compute(Noon, Blantyre, new AppSettings().ToScheduleOptions()).Next!.At;
    private static readonly DateTimeOffset TomorrowSunrise =
        ScheduleCalculator.Compute(Sunset.AddMinutes(1), Blantyre, new AppSettings().ToScheduleOptions()).Next!.At;

    private sealed class Rig
    {
        public FakeClock Clock { get; }
        public FakeTheme Theme { get; }
        public FakeNotifier Notifier { get; } = new();
        public FakeFullScreen FullScreen { get; } = new();
        public FakePower Power { get; } = new();
        public FakeRoom Room { get; } = new();
        public FakeWeather Weather { get; } = new();
        public MemoryStore Store { get; }
        public SolarEngine Engine { get; }

        public Rig(DateTimeOffset now, Theme windowsTheme, AppSettings? settings = null, Place? place = null)
        {
            Clock = new FakeClock(now);
            Theme = new FakeTheme(windowsTheme);
            Store = new MemoryStore(settings ?? new AppSettings { Place = place ?? Blantyre });
            Engine = new SolarEngine(Clock, Theme, Notifier, FullScreen, Store, place ?? Blantyre, Power, Room, Weather);
        }

        public Rig WithSensor(Theme? room)
        {
            Room.Available = true;
            Room.Theme = room;
            Room.Lux = room == Core.Scheduling.Theme.Dark ? 40 : 400;
            return this;
        }

        public EngineSnapshot TickAt(DateTimeOffset t)
        {
            Clock.Now = t;
            return Engine.Tick();
        }
    }

    private static HourWeather Cloud(int percent, bool storm = false) => new(default, percent, storm);

    [Fact]
    public void Room_dark_inside_window_switches_early()
    {
        var r = new Rig(Noon, Theme.Light).WithSensor(Theme.Dark);
        r.Engine.Start();
        var s = r.TickAt(Sunset.AddMinutes(-40));
        Assert.Equal(Theme.Dark, s.Applied);
        Assert.Equal((Theme.Dark, Cause.Room), Assert.Single(r.Notifier.Switches));
        Assert.Equal(LightSource.Sensor, s.Light.Source);
        Assert.True(s.Light.InWindow);
        Assert.Equal(40, s.Light.Lux);
    }

    [Fact]
    public void Room_outside_window_is_ignored()
    {
        var r = new Rig(Noon, Theme.Light).WithSensor(Theme.Dark);
        Assert.Equal(Theme.Light, r.Engine.Start().Applied);
        Assert.Equal(0, r.Theme.Applies);
        r.Room.Theme = Theme.Light;
        Assert.Equal(Theme.Dark, r.TickAt(Sunset.AddHours(5)).Applied);
    }

    [Fact]
    public void Room_light_holds_light_until_the_window_ends()
    {
        var r = new Rig(Noon, Theme.Light).WithSensor(Theme.Light);
        r.Engine.Start();
        Assert.Equal(Theme.Light, r.TickAt(Sunset.AddMinutes(30)).Applied);
        Assert.Equal(Theme.Dark, r.TickAt(Sunset.AddMinutes(61)).Applied);
        Assert.Equal((Theme.Dark, Cause.Sunset), Assert.Single(r.Notifier.Switches));
    }

    [Fact]
    public void No_heads_up_in_sensor_mode()
    {
        var r = new Rig(Noon, Theme.Light).WithSensor(null);
        r.Engine.Start();
        r.TickAt(Sunset.AddMinutes(-4));
        Assert.Empty(r.Notifier.HeadsUps);
    }

    [Fact]
    public void Sensor_wins_over_weather()
    {
        var r = new Rig(Noon, Theme.Light).WithSensor(null);
        r.Weather.Now = Cloud(100, storm: true);
        Assert.Equal(Sunset, r.Engine.Start().Schedule.Next!.At);
    }

    [Fact]
    public void Weather_moves_sunset_when_there_is_no_sensor()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Weather.Now = Cloud(100);
        var s = r.Engine.Start();
        Assert.True(s.Schedule.Next!.At < Sunset);
        Assert.True(s.Schedule.Next.ShiftMinutes < 0);
        Assert.Equal(LightSource.Weather, s.Light.Source);
        Assert.Equal(100, s.Light.Weather!.CloudCover);
    }

    [Fact]
    public void No_weather_uses_plain_sun_times()
    {
        var s = new Rig(Noon, Theme.Light).Engine.Start();
        Assert.Equal(Sunset, s.Schedule.Next!.At);
        Assert.Equal(LightSource.WeatherUnavailable, s.Light.Source);
    }

    [Fact]
    public void Real_light_off_ignores_sensor_and_weather()
    {
        var r = new Rig(Noon, Theme.Light, new AppSettings { Place = Blantyre, FollowRealLight = false }).WithSensor(Theme.Dark);
        r.Weather.Now = Cloud(100);
        r.Engine.Start();
        var s = r.TickAt(Sunset.AddMinutes(-40));
        Assert.Equal(Theme.Light, s.Applied);
        Assert.Equal(Sunset, s.Schedule.Next!.At);
        Assert.Equal(LightSource.Off, s.Light.Source);
    }

    [Fact]
    public void Polar_night_ignores_room_light()
    {
        var noon = At("2026-12-15T12:00", "Europe/Oslo");
        var r = new Rig(noon, Theme.Light, place: Tromso).WithSensor(Theme.Light);
        var s = r.Engine.Start();
        Assert.Equal(Theme.Dark, s.Applied);
        Assert.False(s.Light.InWindow);
        Assert.True(r.Engine.NextDue > noon);
    }

    [Fact]
    public void Battery_at_20_percent_is_low()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.Power.Status = new PowerStatus(true, 20);
        var s = r.TickAt(Noon.AddMinutes(1));
        Assert.Equal(Theme.Dark, s.Applied);
        Assert.True(s.BatteryDark);
        Assert.Equal(20, s.Power.Percent);
        Assert.Equal((Theme.Dark, Cause.Battery), Assert.Single(r.Notifier.Switches));
        r.Power.Status = PowerStatus.Mains;
        Assert.Equal(Theme.Light, r.TickAt(Noon.AddMinutes(2)).Applied);
    }

    [Fact]
    public void Battery_switch_waits_for_full_screen()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Engine.Start();
        r.FullScreen.On = true;
        r.Power.Status = new PowerStatus(true, 15);
        var s = r.TickAt(Noon.AddMinutes(1));
        Assert.Equal(Theme.Light, s.Applied);
        Assert.True(s.WaitingForFullScreen);
    }

    [Fact]
    public void No_heads_up_while_battery_dark()
    {
        var r = new Rig(Sunset.AddHours(3), Theme.Dark);
        r.Engine.Start();
        r.Power.Status = new PowerStatus(true, 10);
        r.TickAt(TomorrowSunrise.AddMinutes(-4));
        Assert.Empty(r.Notifier.HeadsUps);
    }

    [Fact]
    public void Moved_transition_is_announced_once()
    {
        var r = new Rig(Noon, Theme.Light);
        r.Weather.Now = Cloud(60);
        var first = r.Engine.Start().Schedule.Next!.At;
        r.TickAt(first.AddMinutes(-4));
        r.Weather.Now = Cloud(64);
        var moved = r.TickAt(first.AddMinutes(-3.5)).Schedule.Next!.At;
        Assert.NotEqual(first, moved);
        Assert.True(moved > first.AddMinutes(-3.5));
        Assert.Single(r.Notifier.HeadsUps);
    }

    [Fact]
    public void Next_due_includes_window_edge_and_room_pending()
    {
        var r = new Rig(Noon, Theme.Light).WithSensor(null);
        r.Room.Pending = (Theme.Dark, Noon.AddMinutes(2));
        r.Engine.Start();
        Assert.Equal(Noon.AddMinutes(2), r.Engine.NextDue);
        r.Room.Pending = null;
        r.TickAt(Noon);
        Assert.Equal(Sunset.AddMinutes(-60), r.Engine.NextDue);
    }
}
