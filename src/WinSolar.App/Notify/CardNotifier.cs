using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using WinSolar.App.Flyout;
using WinSolar.App.Interop;
using WinSolar.App.Services;
using WinSolar.App.Styling;
using WinSolar.App.Tray;
using WinSolar.Core.Decisions;
using WinSolar.Core.Engine;
using WinSolar.Core.Presentation;
using WinSolar.Core.Scheduling;

namespace WinSolar.App.Notify;

/// <summary>Shows Win Solar's own cards for the engine (no Windows toasts), stacked above the tray.</summary>
public sealed class CardNotifier : INotifier
{
    private const double WindowMargin = 16;
    private const double Gap = 12;
    private readonly List<NoticeCard> infos = [];
    private readonly DispatcherTimer countdown;
    private EngineHost? host;
    private TrayIcon? tray;
    private FlyoutWindow? flyout;
    private NoticeCard? headsUpCard;
    private Transition? headsUpFor;

    public CardNotifier()
    {
        countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdown.Tick += (_, _) => UpdateCountdown();
    }

    public void Bind(EngineHost engineHost, TrayIcon trayIcon, FlyoutWindow flyoutWindow)
    {
        host = engineHost;
        tray = trayIcon;
        flyout = flyoutWindow;
    }

    public void HeadsUp(Transition next, int minutesLeft)
    {
        headsUpFor = next;
        countdown.Start();
        if (Suppressed() || host is null)
        {
            tray?.SetBreathing(true);
            return;
        }

        var engine = host.Engine;
        var card = new NoticeCard(next.Theme, HeadsUpTitle(next), HeadsUpLine(next), withRing: true, quiet: true,
            ("Snooze 15 min", true, engine.Snooze), ("Skip", false, engine.Skip), ("Switch now", false, engine.SwitchNow))
        {
            AutoHide = TimeSpan.FromSeconds(8),
            TuckWhenIdle = true,
        };
        card.Gone += tucked =>
        {
            if (headsUpCard == card) headsUpCard = null;
            if (tucked && headsUpFor is not null) tray?.SetBreathing(true);
            Layout();
        };
        headsUpCard = card;
        Present(card);
        UpdateCountdown();
    }

    public void DismissHeadsUp()
    {
        headsUpFor = null;
        countdown.Stop();
        headsUpCard?.Dismiss();
        headsUpCard = null;
        tray?.SetBreathing(false);
    }

    public void Switched(Theme theme, Cause cause, Transition? next)
    {
        if (Suppressed() || host is null) return;
        var settings = host.Store.Settings;
        var line = cause == Cause.FullScreenEnded
            ? "Waited until you left full screen."
            : next is null ? "" : $"Next: {next.Theme} at {Format.When(next.At, DateTimeOffset.Now, host.Last?.Place.TimeZoneId ?? "UTC", settings.Hour12)}";
        var card = new NoticeCard(theme, $"Switched to {theme} theme", line, withRing: false, quiet: false) { AutoHide = TimeSpan.FromSeconds(6.5) };
        card.Gone += _ => { infos.Remove(card); Layout(); };
        infos.Add(card);
        while (infos.Count > 2) infos[0].Dismiss();
        Present(card);
    }

    /// <summary>Re-stacks the cards (called when the flyout opens or closes).</summary>
    public void Layout()
    {
        var cards = infos.Where(c => !c.IsGone).ToList();
        if (headsUpCard is { IsGone: false } hu) cards.Insert(0, hu);
        if (cards.Count == 0) return;

        var (edge, wa) = TaskbarEdge.Get();
        var right = wa.Right + WindowMargin - Gap;
        if (flyout is { IsShownOnRight: true })
        {
            var shifted = right - flyout.VisibleWidth - Gap;
            if (shifted - wa.Left > 340) right = shifted;
        }

        var fromTop = edge == Edge.Top;
        var y = fromTop ? wa.Top - WindowMargin + Gap : wa.Bottom + WindowMargin - Gap;
        foreach (var card in cards)
        {
            var visible = card.ActualHeight - 2 * WindowMargin;
            var top = fromTop ? y : y - card.ActualHeight;
            card.MoveTo(right - card.ActualWidth, top);
            y = fromTop ? y + visible + 8 : y - visible - 8;
        }
    }

    private void Present(NoticeCard card)
    {
        card.ShowAt(-10000, -10000);
        card.UpdateLayout();
        Layout();
    }

    private void UpdateCountdown()
    {
        if (headsUpFor is not { } next) return;
        var left = next.At - DateTimeOffset.Now;
        headsUpCard?.SetTitle(HeadsUpTitle(next));
        headsUpCard?.SetRing(left.TotalSeconds / 300);
        if (left <= TimeSpan.Zero) countdown.Stop();
    }

    private static string HeadsUpTitle(Transition next)
    {
        var minutes = Math.Max(0, (int)Math.Ceiling((next.At - DateTimeOffset.Now).TotalMinutes));
        return $"{next.Theme} in {Format.Duration(minutes)}";
    }

    private string HeadsUpLine(Transition next)
    {
        var tz = host?.Last?.Place.TimeZoneId ?? "UTC";
        var time = Format.When(next.At, DateTimeOffset.Now, tz, host?.Store.Settings.Hour12 ?? false);
        var kind = next.Kind switch { TransitionKind.Sunset => "Sunset", TransitionKind.Sunrise => "Sunrise", _ => "Scheduled" };
        return $"{kind} · {time}";
    }

    /// <summary>Best effort "do not disturb": presentation, full screen, locked, quiet time, or notifications turned off.</summary>
    private static bool Suppressed()
    {
        if (Native.SHQueryUserNotificationState(out var state) == 0 &&
            state is Native.QunsNotPresent or Native.QunsBusy or Native.QunsRunningD3DFullScreen or Native.QunsPresentationMode or Native.QunsQuietTime)
            return true;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications");
        return key?.GetValue("ToastEnabled") is int enabled && enabled == 0;
    }
}

/// <summary>The small "Dark theme · Auto again at 05:14" pill shown for Win + Shift + D.</summary>
internal sealed class Osd : Window
{
    private static Osd? current;

    private Osd(Theme theme, string detail)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        SetResourceReference(FontFamilyProperty, "UiFont");

        var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Ui.Text($"{theme} theme", 14, FontWeights.SemiBold));
        texts.Children.Add(Ui.Text(detail, 12, null, "Ink2"));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Glyph(theme, 26));
        row.Children.Add(texts);
        var pill = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 18, 10), Child = row, Margin = new Thickness(16) };
        pill.SetResourceReference(Border.BackgroundProperty, "Bg");
        pill.SetResourceReference(Border.BorderBrushProperty, "Line");
        pill.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.25 };
        Content = pill;
        SourceInitialized += (_, _) => Native.MakeNoActivate(new WindowInteropHelper(this).Handle);
    }

    public static void Flash(Theme theme, string detail)
    {
        current?.Close();
        var osd = new Osd(theme, detail) { Left = -10000, Top = -10000, Opacity = 0 };
        current = osd;
        osd.Show();
        osd.UpdateLayout();
        var (edge, wa) = TaskbarEdge.Get();
        osd.Left = wa.Left + (wa.Width - osd.ActualWidth) / 2;
        osd.Top = edge == Edge.Top ? wa.Top + 40 : wa.Bottom - osd.ActualHeight - 40;
        osd.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        var close = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        close.Tick += (_, _) =>
        {
            close.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, _) => { osd.Close(); if (current == osd) current = null; };
            osd.BeginAnimation(OpacityProperty, fade);
        };
        close.Start();
    }
}
