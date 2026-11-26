using WinSolar.Core.Scheduling;

namespace WinSolar.Core.Decisions;

/// <summary>Builds holds with the spec's lengths.</summary>
public static class Holds
{
    public static readonly TimeSpan SnoozeLength = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SkipFallback = TimeSpan.FromHours(12);

    /// <summary>Keep the current theme until the next switch + 15 min.</summary>
    public static Hold? Snooze(Schedule s, Theme current) =>
        s.Next is null ? null : new Hold(current, s.Next.At + SnoozeLength, HoldKind.Snooze);

    /// <summary>Keep the current theme until the switch after next (or next + 12 h).</summary>
    public static Hold? Skip(Schedule s, Theme current) =>
        s.Next is null ? null : new Hold(current, s.After?.At ?? s.Next.At + SkipFallback, HoldKind.Skip);

    public static Hold Pause(DateTimeOffset now, Theme current, TimeSpan length) => new(current, now + length, HoldKind.Pause);

    /// <summary>Take the next switch's theme now; the hold ends exactly when the schedule agrees.</summary>
    public static Hold? SwitchNow(Schedule s) => s.Next is null ? null : new Hold(s.Next.Theme, s.Next.At, HoldKind.SwitchNow);

    /// <summary>When a manual mode should end by itself (the next switch), or null when it should last.</summary>
    public static DateTimeOffset? ManualUntil(Schedule s, bool endsByItself) => endsByItself ? s.Next?.At : null;
}
