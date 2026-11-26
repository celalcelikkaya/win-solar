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
    private static readonly TimeSpan Repaint = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan RollTime = TimeSpan.FromSeconds(1.6);
    private static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(1.3);
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(5);
    private const double Band = 0.12;

    private readonly List<(Window Window, Native.Rect Bounds, Image New, Image Old)> screens = [];
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
        step = new DispatcherTimer(Repaint, DispatcherPriority.Normal, (_, _) => Reveal(animation), Dispatcher.CurrentDispatcher);
    }

    /// <summary>Shows the current picture of every monitor and waits until it is really on screen.</summary>
    private void Freeze()
    {
        foreach (var bounds in Native.Monitors())
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

    private void Reveal(SwitchAnimation animation)
    {
        step?.Stop();
        step = null;
        try
        {
            foreach (var s in screens) s.New.Source = Capture(s.Bounds);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            Log.Write($"Switch animation ended early: {e.Message}");
            Close();
            return;
        }

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
        step = new DispatcherTimer(time + TimeSpan.FromMilliseconds(60), DispatcherPriority.Normal, (_, _) => Close(), Dispatcher.CurrentDispatcher);
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
