using WinSolar.Core.Decisions;
using WinSolar.Core.Light;
using WinSolar.Core.Power;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;

namespace WinSolar.Core.Engine;

public interface IClock
{
    DateTimeOffset Now { get; }
}

/// <summary>Reads and writes the Windows theme.</summary>
public interface IThemeApplier
{
    Theme Read();
    void Apply(Theme theme);
}

public interface INotifier
{
    void HeadsUp(Transition next, int minutesLeft);
    void DismissHeadsUp();
    void Switched(Theme theme, Cause cause, Transition? next);
}

public interface IFullScreenProbe
{
    bool IsFullScreen();
}

public interface IStateStore
{
    AppSettings Settings { get; }
    AppState State { get; }
    void Save(AppState state);
}

/// <summary>Mains or battery, and the charge.</summary>
public interface IPower
{
    PowerStatus Read();
}

/// <summary>The ambient light sensor, already smoothed (hysteresis + 5-second stability).</summary>
public interface IRoomLight
{
    bool Available { get; }
    double? Lux { get; }
    (Theme Theme, DateTimeOffset At)? Pending { get; }
    Theme? Current(DateTimeOffset now);
}

/// <summary>Hourly weather for the place; null when unavailable (offline, stale).</summary>
public interface IWeather
{
    HourWeather? At(DateTimeOffset t);
}

public enum LightSource { Off, Sensor, Weather, WeatherUnavailable }

/// <summary>What real light is doing now, for the flyout.</summary>
public sealed record LightInfo(LightSource Source, double? Lux, HourWeather? Weather, (Theme Theme, DateTimeOffset At)? Pending);

/// <summary>Everything the UI needs to draw the current moment.</summary>
public sealed record EngineSnapshot(
    Schedule Schedule,
    Mode Mode,
    DateTimeOffset? ManualUntil,
    Hold? Hold,
    Theme Applied,
    bool WaitingForFullScreen,
    Transition? HeadsUp,
    Place Place,
    string? Error,
    LightInfo Light,
    PowerStatus Power,
    bool BatteryDark);
