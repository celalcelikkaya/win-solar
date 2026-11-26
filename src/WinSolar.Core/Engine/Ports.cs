using WinSolar.Core.Decisions;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;

namespace WinSolar.Core.Engine;

public interface IClock
{
    DateTimeOffset Now { get; }
}

/// <summary>Reads and writes the Windows theme.</summary>
public interface IThemeApplier
{
    Theme Read();
    void Apply(Theme theme);
}

public interface INotifier
{
    void HeadsUp(Transition next, int minutesLeft);
    void DismissHeadsUp();
    void Switched(Theme theme, Cause cause, Transition? next);
}

public interface IFullScreenProbe
{
    bool IsFullScreen();
}

public interface IStateStore
{
    AppSettings Settings { get; }
    AppState State { get; }
    void Save(AppState state);
}

/// <summary>Everything the UI needs to draw the current moment.</summary>
public sealed record EngineSnapshot(
    Schedule Schedule,
    Mode Mode,
    DateTimeOffset? ManualUntil,
    Hold? Hold,
    Theme Applied,
    bool WaitingForFullScreen,
    Transition? HeadsUp,
    Place Place,
    string? Error = null);
