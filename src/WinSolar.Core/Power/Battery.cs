namespace WinSolar.Core.Power;

public enum BatteryRule { NoChange, DarkBelow20, AlwaysDark }

/// <summary>Power source and charge. <paramref name="Percent"/> is null when there is no battery.</summary>
public sealed record PowerStatus(bool OnBattery, int? Percent)
{
    public static PowerStatus Mains { get; } = new(false, null);
}

public static class BatteryRules
{
    /// <summary>Whether battery mode wants Dark now. "Below 20%" includes 20%, as in the demo.</summary>
    public static bool IsDark(BatteryRule rule, PowerStatus power) => rule switch
    {
        BatteryRule.AlwaysDark => power.OnBattery,
        BatteryRule.DarkBelow20 => power.OnBattery && power.Percent <= 20,
        _ => false,
    };
}
