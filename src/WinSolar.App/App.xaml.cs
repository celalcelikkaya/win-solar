using System.Windows;
using WinSolar.App.Services;
using WinSolar.App.Tray;
using WinSolar.Core.Decisions;
using WinSolar.Core.Engine;
using WinSolar.Core.Scheduling;

namespace WinSolar.App;

public partial class App : Application
{
    private Mutex? singleInstance;
    private EngineHost? host;
    private TrayIcon? tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstance = new Mutex(initiallyOwned: true, @"Local\WinSolar.SingleInstance", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        var store = new AppStateStore();
        host = new EngineHost(store, new SilentNotifier());
        tray = new TrayIcon(host, toggleFlyout: () => { }, openSettings: () => { }, quit: Quit);
        host.Start();
    }

    private void Quit() => Shutdown();

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        host?.Dispose();
        singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Placeholder until the toast notifier (Plan 1, Task 12).</summary>
    private sealed class SilentNotifier : INotifier
    {
        public void HeadsUp(Transition next, int minutesLeft) { }
        public void DismissHeadsUp() { }
        public void Switched(Theme theme, Cause cause, Transition? next) { }
    }
}
