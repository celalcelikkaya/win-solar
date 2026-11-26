using WinSolar.Core.Decisions;
using WinSolar.Core.Light;
using WinSolar.Core.Power;
using WinSolar.Core.Presentation;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;

namespace WinSolar.Core.Engine;

/// <summary>
/// Decides the theme, waits for full screen, applies it, raises the heads-up and keeps state. Talks to Windows only
/// through the ports, so every rule here is unit-tested.
/// </summary>
public sealed class SolarEngine
{
    public static readonly TimeSpan HeadsUpLead = TimeSpan.FromMinutes(5);

    private readonly IClock clock;
    private readonly IThemeApplier theme;
    private readonly INotifier notifier;
    private readonly IFullScreenProbe fullScreen;
    private readonly IStateStore store;
    private readonly IPower power;
    private readonly IRoomLight room;
    private readonly IWeather weather;

    private AppState state;
    private AppState savedState;
    private Theme applied;
    private bool waiting;
    private Transition? headsUp;
    private (DateOnly Date, TransitionKind Kind)? headsUpShownFor;
    private bool outsideChange; // Windows was changed behind our back: keep it until the next switch passes
    private (DateOnly Date, TransitionKind Kind)? outsideSince;
    private (Mode Mode, Hold? Hold) outsideUnder; // what the user had asked Win Solar for when Windows was changed
    private bool userAction;
    private string? error;

    public SolarEngine(IClock clock, IThemeApplier theme, INotifier notifier, IFullScreenProbe fullScreen, IStateStore store, Place fallbackPlace,
        IPower power, IRoomLight room, IWeather weather)
    {
        this.clock = clock;
        this.theme = theme;
        this.notifier = notifier;
        this.fullScreen = fullScreen;
        this.store = store;
        FallbackPlace = fallbackPlace;
        this.power = power;
        this.room = room;
        this.weather = weather;
        state = savedState = store.State;
    }

    public DateTimeOffset NextDue { get; private set; }

    /// <summary>The place used until the user picks one; refreshed when the PC time zone changes.</summary>
    public Place FallbackPlace { get; set; }

    public event Action<EngineSnapshot>? Changed;

    private AppSettings Settings => store.Settings;
    private Place Place => Settings.Place ?? FallbackPlace;

    /// <summary>First run after launch: never notifies, and changes nothing when the theme is already right.</summary>
    public EngineSnapshot Start()
    {
        applied = theme.Read();
        return Evaluate(starting: true);
    }

    public EngineSnapshot Tick() => Evaluate(starting: false);

    public void SetMode(Mode mode)
    {
        var s = Recompute(clock.Now);
        state = state with
        {
            Mode = mode,
            Hold = null,
            ManualUntil = mode == Mode.Auto ? null : Holds.ManualUntil(s, Settings.ManualEndsByItself),
        };
        UserTick();
    }

    public void Snooze() => SetHold(s => Holds.Snooze(s, applied));

    public void Skip() => SetHold(s => Holds.Skip(s, applied));

    public void SwitchNow() => SetHold(Holds.SwitchNow);

    public void Pause(TimeSpan length) => SetHold(_ => Holds.Pause(clock.Now, applied, length));

    public void Resume()
    {
        state = state with { Hold = null };
        UserTick();
    }

    /// <summary>A tick caused by something the user just did: any switch it makes is quiet.</summary>
    private void UserTick()
    {
        userAction = true;
        try { Tick(); }
        finally { userAction = false; }
    }

    /// <summary>Win + Shift + D: flip now as a manual change, or go back to Auto when the flip matches the schedule.</summary>
    public (Theme Theme, string Detail) Shortcut()
    {
        var now = clock.Now;
        applied = theme.Read();
        ClearExpired(now);
        var s = Recompute(now);
        ClearCaughtUp(s);
        var target = applied == Theme.Light ? Theme.Dark : Theme.Light;
        string detail;
        if (state.Mode != Mode.Auto && target == s.Theme)
        {
            state = state with { Mode = Mode.Auto, ManualUntil = null, Hold = null };
            detail = "Back to Auto";
        }
        else
        {
            var until = Holds.ManualUntil(s, Settings.ManualEndsByItself);
            state = state with { Mode = target == Theme.Light ? Mode.Light : Mode.Dark, ManualUntil = until, Hold = null };
            detail = until is { } u ? $"Auto again at {Format.When(u, now, Place.TimeZoneId, Settings.Hour12)}" : "Until you change it";
        }

        Switch(target, Cause.Shortcut);
        Tick();
        return (target, detail);
    }

    private void SetHold(Func<Schedule, Hold?> make)
    {
        if (state.Mode != Mode.Auto) return;
        var hold = make(Recompute(clock.Now));
        if (hold is null) return;
        state = state with { Hold = hold };
        Tick();
    }

    private EngineSnapshot Evaluate(bool starting)
    {
        var now = clock.Now;
        ClearExpired(now);
        var s = Recompute(now);
        ClearCaughtUp(s);
        var source = Source(now);
        var powerNow = power.Read();
        var batteryDark = BatteryRules.IsDark(Settings.Battery, powerNow);
        var inWindow = LightModel.InWindow(now, s.SunEvents, Settings.MaxShift);
        var roomTheme = source == LightSource.Sensor ? room.Current(now) : null;
        var decision = ThemeDecider.Decide(new DecisionInput(state.Mode, state.Hold, batteryDark, roomTheme, inWindow, s));
        var isFullScreen = fullScreen.IsFullScreen();

        // Windows changed behind our back: respect it until the decision itself changes.
        var onScreen = theme.Read();
        var prevId = Id(s.Prev);
        if (!starting && onScreen != applied)
        {
            applied = onScreen;
            outsideChange = true;
            outsideSince = prevId;
            outsideUnder = (state.Mode, state.Hold);
        }

        // It lasts until the user asks Win Solar for something else (mode, hold) or, in plain Auto, the next switch passes.
        if (outsideChange && (outsideUnder != (state.Mode, state.Hold)
                              || (state.Mode == Mode.Auto && state.Hold is null && outsideSince != prevId)))
            outsideChange = false;

        if (decision.Theme != applied && !outsideChange)
        {
            if (ThemeDecider.ShouldDefer(decision.Cause, isFullScreen, Settings.WaitForFullScreen))
            {
                waiting = true;
            }
            else
            {
                var cause = starting ? Cause.Start : userAction ? Cause.Manual : waiting ? Cause.FullScreenEnded : decision.Cause;
                waiting = false;
                Switch(decision.Theme, cause);
            }
        }
        else
        {
            waiting = false;
        }

        UpdateHeadsUp(now, s, isFullScreen || source == LightSource.Sensor || batteryDark);

        if (state != savedState)
        {
            try
            {
                store.Save(state);
                savedState = state;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Kept in memory; saved again on the next tick.
            }
        }

        NextDue = global::WinSolar.Core.Engine.NextDue.Compute(now, s, state.Hold, state.ManualUntil, HeadsUpLead, ExtraDue(source, s));
        var snapshot = new EngineSnapshot(s, state.Mode, state.ManualUntil, state.Hold, applied, waiting, headsUp, Place, error,
            new LightInfo(
                source,
                source == LightSource.Sensor ? room.Lux : null,
                source == LightSource.Weather ? weather.At(now) : null,
                source == LightSource.Sensor ? room.Pending : null,
                inWindow),
            powerNow,
            batteryDark);
        Changed?.Invoke(snapshot);
        return snapshot;
    }

    private void Switch(Theme target, Cause cause)
    {
        try
        {
            theme.Apply(target);
            error = null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidOperationException)
        {
            error = "Could not change the Windows theme";
            return;
        }

        applied = target;
        state = state with { LastApplied = target };
        if (headsUp is not null)
        {
            notifier.DismissHeadsUp();
            headsUp = null;
        }

        var quiet = cause is Cause.Manual or Cause.Shortcut or Cause.SwitchNow or Cause.Start;
        if (!quiet && Settings.NotifyAfterSwitch)
            notifier.Switched(target, cause, Recompute(clock.Now).Next);
    }

    /// <param name="quiet">No heads-up: full screen, sensor mode (the time is not known in advance) or battery mode.</param>
    private void UpdateHeadsUp(DateTimeOffset now, Schedule s, bool quiet)
    {
        var next = s.Next;
        var eligible = Settings.HeadsUp
            && state.Mode == Mode.Auto
            && state.Hold is null
            && !quiet
            && next is not null
            && next.Theme != applied
            && next.At > now
            && next.At - now <= HeadsUpLead;

        if (eligible)
        {
            var id = Id(next!);
            if (headsUpShownFor != id)
            {
                headsUpShownFor = id;
                headsUp = next!;
                notifier.HeadsUp(next!, (int)Math.Ceiling((next!.At - now).TotalMinutes));
            }
        }
        else if (headsUp is not null)
        {
            notifier.DismissHeadsUp();
            headsUp = null;
        }
    }

    private void ClearExpired(DateTimeOffset now)
    {
        if (state.Mode != Mode.Auto && state.ManualUntil is { } until && until <= now)
            state = state with { Mode = Mode.Auto, ManualUntil = null };
        if (state.Hold is { } hold && (hold.Until <= now || state.Mode != Mode.Auto))
            state = state with { Hold = null };
    }

    /// <summary>A transition's identity that survives real light moving it by minutes: local date + kind.</summary>
    private (DateOnly Date, TransitionKind Kind)? Id(Transition? t) =>
        t is null ? null : (Zones.LocalDate(t.At, Zones.Find(Place.TimeZoneId)), t.Kind);

    /// <summary>Switch now ends once the schedule reaches the theme it took early.</summary>
    private void ClearCaughtUp(Schedule s)
    {
        if (state.Hold is { Kind: HoldKind.SwitchNow } hold && s.Theme == hold.Theme)
            state = state with { Hold = null };
    }

    private Schedule Recompute(DateTimeOffset now)
    {
        Func<DateTimeOffset, TransitionKind, DateTimeOffset>? adjust = null;
        if (Source(now) == LightSource.Weather)
        {
            var place = Place;
            var maxShift = Settings.MaxShift;
            adjust = (t, kind) => LightModel.Adjust(t, kind, place,
                at => weather.At(at) is { } w ? LightModel.WeatherFactor(w) : 1, maxShift);
        }

        return ScheduleCalculator.Compute(now, Place, Settings.ToScheduleOptions(), adjust);
    }

    /// <summary>Spec: the sensor decides when there is one; otherwise the weather moves the sun times.</summary>
    private LightSource Source(DateTimeOffset now) =>
        !Settings.FollowRealLight || Settings.Source != ScheduleSource.Sun ? LightSource.Off
        : room.Available ? LightSource.Sensor
        : weather.At(now) is not null ? LightSource.Weather
        : LightSource.WeatherUnavailable;

    /// <summary>Extra moments to look again: a room change about to count, and the light window's edges.</summary>
    private IEnumerable<DateTimeOffset> ExtraDue(LightSource source, Schedule s)
    {
        if (source == LightSource.Off) return [];
        var edges = LightModel.WindowEdges(s.SunEvents, Settings.MaxShift);
        return source == LightSource.Sensor && room.Pending is { } p ? edges.Append(p.At) : edges;
    }
}
