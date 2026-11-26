using WinSolar.Core.Decisions;
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
    private readonly Place fallbackPlace;

    private AppState state;
    private AppState savedState;
    private Theme applied;
    private bool waiting;
    private Transition? headsUp;
    private DateTimeOffset? headsUpShownFor;

    public SolarEngine(IClock clock, IThemeApplier theme, INotifier notifier, IFullScreenProbe fullScreen, IStateStore store, Place fallbackPlace)
    {
        this.clock = clock;
        this.theme = theme;
        this.notifier = notifier;
        this.fullScreen = fullScreen;
        this.store = store;
        this.fallbackPlace = fallbackPlace;
        state = savedState = store.State;
    }

    public DateTimeOffset NextDue { get; private set; }

    public event Action<EngineSnapshot>? Changed;

    private AppSettings Settings => store.Settings;
    private Place Place => Settings.Place ?? fallbackPlace;

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
        Tick();
    }

    public void Snooze() => SetHold(s => Holds.Snooze(s, applied));

    public void Skip() => SetHold(s => Holds.Skip(s, applied));

    public void SwitchNow() => SetHold(Holds.SwitchNow);

    public void Pause(TimeSpan length) => SetHold(_ => Holds.Pause(clock.Now, applied, length));

    public void Resume()
    {
        state = state with { Hold = null };
        Tick();
    }

    /// <summary>Win + Shift + D: flip now as a manual change, or go back to Auto when the flip matches the schedule.</summary>
    public (Theme Theme, string Detail) Shortcut()
    {
        var now = clock.Now;
        ClearExpired(now);
        var s = Recompute(now);
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
        var decision = ThemeDecider.Decide(new DecisionInput(state.Mode, state.Hold, false, null, false, s));
        var isFullScreen = fullScreen.IsFullScreen();

        if (decision.Theme != applied)
        {
            if (ThemeDecider.ShouldDefer(decision.Cause, isFullScreen, Settings.WaitForFullScreen))
            {
                waiting = true;
            }
            else
            {
                var cause = starting ? Cause.Start : waiting ? Cause.FullScreenEnded : decision.Cause;
                waiting = false;
                Switch(decision.Theme, cause);
            }
        }
        else
        {
            waiting = false;
        }

        UpdateHeadsUp(now, s, isFullScreen);

        if (state != savedState)
        {
            store.Save(state);
            savedState = state;
        }

        NextDue = global::WinSolar.Core.Engine.NextDue.Compute(now, s, state.Hold, state.ManualUntil, HeadsUpLead);
        var snapshot = new EngineSnapshot(s, state.Mode, state.ManualUntil, state.Hold, applied, waiting, headsUp, Place);
        Changed?.Invoke(snapshot);
        return snapshot;
    }

    private void Switch(Theme target, Cause cause)
    {
        theme.Apply(target);
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

    private void UpdateHeadsUp(DateTimeOffset now, Schedule s, bool isFullScreen)
    {
        var next = s.Next;
        var eligible = Settings.HeadsUp
            && state.Mode == Mode.Auto
            && state.Hold is null
            && !isFullScreen
            && next is not null
            && next.Theme != applied
            && next.At > now
            && next.At - now <= HeadsUpLead;

        if (eligible)
        {
            if (headsUpShownFor != next!.At)
            {
                headsUpShownFor = next.At;
                headsUp = next;
                notifier.HeadsUp(next, (int)Math.Ceiling((next.At - now).TotalMinutes));
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

    private Schedule Recompute(DateTimeOffset now) =>
        ScheduleCalculator.Compute(now, Place, Settings.ToScheduleOptions());
}
