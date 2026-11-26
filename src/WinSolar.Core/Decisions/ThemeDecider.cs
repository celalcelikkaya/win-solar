using WinSolar.Core.Scheduling;

namespace WinSolar.Core.Decisions;

/// <summary>Spec priority: Manual &gt; Battery &gt; Hold &gt; Room (inside the light window) &gt; Schedule.</summary>
public static class ThemeDecider
{
    public static Decision Decide(DecisionInput i)
    {
        if (i.Mode != Mode.Auto) return new Decision(i.Mode == Mode.Light ? Theme.Light : Theme.Dark, Cause.Manual);
        if (i.BatteryDark) return new Decision(Theme.Dark, Cause.Battery);
        if (i.Hold is { } hold) return new Decision(hold.Theme, hold.Kind == HoldKind.SwitchNow ? Cause.SwitchNow : Cause.Hold);
        if (i.RoomTheme is { } room && i.InLightWindow) return new Decision(room, Cause.Room);

        var cause = i.Schedule.Prev?.Kind switch
        {
            TransitionKind.Sunrise => Cause.Sunrise,
            TransitionKind.Sunset => Cause.Sunset,
            TransitionKind.Fixed => Cause.Fixed,
            _ => Cause.Polar,
        };
        return new Decision(i.Schedule.Theme, cause);
    }

    /// <summary>Automatic switches wait for full screen to end; things the user just did never wait.</summary>
    public static bool ShouldDefer(Cause cause, bool fullScreen, bool waitForFullScreen) =>
        fullScreen && waitForFullScreen && cause is not (Cause.Manual or Cause.Shortcut or Cause.SwitchNow);
}
