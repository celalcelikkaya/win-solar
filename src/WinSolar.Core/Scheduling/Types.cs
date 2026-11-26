using WinSolar.Core.Sun;

namespace WinSolar.Core.Scheduling;

public enum Theme { Light, Dark }

public enum TransitionKind { Sunrise, Sunset, Fixed }

public enum Phase { Night, Dawn, Day, Dusk }

/// <summary>What Auto follows. <see cref="RoomLight"/> uses the light sensor at any hour (sun times when the room is not
/// known yet or there is no sensor).</summary>
public enum ScheduleSource { Sun, Fixed, RoomLight }

/// <summary>A planned switch. <paramref name="ShiftMinutes"/> is how far weather/real light moved it from the sun event.</summary>
public sealed record Transition(DateTimeOffset At, Theme Theme, TransitionKind Kind, int ShiftMinutes = 0);

public sealed record ScheduleOptions(ScheduleSource Source, int OffsetSunriseMin, int OffsetSunsetMin, TimeOnly FixedLight, TimeOnly FixedDark);

/// <summary>Where "now" sits between switches. Times are UTC instants; render them in the place's zone.</summary>
public sealed record Schedule(
    Transition? Prev,
    Transition? Next,
    Transition? After,
    Theme Theme,
    Phase Phase,
    double Progress,
    SunDay Today,
    Transition? TodayLight,
    Transition? TodayDark,
    IReadOnlyList<DateTimeOffset> SunEvents);
