using WinSolar.Core.Decisions;
using WinSolar.Core.Desktop;
using WinSolar.Core.Power;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Wallpaper;

namespace WinSolar.Core.Settings;

/// <summary>User settings. Defaults follow the spec's first run: every feature on.</summary>
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

    // Plan 2 (missing in older files: these defaults apply)
    public bool FollowRealLight { get; init; } = true;
    public int MaxShiftMin { get; init; } = 60;
    public BatteryRule Battery { get; init; } = BatteryRule.DarkBelow20;

    // Plan 3
    public Profile Day { get; init; } = new("#0067c0", Pointer.White, Sounds.Normal);
    public Profile Night { get; init; } = new("#6b69d6", Pointer.Black, Sounds.Normal);

    /// <summary>"Pointer and sounds follow the profile"; off keeps the Day profile's pointer and sounds.</summary>
    public bool ProfilesFollow { get; init; } = true;

    // Plan 4
    public WallpaperMode Wallpaper { get; init; } = WallpaperMode.Sun;

    /// <summary>Folder name of the imported set shown in sun mode; null until one is imported.</summary>
    public string? ActiveSet { get; init; }

    public int Schema { get; init; } = 3;

    /// <summary>How far real light may move a switch: 30, 60 or 90 minutes.</summary>
    public TimeSpan MaxShift => TimeSpan.FromMinutes(Math.Clamp(MaxShiftMin, 30, 90));

    /// <summary>
    /// First run: every feature on. With a light sensor the room decides the theme (Celal, 2026-11-27); without one
    /// the sun and the weather do.
    /// </summary>
    public static AppSettings FirstRun(bool hasSensor) =>
        new() { Source = hasSensor ? ScheduleSource.RoomLight : ScheduleSource.Sun };

    public ScheduleOptions ToScheduleOptions() =>
        new(Source == ScheduleSource.RoomLight ? ScheduleSource.Sun : Source, OffsetSunriseMin, OffsetSunsetMin, FixedLight, FixedDark);
}

/// <summary>What must survive a restart: mode, manual end, hold, and the last theme Win Solar applied.</summary>
public sealed record AppState(Mode Mode = Mode.Auto, DateTimeOffset? ManualUntil = null, Hold? Hold = null, Theme? LastApplied = null);
