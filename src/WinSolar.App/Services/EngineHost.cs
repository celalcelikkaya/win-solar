using System.Windows.Threading;
using Microsoft.Win32;
using WinSolar.Core;
using WinSolar.Core.Engine;

namespace WinSolar.App.Services;

/// <summary>
/// Owns the engine on the UI thread. One timer set to the engine's next due moment; extra checks on wake, unlock,
/// clock/zone changes, and every 10 s only while a switch waits for full screen to end.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer fullScreenPoll;

    public EngineHost(AppStateStore store, INotifier notifier)
    {
        Store = store;
        dispatcher = Dispatcher.CurrentDispatcher;
        Learned = new LearnedStore(store.Folder);
        Power = new PowerService(dispatcher);
        var firstGuess = GuessPlace();
        SolarEngine? engine = null; // the sensor may read before the engine exists
        Light = new LightSensorService(dispatcher, Learned, () => store.Settings.Place ?? engine?.FallbackPlace ?? firstGuess);
        Weather = new WeatherService(dispatcher);
        Desktop = new DesktopProfile(store.Folder);
        Wallpaper = new Wallpapers.WallpaperService(store.Folder, () => store.Settings,
            () => store.Settings.Place ?? engine?.FallbackPlace ?? firstGuess, dispatcher);
        var fullScreen = new FullScreenProbe();
        Theme = new WindowsTheme(() => store.Settings, Desktop, Wallpaper, new Overlay.SwitchOverlay(), fullScreen);
        Engine = engine = new SolarEngine(new SystemClock(), Theme, notifier, fullScreen, store, firstGuess,
            Power, Light, Weather);
        Engine.Changed += OnChanged;
        Power.Changed += Tick;
        Light.Changed += Tick;
        Weather.Changed += Tick;

        timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        timer.Tick += (_, _) => Tick();
        fullScreenPoll = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
        fullScreenPoll.Stop();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.TimeChanged += OnTimeChanged;
    }

    public AppStateStore Store { get; }

    public SolarEngine Engine { get; }

    public LearnedStore Learned { get; }

    public PowerService Power { get; }

    public LightSensorService Light { get; }

    public WeatherService Weather { get; }

    public DesktopProfile Desktop { get; }

    public WindowsTheme Theme { get; }

    public Wallpapers.WallpaperService Wallpaper { get; }

    public EngineSnapshot? Last { get; private set; }

    public event Action<EngineSnapshot>? Changed;

    private bool started;

    public void Start()
    {
        Wallpaper.Importer.CleanUp();
        Engine.Start();
        started = true; // ticks that arrive while the first switch is painted are dropped; the engine re-checks later
        RefreshLook(); // a profile setting may not match yet (first run, or changed while Win Solar was closed)
    }

    /// <summary>Brings the accent, pointer and sounds in line with the current theme (no animation).</summary>
    public void RefreshLook()
    {
        try
        {
            Theme.Refresh();
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Write($"Could not update the desktop look: {e.Message}");
        }
    }

    /// <summary>Re-evaluates now (ignored until <see cref="Start"/>, so early sensor or power events change nothing).</summary>
    public void Tick()
    {
        if (started) Engine.Tick();
    }

    private void OnChanged(EngineSnapshot snapshot)
    {
        if (snapshot.Error is { } error && error != Last?.Error) Log.Write(error);
        Last = snapshot;
        timer.Stop();
        var wait = Engine.NextDue - DateTimeOffset.Now;
        timer.Interval = wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
        timer.Start();
        fullScreenPoll.IsEnabled = snapshot.WaitingForFullScreen;
        Weather.Use(snapshot.Light.Source is LightSource.Weather or LightSource.WeatherUnavailable, snapshot.Place);
        Wallpaper.Update(snapshot.Applied);
        Changed?.Invoke(snapshot);
    }

    private void Later() => dispatcher.BeginInvoke(new Action(Tick));

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Later();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon) Later();
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        TimeZoneInfo.ClearCachedData();
        Zones.ClearCache();
        dispatcher.BeginInvoke(new Action(() =>
        {
            Engine.FallbackPlace = GuessPlace();
            Tick();
        }));
    }

    private static Place GuessPlace()
    {
        var local = TimeZoneInfo.Local;
        var iana = TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var id) ? id : local.Id;
        var now = DateTimeOffset.Now;
        return Places.Guess(iana, now.Offset, now);
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.TimeChanged -= OnTimeChanged;
        timer.Stop();
        fullScreenPoll.Stop();
        Power.Dispose();
        Light.Dispose();
        Weather.Dispose();
        Wallpaper.Dispose();
    }
}
