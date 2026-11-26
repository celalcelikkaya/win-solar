using WinSolar.Core.Light;

namespace WinSolar.Tests.Light;

public class CalibrationTests
{
    private static readonly DateTimeOffset H = new(2026, 11, 26, 18, 0, 0, TimeSpan.Zero);

    private static Calibration Of(double day, double evening, int hours) =>
        new(Enumerable.Repeat(day, hours).ToList(), Enumerable.Repeat(evening, hours).ToList());

    [Fact]
    public void Learned_thresholds_sit_between_evening_and_day()
    {
        var t = CalibrationMath.For(Of(400, 120, 6));
        Assert.Equal(182.9, t.DarkBelow, 0.5);
        Assert.Equal(262.4, t.LightAbove, 0.5);
        Assert.True(CalibrationMath.IsLearned(Of(400, 120, 6)));
    }

    [Fact]
    public void Too_little_data_uses_defaults() =>
        Assert.Equal(CalibrationMath.Default, CalibrationMath.For(Of(400, 120, 5)));

    [Fact]
    public void Day_not_brighter_than_evening_uses_defaults() =>
        Assert.Equal(CalibrationMath.Default, CalibrationMath.For(Of(150, 120, 10)));

    [Fact]
    public void Defaults_are_60_and_150() => Assert.Equal(new Thresholds(60, 150), CalibrationMath.Default);

    [Fact]
    public void Learner_adds_hour_median_when_hour_ends()
    {
        var l = new CalibrationLearner(Calibration.Empty);
        Assert.False(l.Add(H.AddMinutes(1), 100, -10));
        Assert.False(l.Add(H.AddMinutes(20), 120, -10));
        Assert.False(l.Add(H.AddMinutes(40), 500, -10));
        Assert.True(l.Add(H.AddMinutes(61), 130, -12));
        Assert.Equal(new[] { 120.0 }, l.Current.Evening);
        Assert.Empty(l.Current.Day);
    }

    [Fact]
    public void Learner_ignores_twilight_and_dark_room()
    {
        var l = new CalibrationLearner(Calibration.Empty);
        for (var i = 0; i < 5; i++) l.Add(H.AddMinutes(i), 300, 0);
        for (var i = 5; i < 10; i++) l.Add(H.AddMinutes(i), 2, -20);
        Assert.False(l.Add(H.AddMinutes(70), 300, 20));
        Assert.Empty(l.Current.Day);
        Assert.Empty(l.Current.Evening);
    }

    [Fact]
    public void Learner_keeps_the_last_48_hours()
    {
        var l = new CalibrationLearner(Calibration.Empty);
        for (var h = 0; h < 60; h++)
            for (var i = 0; i < 3; i++) l.Add(H.AddHours(h).AddMinutes(i * 10), 300 + h, 30);
        l.Add(H.AddHours(61), 300, 30);
        Assert.Equal(48, l.Current.Day.Count);
        Assert.Equal(359, l.Current.Day[^1]);
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var l = new CalibrationLearner(Of(400, 120, 6));
        l.Reset();
        Assert.Equal(Calibration.Empty.Day, l.Current.Day);
        Assert.Empty(l.Current.Evening);
    }

    [Fact]
    public void Learned_state_round_trips_through_the_json_store()
    {
        var store = new WinSolar.Core.Settings.JsonFileStore(Directory.CreateTempSubdirectory().FullName);
        store.Save("learned.json", new WinSolar.Core.Settings.LearnedState(Of(400, 120, 6)));
        var back = store.Load("learned.json", () => WinSolar.Core.Settings.LearnedState.Empty);
        Assert.Equal(Of(400, 120, 6).Day, back.Calibration.Day);
        Assert.Equal(6, back.Calibration.Evening.Count);
    }
}
