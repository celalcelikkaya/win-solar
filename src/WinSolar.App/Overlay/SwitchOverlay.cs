using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinSolar.App.Interop;
using WinSolar.App.Services;
using WinSolar.Core.Desktop;

namespace WinSolar.App.Overlay;

/// <summary>
/// Hides Windows' instant theme cut: freezes each monitor's picture in a topmost, click-through window, lets the
/// theme change underneath, waits until Windows has really repainted (the overlay is excluded from capture) and then
/// rolls or fades the old picture away. One overlay at a time; it can never stay on screen (one-shot watchdog), and
/// timers of an overlay that was closed do nothing (each run has its own number).
/// </summary>
public sealed class SwitchOverlay
{
    private static readonly TimeSpan Look = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MinWait = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CatchUp = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RollTime = TimeSpan.FromSeconds(1.6);
    private static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(1.3);
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(8);
    private const double Band = 0.12;
    private const int WmDpiChanged = 0x02E0;

    private readonly List<(Window Window, Native.Rect Bounds, Image New, Image Old)> screens = [];
    private readonly List<(DispatcherTimer Timer, bool Watchdog)> timers = [];
    private int run;
    private bool freezing;
    private uint[]? oldSample, lastSample;
    private int stableLooks;
    private DateTime appliedAt;
    private bool counting;
    private int frames;
    private TimeSpan firstFrame, lastFrame;

    /// <summary>Runs <paramref name="apply"/> (exactly once) behind the chosen animation; its exceptions reach the caller.</summary>
    public void Run(SwitchAnimation animation, Action apply)
    {
        if (freezing)
        {
            apply(); // a switch that arrives while the screen is being frozen: no second overlay
            return;
        }

        Close();
        if (animation == SwitchAnimation.Instant || !SystemParameters.ClientAreaAnimation)
        {
            apply();
            return;
        }

        var me = run;
        Start(Watchdog, me, Close, repeat: false, watchdog: true);
        try
        {
            freezing = true;
            Freeze();
        }
        catch (Exception e)
        {
            Log.Write($"Switch animation skipped: {e.Message}");
            Close();
            apply();
            return;
        }
        finally
        {
            freezing = false;
        }

        try
        {
            apply();
        }
        catch
        {
            Close();
            throw;
        }

        appliedAt = DateTime.UtcNow;
        lastSample = null;
        stableLooks = 0;
        Start(Look, me, () => WaitForRepaint(animation), repeat: true);
    }

    /// <summary>Shows the current picture of every monitor and waits until it is really on screen.</summary>
    private void Freeze()
    {
        var monitors = Native.Monitors();
        oldSample = SampleAll(monitors);
        foreach (var bounds in monitors)
        {
            var oldImage = new Image { Source = Capture(bounds), Stretch = Stretch.Fill };
            var newImage = new Image { Stretch = Stretch.Fill };
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = Brushes.Black,
                Left = -32000,
                Top = -32000,
                Width = 1,
                Height = 1,
                Content = new Grid { Children = { newImage, oldImage } },
            };
            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                // Crossing monitors of different DPI must not resize the overlay: it is placed in physical pixels.
                HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
                {
                    if (msg == WmDpiChanged) handled = true;
                    return IntPtr.Zero;
                });
                Native.MakeOverlay(hwnd, bounds);
            };
            screens.Add((window, bounds, newImage, oldImage));
            window.Show();
            Native.MakeOverlay(new WindowInteropHelper(window).Handle, bounds); // again: WPF may move it on show
        }

        // Let WPF render the frozen pictures and DWM put them on screen before anything changes below.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Native.DwmFlush();
        Native.DwmFlush();
    }

    /// <summary>
    /// Windows repaints at its own pace after a theme change, and some apps (Electron, browsers) much later than the
    /// taskbar. Look about every 100 ms with a tiny, flicker-free sample of every monitor, and start once the screen
    /// differs from the frozen picture and three looks in a row agree, no sooner than 0.6 s and no later than 3 s.
    /// </summary>
    private void WaitForRepaint(SwitchAnimation animation)
    {
        try
        {
            var sample = SampleAll(screens.Select(s => s.Bounds));
            var changed = Differs(oldSample!, sample, 0.02);
            stableLooks = changed && lastSample is not null && !Differs(lastSample, sample, 0.002) ? stableLooks + 1 : 0;
            lastSample = sample;
            var waited = DateTime.UtcNow - appliedAt;
            if ((stableLooks < 2 || waited < MinWait) && waited < GiveUp) return;
            foreach (var s in screens) s.New.Source = Capture(s.Bounds);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            Log.Write($"Switch animation ended early: {e.Message}");
            Close();
            return;
        }

        StopTimers(keepWatchdog: true);
        Log.Write($"Switch animation: screen repainted after {(DateTime.UtcNow - appliedAt).TotalMilliseconds:0} ms");
        Reveal(animation);
    }

    private void Reveal(SwitchAnimation animation)
    {
        var time = animation == SwitchAnimation.Fade ? FadeTime : RollTime;
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        foreach (var s in screens)
        {
            if (animation == SwitchAnimation.Fade)
            {
                s.Old.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, time) { EasingFunction = ease });
                continue;
            }

            // The new look is painted top to bottom: the old picture becomes transparent above a soft moving edge.
            var clear = new GradientStop(Colors.Transparent, -Band);
            var solid = new GradientStop(Colors.Black, 0);
            s.Old.OpacityMask = new LinearGradientBrush(new GradientStopCollection { clear, solid }, new Point(0, 0), new Point(0, 1));
            clear.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(-Band, 1, time) { EasingFunction = ease });
            solid.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0, 1 + Band, time) { EasingFunction = ease });
        }

        StartCounting();
        Start(Look, run, Refresh, repeat: true);
        Start(time + TimeSpan.FromMilliseconds(60), run, CatchUpLate, repeat: false);
    }

    /// <summary>
    /// While the roll runs, an app that repaints late (Electron, browsers often take 2–3 s) is taken into the new
    /// picture at once, so the part not painted yet shows it when the edge arrives. Only when the sample changed, so
    /// the full capture (which makes the pointer flicker) is rare.
    /// </summary>
    private void Refresh()
    {
        try
        {
            var sample = SampleAll(screens.Select(s => s.Bounds));
            if (lastSample is not null && !Differs(lastSample, sample, 0.002)) return;
            lastSample = sample;
            foreach (var s in screens) s.New.Source = Capture(s.Bounds);
            Log.Write($"Switch animation: an app repainted at {(DateTime.UtcNow - appliedAt).TotalMilliseconds:0} ms; picture updated");
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            Log.Write($"Switch animation: {e.Message}");
        }
    }

    /// <summary>An app that repainted after the new picture was taken would pop in when the overlay closes: fade to it instead.</summary>
    private void CatchUpLate()
    {
        StopTimers(keepWatchdog: true); // the refresh timer
        StopCounting();
        try
        {
            if (lastSample is not null && Differs(lastSample, SampleAll(screens.Select(s => s.Bounds)), 0.002))
            {
                foreach (var s in screens)
                {
                    var shown = s.New.Source;
                    s.Old.BeginAnimation(UIElement.OpacityProperty, null);
                    s.Old.OpacityMask = null;
                    s.Old.Opacity = 1;
                    s.Old.Source = shown;
                    s.New.Source = Capture(s.Bounds);
                    s.Old.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, CatchUp));
                }

                Log.Write("Switch animation: an app repainted late; faded to it");
                Start(CatchUp + TimeSpan.FromMilliseconds(40), run, Close, repeat: false);
                return;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            Log.Write($"Switch animation: {e.Message}");
        }

        Close();
    }

    public void Close()
    {
        run++; // timers of the closed run now do nothing
        StopTimers(keepWatchdog: false);
        StopCounting();
        foreach (var s in screens) s.Window.Close();
        screens.Clear();
        oldSample = lastSample = null;
    }

    // ---------------- timers that belong to one run ----------------

    private void Start(TimeSpan interval, int owner, Action action, bool repeat, bool watchdog = false)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = interval };
        timer.Tick += (_, _) =>
        {
            if (!repeat || owner != run) timer.Stop();
            if (owner == run) action();
        };
        timers.Add((timer, watchdog));
        timer.Start();
    }

    private void StopTimers(bool keepWatchdog)
    {
        foreach (var t in timers.Where(t => !(keepWatchdog && t.Watchdog)).ToList())
        {
            t.Timer.Stop();
            timers.Remove(t);
        }
    }

    // ---------------- frame timing (logged) ----------------

    private void StartCounting()
    {
        StopCounting();
        frames = 0;
        counting = true;
        CompositionTarget.Rendering += CountFrame;
    }

    private void StopCounting()
    {
        if (!counting) return;
        counting = false;
        CompositionTarget.Rendering -= CountFrame;
        if (frames > 1) Log.Write($"Switch animation: {(lastFrame - firstFrame).TotalMilliseconds / (frames - 1):0.0} ms/frame over {frames} frames");
    }

    private void CountFrame(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        if (frames == 0) firstFrame = t;
        else if (t == lastFrame) return;
        lastFrame = t;
        frames++;
    }

    // ---------------- pictures ----------------

    private static uint[] SampleAll(IEnumerable<Native.Rect> monitors) => monitors.SelectMany(Native.SampleScreen).ToArray();

    private static bool Differs(uint[] a, uint[] b, double share) =>
        a.Length != b.Length || a.Zip(b).Count(p => p.First != p.Second) > share * a.Length;

    private static BitmapSource Capture(Native.Rect bounds)
    {
        var bitmap = Native.CaptureScreen(bounds);
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            Native.DeleteObject(bitmap);
        }
    }
}
