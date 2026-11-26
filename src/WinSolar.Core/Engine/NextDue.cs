using WinSolar.Core.Decisions;
using WinSolar.Core.Scheduling;

namespace WinSolar.Core.Engine;

/// <summary>When the engine must look again. One timer, no polling.</summary>
public static class NextDue
{
    /// <summary>Longest sleep between checks (also covers polar days and the date rolling over).</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromHours(6);

    public static DateTimeOffset Compute(DateTimeOffset now, Schedule s, Hold? hold, DateTimeOffset? manualUntil, TimeSpan headsUpLead,
        IEnumerable<DateTimeOffset> extra)
    {
        var due = now + MaxWait;
        void Consider(DateTimeOffset? t)
        {
            if (t is { } v && v > now && v < due) due = v;
        }

        Consider(s.Next?.At - headsUpLead);
        Consider(s.Next?.At);
        Consider(hold?.Until);
        Consider(manualUntil);
        foreach (var t in extra) Consider(t);
        return due;
    }
}
