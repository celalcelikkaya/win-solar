using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinSolar.App.Interop;
using WinSolar.App.Services;
using WinSolar.Core.Desktop;

namespace WinSolar.App.Overlay;

/// <summary>
/// Hides Windows' instant theme cut. Each monitor's picture is frozen in a topmost, click-through window; the theme
/// changes underneath; then the frozen picture is cut away from the top (a window region, cheap) behind a thin soft
/// band (a small per-pixel transparent window), so the live screen shows above the edge. Whatever repaints late simply
/// appears live. One overlay at a time; a one-shot watchdog closes it,
/// and timers of a closed run do nothing.
/// </summary>
public sealed class SwitchOverlay
{
    private static readonly TimeSpan Look = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MinWait = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan RollTime = TimeSpan.FromSeconds(1.6);
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(6);
    private const double BandShare = 0.12;
    private const int WmDpiChanged = 0x02E0;

    /// <summary>One monitor: the frozen picture's window, the soft band (a bare layered window we draw ourselves),
    /// and the frozen pixels the band is made from.</summary>
    private sealed record Screen(Window Main, HwndSource Band, Native.LayeredStrip Strip, int[] Pixels, int[] BandPixels, Native.Rect Bounds)
    {
        public IntPtr MainHwnd => new WindowInteropHelper(Main).Handle;

        public int Width => Bounds.Right - Bounds.Left;

        public int Height => Bounds.Bottom - Bounds.Top;
    }

    private readonly List<Screen> screens = [];
    private readonly List<(DispatcherTimer Timer, bool Watchdog)> timers = [];
    private int run;
    private bool freezing;
    private uint[]? oldSample, lastSample;
    private int stableLooks;
    private DateTime appliedAt;
    private TimeSpan? startedAt;
    private int frames;
    private TimeSpan lastFrame;

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
            var picture = Capture(bounds);
            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            var pixels = new int[width * height];
            picture.CopyPixels(pixels, width * 4, 0);
            var bandHeight = Math.Max(2, (int)Math.Round(height * BandShare));
            var main = NewWindow(new Image { Source = picture, Stretch = Stretch.Fill });

            // The band is a bare window (no WPF content): its pixels and position change together in one call.
            var band = new HwndSource(new HwndSourceParameters("WinSolarBand")
            {
                WindowStyle = Native.BandStyle,
                ExtendedWindowStyle = Native.BandExStyle,
                PositionX = bounds.Left,
                PositionY = bounds.Top - bandHeight,
                Width = width,
                Height = bandHeight,
            });
            Native.ExcludeFromCapture(band.Handle);
            var strip = new Native.LayeredStrip(band.Handle, width, bandHeight);
            var screen = new Screen(main, band, strip, pixels, new int[width * bandHeight], bounds);
            screens.Add(screen);

            main.SourceInitialized += (_, _) => Native.MakeOverlay(screen.MainHwnd, bounds);
            main.Show();
            Native.MakeOverlay(screen.MainHwnd, bounds); // again: WPF may move it on show
            Native.BringToTop(band.Handle);
        }

        // Let WPF render the frozen pictures and DWM put them on screen before anything changes below.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Native.DwmFlush();
        Native.DwmFlush();
    }

    private static Window NewWindow(UIElement content)
    {
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
            Content = content,
        };
        window.SourceInitialized += (_, _) =>
        {
            // Crossing monitors of different DPI must not resize the overlay: it is placed in physical pixels.
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
            {
                if (msg == WmDpiChanged) handled = true;
                return IntPtr.Zero;
            });
        };
        return window;
    }

    private static int BandHeight(Screen s) => s.BandPixels.Length / s.Width;

    /// <summary>
    /// The band's pixels for an edge at <paramref name="edge"/>: the frozen picture under the band, premultiplied by an
    /// alpha that rises smoothly from 0 at the edge to 1 where the uncut picture starts.
    /// </summary>
    private static void PaintBand(Screen s, int edge)
    {
        var band = BandHeight(s);
        var w = s.Width;
        for (var i = 0; i < band; i++)
        {
            var y = edge + i;
            var row = i * w;
            if (y < 0 || y >= s.Height)
            {
                Array.Clear(s.BandPixels, row, w);
                continue;
            }

            var f = (i + 0.5) / band;
            var a = (int)Math.Round(255 * f * f * (3 - 2 * f)); // smoothstep
            var src = y * w;
            for (var x = 0; x < w; x++)
            {
                var c = s.Pixels[src + x];
                var r = ((c >> 16) & 0xFF) * a / 255;
                var g = ((c >> 8) & 0xFF) * a / 255;
                var b = (c & 0xFF) * a / 255;
                s.BandPixels[row + x] = (a << 24) | (r << 16) | (g << 8) | b;
            }
        }
    }

    /// <summary>
    /// Give Windows a moment so most of the screen has its new look before the edge passes: look about every 100 ms
    /// with a tiny, flicker-free sample, and start once the screen changed and three looks agree (0.4–1.5 s). Anything
    /// that repaints later just appears live, since the frozen picture is cut away rather than replaced.
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
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            Log.Write($"Switch animation ended early: {e.Message}");
            Close();
            return;
        }

        StopTimers(keepWatchdog: true);
        Log.Write($"Switch animation: started after {(DateTime.UtcNow - appliedAt).TotalMilliseconds:0} ms");
        startedAt = null;
        frames = 0;
        CompositionTarget.Rendering += Step;
    }

    /// <summary>One frame: move the cut edge. Closes at the end.</summary>
    private void Step(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == lastFrame) return;
        lastFrame = now;
        startedAt ??= now;
        frames++;
        var t = Math.Min(1, (now - startedAt.Value) / RollTime);
        var p = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; // ease in-out
        foreach (var s in screens)
        {
            // The edge travels from above the screen to below it: live screen above, soft band, frozen picture below.
            var band = BandHeight(s);
            var edge = (int)Math.Round(-band + p * (s.Height + band));
            PaintBand(s, edge);
            s.Strip.Show(s.BandPixels, s.Bounds.Left, s.Bounds.Top + edge);
            Native.ClipWindow(s.MainHwnd, 0, Math.Clamp(edge + band, 0, s.Height), s.Width, s.Height);
        }

        if (t < 1) return;
        var span = (now - startedAt.Value).TotalMilliseconds;
        Log.Write($"Switch animation: {(frames > 1 ? span / (frames - 1) : 0):0.0} ms/frame over {frames} frames");
        Close();
    }

    public void Close()
    {
        run++; // timers of the closed run now do nothing
        StopTimers(keepWatchdog: false);
        CompositionTarget.Rendering -= Step;
        foreach (var s in screens)
        {
            s.Strip.Dispose();
            s.Band.Dispose();
            s.Main.Close();
        }

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
