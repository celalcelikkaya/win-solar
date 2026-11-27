using WinSolar.Core.Light;
using WinSolar.Core.Scheduling;

namespace WinSolar.Tests.Light;

public class RoomLightTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 26, 15, 0, 0, TimeSpan.Zero);
    private static readonly Thresholds Th = new(60, 150);

    private static RoomLightTracker StableAt(Theme theme)
    {
        var r = new RoomLightTracker(Th);
        r.Feed(T0.AddMinutes(-10), theme == Theme.Dark ? 20 : 400);
        r.Stable(T0.AddMinutes(-5));
        return r;
    }

    [Fact]
    public void Dark_counts_after_5_seconds()
    {
        var r = new RoomLightTracker(Th);
        r.Feed(T0, 40);
        Assert.Null(r.Stable(T0.AddSeconds(4)));
        Assert.Equal((Theme.Dark, T0.AddSeconds(5)), r.Pending);
        Assert.Equal(Theme.Dark, r.Stable(T0.AddSeconds(5)));
        Assert.Null(r.Pending);
        Assert.Equal(40, r.Lux);
    }

    [Fact]
    public void Short_dip_does_not_count()
    {
        var r = StableAt(Theme.Light);
        r.Feed(T0, 30);
        r.Feed(T0.AddSeconds(3), 300);
        Assert.Equal(Theme.Light, r.Stable(T0.AddMinutes(5)));
        Assert.Null(r.Pending);
    }

    [Fact]
    public void Short_dip_back_to_the_middle_does_not_count()
    {
        var r = StableAt(Theme.Light);
        r.Feed(T0, 10);
        r.Feed(T0.AddSeconds(5), 100);
        Assert.Equal(Theme.Light, r.Stable(T0.AddMinutes(3)));
        Assert.Null(r.Pending);
    }

    [Fact]
    public void Between_thresholds_keeps_the_current_theme()
    {
        var r = StableAt(Theme.Dark);
        for (var m = 0; m <= 10; m++) r.Feed(T0.AddMinutes(m), 100);
        Assert.Equal(Theme.Dark, r.Stable(T0.AddMinutes(10)));
        Assert.Null(r.Pending);
    }

    [Fact]
    public void Steady_readings_do_not_restart_the_wait()
    {
        var r = StableAt(Theme.Light);
        r.Feed(T0, 30);
        r.Feed(T0.AddSeconds(3), 35);
        Assert.Equal((Theme.Dark, T0.AddSeconds(5)), r.Pending);
        Assert.Equal(Theme.Dark, r.Stable(T0.AddSeconds(5)));
    }
}
