using WinSolar.Core.Decisions;
using WinSolar.Core.Scheduling;

namespace WinSolar.Core.Settings;

/// <summary>User settings (Plan 1 fields). Defaults follow the spec's first run: every feature on.</summary>
public sealed record AppSettings
{
    public Place? Place { get; init; }
    public ScheduleSource Source { get; init; } = ScheduleSource.Sun;
    public int OffsetSunriseMin { get; init; }
    public int OffsetSunsetMin { get; init; }
    public TimeOnly FixedLight { get; init; } = new(7, 0);
    public TimeOnly FixedDark { get; init; } = new(19, 0);
    public bool HeadsUp { get; init; } = true;
    public bool WaitForFullScreen { get; init; } = true;
    public bool ManualEndsByItself { get; init; } = true;
    public bool Shortcut { get; init; } = true;
    public bool NotifyAfterSwitch { get; init; } = true;
    public bool StartWithWindows { get; init; } = true;
    public bool HideOnClickOutside { get; init; } = true;
    public bool Hour12 { get; init; }
    public int Schema { get; init; } = 3;

    public ScheduleOptions ToScheduleOptions() => new(Source, OffsetSunriseMin, OffsetSunsetMin, FixedLight, FixedDark);
}

/// <summary>What must survive a restart: mode, manual end, hold, and the last theme Win Solar applied.</summary>
public sealed record AppState(Mode Mode = Mode.Auto, DateTimeOffset? ManualUntil = null, Hold? Hold = null, Theme? LastApplied = null);
