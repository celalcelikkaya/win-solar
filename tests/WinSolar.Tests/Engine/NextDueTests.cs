using WinSolar.Core.Decisions;
using WinSolar.Core.Engine;
using WinSolar.Core.Scheduling;
using static WinSolar.Tests.Decisions.ThemeDeciderTests;

namespace WinSolar.Tests.Engine;

public class NextDueTests
{
    private static readonly TimeSpan Lead = TimeSpan.FromMinutes(5);

    [Fact]
    public void Wakes_for_the_heads_up_first()
    {
        var s = LightSchedule;
        var now = s.Next!.At.AddHours(-1);
        Assert.Equal(s.Next.At - Lead, NextDue.Compute(now, s, null, null, Lead));
    }

    [Fact]
    public void Wakes_at_the_switch_after_the_heads_up()
    {
        var s = LightSchedule;
        var now = s.Next!.At.AddMinutes(-3);
        Assert.Equal(s.Next.At, NextDue.Compute(now, s, null, null, Lead));
    }

    [Fact]
    public void Hold_end_counts()
    {
        var s = LightSchedule;
        var now = s.Next!.At.AddHours(-3);
        var hold = new Hold(Theme.Light, now.AddMinutes(10), HoldKind.Pause);
        Assert.Equal(now.AddMinutes(10), NextDue.Compute(now, s, hold, null, Lead));
    }

    [Fact]
    public void Never_in_the_past_and_at_most_six_hours()
    {
        var s = LightSchedule with { Next = null };
        var now = DateTimeOffset.UnixEpoch.AddYears(56);
        var due = NextDue.Compute(now, s, null, now.AddHours(-1), Lead);
        Assert.Equal(now + NextDue.MaxWait, due);
    }
}
