using WinSolar.Core.Power;

namespace WinSolar.Tests.Power;

public class BatteryTests
{
    [Theory]
    [InlineData(BatteryRule.DarkBelow20, true, 20, true)]
    [InlineData(BatteryRule.DarkBelow20, true, 21, false)]
    [InlineData(BatteryRule.DarkBelow20, false, 5, false)]
    [InlineData(BatteryRule.AlwaysDark, true, 90, true)]
    [InlineData(BatteryRule.AlwaysDark, false, 90, false)]
    [InlineData(BatteryRule.NoChange, true, 3, false)]
    public void IsDark(BatteryRule rule, bool onBattery, int percent, bool dark) =>
        Assert.Equal(dark, BatteryRules.IsDark(rule, new PowerStatus(onBattery, percent)));

    [Fact]
    public void Unknown_percent_is_not_low() =>
        Assert.False(BatteryRules.IsDark(BatteryRule.DarkBelow20, new PowerStatus(true, null)));
}
