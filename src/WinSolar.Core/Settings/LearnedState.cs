using WinSolar.Core.Light;

namespace WinSolar.Core.Settings;

/// <summary>What Win Solar learns about this PC (learned.json), owned by the App services, never by the engine.</summary>
public sealed record LearnedState(Calibration Calibration)
{
    public static LearnedState Empty { get; } = new(Calibration.Empty);
}
