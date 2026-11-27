using System.Windows;
using WinSolar.App.Flyout;
using WinSolar.App.Notify;
using WinSolar.App.Services;
using WinSolar.App.SettingsUi;
using WinSolar.App.Styling;
using WinSolar.App.Tray;

namespace WinSolar.App;

public partial class App : Application
{
    private const string MutexName = @"Local\WinSolar.SingleInstance";
    private const string ShowEventName = @"Local\WinSolar.Show";

    private Mutex? singleInstance;
    private EventWaitHandle? showSignal;
    private EngineHost? host;
    private TrayIcon? tray;
    private FlyoutWindow? flyout;
    private Hotkey? hotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Failures are logged and shown, never silent and never fatal for a tray app.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write($"Unhandled: {args.Exception}");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write($"Unobserved task: {args.Exception}");
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write($"Fatal: {args.ExceptionObject}");

        singleInstance = new Mutex(initiallyOwned: true, MutexName, out var first);
        if (!first)
        {
            // Already running: ask the running copy to open its window, then leave.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var other)) { other.Set(); other.Dispose(); }
            Shutdown();
            return;
        }

        var store = new AppStateStore();
        var notifier = new CardNotifier();
        var engineHost = new EngineHost(store, notifier);
        ThemeManager.Apply(engineHost.Theme.Read());
        var window = new FlyoutWindow(engineHost);
        var trayIcon = new TrayIcon(engineHost, window.Toggle, window.ShowSettings, Quit);
        var keys = new Hotkey(() =>
        {
            var (theme, detail) = engineHost.Engine.Shortcut();
            Osd.Flash(theme, detail);
        });
        (host, flyout, tray, hotkey) = (engineHost, window, trayIcon, keys);

        notifier.Bind(engineHost, trayIcon, window);
        window.IsVisibleChanged += (_, _) => notifier.Layout();
        if (store.Settings.Shortcut) keys.Register();

        window.Context = new SheetContext
        {
            Host = engineHost,
            Flyout = window,
            SetShortcut = on => on ? keys.Register() : UnregisterShortcut(),
            ShortcutTaken = () => store.Settings.Shortcut && !keys.IsRegistered,
            Quit = Quit,
        };

        // Every Win Solar surface follows the theme Windows is in.
        engineHost.Changed += s => ThemeManager.Apply(s.Applied);

        // First run of an installed copy: start with Windows as the settings say.
        if (Autostart.IsInstalledCopy && store.Settings.StartWithWindows && !Autostart.IsOn()) Autostart.Set(true);

        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        showSignal = signal;
        var listener = new Thread(() =>
        {
            try
            {
                while (signal.WaitOne())
                {
                    if (Dispatcher.HasShutdownStarted) return;
                    Dispatcher.InvokeAsync(window.ShowFlyout);
                }
            }
            catch (ObjectDisposedException)
            {
                // app is closing
            }
        }) { IsBackground = true, Name = "WinSolar.ShowListener" };
        listener.Start();

        engineHost.Start();
        if (store.IsFirstRun || e.Args.Contains("--welcome", StringComparer.OrdinalIgnoreCase))
        {
            store.UpdateSettings(x => x); // saved now, so the welcome shows once
            window.ShowFlyout();
            window.OpenSheet("Welcome to Win Solar", body => WelcomeSheet.Build(body, window.Context));
        }
        else if (!e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            window.ShowFlyout();
        }
    }

    private bool UnregisterShortcut()
    {
        hotkey?.Unregister();
        return true;
    }

    private void Quit() => Shutdown();

    protected override void OnExit(ExitEventArgs e)
    {
        hotkey?.Dispose();
        tray?.Dispose();
        host?.Dispose();
        showSignal?.Dispose();
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}
