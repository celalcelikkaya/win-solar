using WinSolar.Core.Sun;

namespace WinSolar.Core.Scheduling;

/// <summary>Port of v2's computeSchedule (src/lib/engine.ts), computed in the place's time zone.</summary>
public static class ScheduleCalculator
{
    private static readonly TimeSpan Near = TimeSpan.FromMinutes(40);

    /// <param name="adjust">Optional: moves an astronomical sunrise/sunset (used by real light in Plan 2).</param>
    public static Schedule Compute(DateTimeOffset now, Place place, ScheduleOptions opt,
        Func<DateTimeOffset, TransitionKind, DateTimeOffset>? adjust = null)
    {
        var zone = Zones.Find(place.TimeZoneId);
        var today = Zones.LocalDate(now, zone);
        var days = new List<SunDay>(4);
        var transitions = new List<Transition>(8);
        var events = new List<DateTimeOffset>(8);

        for (var n = -1; n <= 2; n++)
        {
            var date = today.AddDays(n);
            var sun = SunCalculator.ForDate(date, place.Lat, place.Lng);
            days.Add(sun);
            if (sun.Sunrise is { } rise) events.Add(rise);
            if (sun.Sunset is { } set) events.Add(set);

            if (opt.Source == ScheduleSource.Sun)
            {
                if (sun.Sunrise is { } r) transitions.Add(Make(r, TransitionKind.Sunrise, Theme.Light, opt.OffsetSunriseMin, adjust));
                if (sun.Sunset is { } s) transitions.Add(Make(s, TransitionKind.Sunset, Theme.Dark, opt.OffsetSunsetMin, adjust));
            }
            else
            {
                transitions.Add(new Transition(Zones.ToInstant(date, opt.FixedLight, zone), Theme.Light, TransitionKind.Fixed));
                transitions.Add(new Transition(Zones.ToInstant(date, opt.FixedDark, zone), Theme.Dark, TransitionKind.Fixed));
            }
        }

        transitions.Sort((a, b) => a.At.CompareTo(b.At));
        Transition? prev = null, next = null, after = null;
        foreach (var t in transitions)
        {
            if (t.At <= now) prev = t;
            else if (next is null) next = t;
            else { after = t; break; }
        }

        var todaySun = days[1];
        var theme = prev?.Theme ?? (todaySun.Polar == PolarState.Day ? Theme.Light : Theme.Dark);
        var progress = prev is not null && next is not null
            ? (now - prev.At).TotalMilliseconds / (next.At - prev.At).TotalMilliseconds
            : 0.5;

        var phase = theme == Theme.Light ? Phase.Day : Phase.Night;
        var near = IsNear(next, now) ? next : IsNear(prev, now) ? prev : null;
        if (near is not null) phase = near.Theme == Theme.Light ? Phase.Dawn : Phase.Dusk;

        var todayLight = transitions.FirstOrDefault(t => t.Theme == Theme.Light && Zones.LocalDate(t.At, zone) == today);
        var todayDark = transitions.FirstOrDefault(t => t.Theme == Theme.Dark && Zones.LocalDate(t.At, zone) == today);

        return new Schedule(prev, next, after, theme, phase, Math.Clamp(progress, 0, 1), todaySun, todayLight, todayDark, events);
    }

    private static Transition Make(DateTimeOffset sunEvent, TransitionKind kind, Theme theme, int offsetMin,
        Func<DateTimeOffset, TransitionKind, DateTimeOffset>? adjust)
    {
        var moved = adjust?.Invoke(sunEvent, kind) ?? sunEvent;
        var shift = (int)Math.Round((moved - sunEvent).TotalMinutes);
        return new Transition(moved.AddMinutes(offsetMin), theme, kind, shift);
    }

    private static bool IsNear(Transition? t, DateTimeOffset now) => t is not null && (t.At - now).Duration() <= Near;
}
