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
/// theme change underneath, captures the new look (the overlay is excluded from capture) and then rolls or fades the
/// old picture away. Exactly one overlay at a time; it can never stay on screen (watchdog).
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

    private readonly List<(Window Window, Native.Rect Bounds, Image New, Image Old)> screens = [];
    private uint[]? oldSample, lastSample;
    private int stableLooks;
    private DateTime appliedAt;
    private DispatcherTimer? step;
    private DispatcherTimer? watchdog;
    private int frames;
    private TimeSpan firstFrame, lastFrame;

    /// <summary>Runs <paramref name="apply"/> (exactly once) behind the chosen animation; its exceptions reach the caller.</summary>
    public void Run(SwitchAnimation animation, Action apply)
    {
        Close();
        if (animation == SwitchAnimation.Instant || !SystemParameters.ClientAreaAnimation)
        {
            apply();
            return;
        }

        try
        {
            Freeze();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
        {
            Log.Write($"Switch animation skipped: {e.Message}");
            Close();
            apply();
            return;
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

        watchdog = new DispatcherTimer(Watchdog, DispatcherPriority.Normal, (_, _) => Close(), Dispatcher.CurrentDispatcher);
        appliedAt = DateTime.UtcNow;
        lastSample = null;
        stableLooks = 0;
        step = new DispatcherTimer(Look, DispatcherPriority.Normal, (_, _) => WaitForRepaint(animation), Dispatcher.CurrentDispatcher);
    }

    /// <summary>Shows the current picture of every monitor and waits until it is really on screen.</summary>
    private void Freeze()
    {
        foreach (var bounds in Native.Monitors())
        {
            var picture = Capture(bounds);
            oldSample ??= Sample(picture);
            var oldImage = new Image { Source = picture, Stretch = Stretch.Fill };
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
            window.SourceInitialized += (_, _) => Native.MakeOverlay(new WindowInteropHelper(window).Handle, bounds);
            window.Show();
            Native.MakeOverlay(new WindowInteropHelper(window).Handle, bounds); // again: WPF may resize on show
            screens.Add((window, bounds, newImage, oldImage));
        }

        // Let WPF render the frozen pictures and DWM put them on screen before anything changes below.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Native.DwmFlush();
        Native.DwmFlush();
    }

    /// <summary>
    /// Windows repaints at its own pace after a theme change, and some apps (Electron, browsers) much later than the
    /// taskbar. Look about every 100 ms (the overlay is excluded from capture) and start once the screen differs from
    /// the frozen picture and three looks in a row agree, no sooner than 0.6 s and no later than 3 s.
    /// </summary>
    private void WaitForRepaint(SwitchAnimation animation)
    {
        try
        {
            var captures = screens.Select(s => Capture(s.Bounds)).ToList();
            var sample = Sample(captures[0]);
            var changed = Differs(oldSample!, sample, 0.02);
            stableLooks = changed && lastSample is not null && !Differs(lastSample, sample, 0.002) ? stableLooks + 1 : 0;
            lastSample = sample;
            var waited = DateTime.UtcNow - appliedAt;
            if ((stableLooks < 2 || waited < MinWait) && waited < GiveUp) return;
            for (var i = 0; i < screens.Count; i++) screens[i].New.Source = captures[i];
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            Log.Write($"Switch animation ended early: {e.Message}");
            Close();
            return;
        }

        step?.Stop();
        step = null;
        Log.Write($"Switch animation: screen repainted after {(DateTime.UtcNow - appliedAt).TotalMilliseconds:0} ms");
        Reveal(animation);
    }

    /// <summary>A 64 x 40 grid of pixels, enough to tell a theme change from a blinking caret.</summary>
    private static uint[] Sample(BitmapSource image)
    {
        var w = image.PixelWidth;
        var h = image.PixelHeight;
        var pixels = new uint[w * h];
        image.CopyPixels(pixels, w * 4, 0);
        var sample = new uint[64 * 40];
        for (var y = 0; y < 40; y++)
            for (var x = 0; x < 64; x++)
                sample[y * 64 + x] = pixels[(y * h / 40 + h / 80) * w + x * w / 64 + w / 128] & 0xFFFFFF;
        return sample;
    }

    private static bool Differs(uint[] a, uint[] b, double share) =>
        a.Zip(b).Count(p => p.First != p.Second) > share * a.Length;

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

        frames = 0;
        CompositionTarget.Rendering += CountFrame;
        step = new DispatcherTimer(time + TimeSpan.FromMilliseconds(60), DispatcherPriority.Normal, (_, _) => CatchUpLate(), Dispatcher.CurrentDispatcher);
    }

    /// <summary>An app that repainted after the new picture was taken would pop in when the overlay closes: fade to it instead.</summary>
    private void CatchUpLate()
    {
        step?.Stop();
        step = null;
        CompositionTarget.Rendering -= CountFrame;
        try
        {
            var late = false;
            foreach (var s in screens)
            {
                var live = Capture(s.Bounds);
                if (s.New.Source is not BitmapSource shown || !Differs(Sample(shown), Sample(live), 0.002)) continue;
                late = true;
                s.Old.BeginAnimation(UIElement.OpacityProperty, null);
                s.Old.OpacityMask = null;
                s.Old.Opacity = 1;
                s.Old.Source = shown;
                s.New.Source = live;
                s.Old.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, CatchUp));
            }

            if (late)
            {
                Log.Write("Switch animation: an app repainted late; faded to it");
                step = new DispatcherTimer(CatchUp + TimeSpan.FromMilliseconds(40), DispatcherPriority.Normal, (_, _) => Close(), Dispatcher.CurrentDispatcher);
                return;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            Log.Write($"Switch animation: {e.Message}");
        }

        Close();
    }

    private void CountFrame(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        if (frames == 0) firstFrame = t;
        else if (t == lastFrame) return;
        lastFrame = t;
        frames++;
    }

    public void Close()
    {
        step?.Stop();
        watchdog?.Stop();
        step = watchdog = null;
        CompositionTarget.Rendering -= CountFrame;
        if (frames > 1)
        {
            Log.Write($"Switch animation: {(lastFrame - firstFrame).TotalMilliseconds / (frames - 1):0.0} ms/frame over {frames} frames");
            frames = 0;
        }

        foreach (var s in screens) s.Window.Close();
        screens.Clear();
        oldSample = lastSample = null;
    }

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
