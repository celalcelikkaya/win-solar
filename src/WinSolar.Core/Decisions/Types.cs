using WinSolar.Core.Scheduling;

namespace WinSolar.Core.Decisions;

public enum Mode { Auto, Light, Dark }

public enum HoldKind { Snooze, Skip, Pause, SwitchNow }

/// <summary>Keeps <paramref name="Theme"/> until <paramref name="Until"/> while in Auto. Saved to disk.</summary>
public sealed record Hold(Theme Theme, DateTimeOffset Until, HoldKind Kind);

public enum Cause { Manual, Battery, Hold, SwitchNow, Room, Sunrise, Sunset, Fixed, Polar, Start, FullScreenEnded, Shortcut }

public sealed record DecisionInput(Mode Mode, Hold? Hold, bool BatteryDark, Theme? RoomTheme, bool InLightWindow, Schedule Schedule);

public sealed record Decision(Theme Theme, Cause Cause);
