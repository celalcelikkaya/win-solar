using WinSolar.Core.Decisions;
using WinSolar.Core.Engine;
using WinSolar.Core.Light;
using WinSolar.Core.Power;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;

namespace WinSolar.Tests.Fakes;

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; set; } = now;
}

internal sealed class FakeTheme(Theme current) : IThemeApplier
{
    public Theme Current { get; private set; } = current;
    public int Applies { get; private set; }

    public bool FailApply { get; set; }

    public Theme Read() => Current;

    /// <summary>The user (or another app) changes the Windows theme behind the engine's back.</summary>
    public void SetOutside(Theme theme) => Current = theme;

    public void Apply(Theme theme)
    {
        if (FailApply) throw new UnauthorizedAccessException("registry");
        Current = theme;
        Applies++;
    }
}

internal sealed class FakeNotifier : INotifier
{
    public List<(Transition Next, int Minutes)> HeadsUps { get; } = [];
    public int Dismissed { get; private set; }
    public List<(Theme Theme, Cause Cause)> Switches { get; } = [];

    public void HeadsUp(Transition next, int minutesLeft) => HeadsUps.Add((next, minutesLeft));
    public void DismissHeadsUp() => Dismissed++;
    public void Switched(Theme theme, Cause cause, Transition? next) => Switches.Add((theme, cause));
}

internal sealed class FakeFullScreen : IFullScreenProbe
{
    public bool On { get; set; }
    public bool IsFullScreen() => On;
}

internal sealed class MemoryStore(AppSettings settings, AppState? state = null) : IStateStore
{
    public AppSettings Settings { get; set; } = settings;
    public AppState State { get; private set; } = state ?? new AppState();
    public int Saves { get; private set; }
    public int FailNextSaves { get; set; }

    public void Save(AppState value)
    {
        if (FailNextSaves > 0) { FailNextSaves--; throw new IOException("locked"); }
        State = value;
        Saves++;
    }
}

internal sealed class FakePower : IPower
{
    public PowerStatus Status { get; set; } = PowerStatus.Mains;
    public PowerStatus Read() => Status;
}

internal sealed class FakeRoom : IRoomLight
{
    public bool Available { get; set; }
    public double? Lux { get; set; }
    public Theme? Theme { get; set; }
    public (Theme Theme, DateTimeOffset At)? Pending { get; set; }
    public Theme? Current(DateTimeOffset now) => Theme;
}

internal sealed class FakeWeather : IWeather
{
    public HourWeather? Now { get; set; }
    public HourWeather? At(DateTimeOffset t) => Now is { } w ? w with { Hour = t } : null;
}
