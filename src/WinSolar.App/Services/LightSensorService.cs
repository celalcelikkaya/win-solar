using System.Windows.Threading;
using Windows.Devices.Sensors;
using WinSolar.Core;
using WinSolar.Core.Engine;
using WinSolar.Core.Light;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Sun;

namespace WinSolar.App.Services;

/// <summary>
/// The ambient light sensor. It only reports while someone listens, so it stays subscribed with a report interval
/// (see CLAUDE.md). Readings feed the hysteresis tracker and the self-calibration. <see cref="Changed"/> fires when the
/// room theme, a pending change or (noticeably) the lux value changes, not on every reading.
/// </summary>
public sealed class LightSensorService : IRoomLight, IDisposable
{

    private readonly Dispatcher dispatcher;
    private readonly LearnedStore store;
    private readonly Func<Place> place;
    private readonly LightSensor? sensor;
    private readonly RoomLightTracker tracker;
    private readonly CalibrationLearner learner;
    private double? reportedLux;

    public LightSensorService(Dispatcher dispatcher, LearnedStore store, Func<Place> place)
    {
        this.dispatcher = dispatcher;
        this.store = store;
        this.place = place;
        learner = new CalibrationLearner(store.State.Calibration);
        tracker = new RoomLightTracker(CalibrationMath.For(learner.Current));
        try
        {
            sensor = LightSensor.GetDefault();
            if (sensor is null) return;
            sensor.ReportInterval = Math.Max(sensor.MinimumReportInterval, 1000u);
            sensor.ReadingChanged += OnReading;
            if (sensor.GetCurrentReading() is { } first) Feed(DateTimeOffset.Now, first.IlluminanceInLux);
        }
        catch (Exception e) when (e is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Log.Write($"Light sensor unavailable: {e.Message}");
            sensor = null;
        }
    }

    public event Action? Changed;

    public bool Available => sensor is not null;

    public double? Lux => tracker.Lux;

    public (Theme Theme, DateTimeOffset At)? Pending => tracker.Pending;

    public Thresholds Thresholds => tracker.Thresholds;

    public bool Learned => CalibrationMath.IsLearned(learner.Current);

    public Theme? Current(DateTimeOffset now) => tracker.Stable(now);

    public void ResetLearning()
    {
        learner.Reset();
        store.Update(s => s with { Calibration = learner.Current });
        tracker.Thresholds = CalibrationMath.For(learner.Current);
        Changed?.Invoke();
    }

    private void OnReading(LightSensor sender, LightSensorReadingChangedEventArgs args)
    {
        var lux = args.Reading.IlluminanceInLux;
        dispatcher.BeginInvoke(() => Feed(DateTimeOffset.Now, lux));
    }

    private void Feed(DateTimeOffset now, double lux)
    {
        var before = (tracker.Stable(now), tracker.Pending);
        tracker.Feed(now, lux);
        var p = place();
        if (learner.Add(now, lux, SunCalculator.Elevation(now, p.Lat, p.Lng)))
        {
            store.Update(s => s with { Calibration = learner.Current });
            tracker.Thresholds = CalibrationMath.For(learner.Current);
        }

        // The sensor itself reports only when the light changes (in ~10 lx steps on Celal's laptop, measured
        // 2026-11-27), so every new value is shown at once.
        var luxMoved = reportedLux != lux;
        if (before != (tracker.Stable(now), tracker.Pending) || luxMoved)
        {
            reportedLux = lux;
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        if (sensor is not null) sensor.ReadingChanged -= OnReading;
    }
}
