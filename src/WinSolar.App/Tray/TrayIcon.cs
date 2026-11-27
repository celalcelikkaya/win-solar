using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using WinSolar.App.Interop;
using WinSolar.App.Services;
using WinSolar.App.Styling;
using WinSolar.Core.Decisions;
using WinSolar.Core.Engine;
using WinSolar.Core.Presentation;
using WinSolar.Core.Scheduling;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WinSolar.App.Tray;

/// <summary>Tray icon: live sun/moon glyph, a tooltip that says what happens next, the right-click menu, and a soft
/// "breathing" while a tucked heads-up is pending.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly EngineHost host;
    private readonly Action toggleFlyout;
    private readonly Action openSettings;
    private readonly Action quit;
    private readonly Forms.NotifyIcon icon;
    private readonly DispatcherTimer breath;
    private Drawing.Icon? normal;
    private Drawing.Icon? dim;
    private (Theme, TrayBadge)? drawn;
    private bool dimmed;

    public TrayIcon(EngineHost host, Action toggleFlyout, Action openSettings, Action quit)
    {
        this.host = host;
        this.toggleFlyout = toggleFlyout;
        this.openSettings = openSettings;
        this.quit = quit;
        icon = new Forms.NotifyIcon { Text = "Win Solar", Visible = true };
        icon.MouseUp += OnMouseUp;
        breath = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        breath.Tick += (_, _) =>
        {
            dimmed = !dimmed;
            icon.Icon = dimmed ? dim : normal;
        };
        host.Changed += Update;
    }

    public void Update(EngineSnapshot s)
    {
        var badge = s.Error is not null || s.WaitingForFullScreen ? TrayBadge.Waiting
            : s.Hold is { Kind: not HoldKind.SwitchNow } ? TrayBadge.Hold
            : s.Mode != Mode.Auto ? TrayBadge.Manual
            : TrayBadge.None;
        if (drawn != (s.Applied, badge))
        {
            var (oldNormal, oldDim) = (normal, dim);
            normal = TrayGlyphs.Render(s.Applied, badge);
            dim = TrayGlyphs.Render(s.Applied, badge, 0.4);
            icon.Icon = dimmed ? dim : normal;
            oldNormal?.Dispose();
            oldDim?.Dispose();
            drawn = (s.Applied, badge);
        }

        var text = s.Error is { } err ? $"Win Solar: {err}" : Tooltip(s, host.Store.Settings.Hour12);
        icon.Text = text.Length > 127 ? text[..127] : text;
    }

    public void SetBreathing(bool on)
    {
        if (on) breath.Start();
        else
        {
            breath.Stop();
            dimmed = false;
            icon.Icon = normal;
        }
    }

    /// <summary>Spec copy: "Win Solar · Dark" then what happens next.</summary>
    public static string Tooltip(EngineSnapshot s, bool hour12)
    {
        var now = DateTimeOffset.Now;
        string When(DateTimeOffset t) => Format.When(t, now, s.Place.TimeZoneId, hour12);
        var line2 =
            s.WaitingForFullScreen ? "Waiting for full screen to close"
            : s.Mode != Mode.Auto ? (s.ManualUntil is { } u ? $"{s.Mode} until {When(u)}" : $"{s.Mode} (manual)")
            : s.Hold is { Kind: HoldKind.Pause } p ? $"Paused until {When(p.Until)}"
            : s.Hold is { Kind: not HoldKind.SwitchNow } h ? $"Postponed until {When(h.Until)}"
            : s.Schedule.Next is { } n ? $"{n.Theme} in {Format.Duration(Math.Max(0, (int)Math.Ceiling((n.At - now).TotalMinutes)))} ({When(n.At)})"
            : "No switch today";
        return $"Win Solar · {s.Applied}\n{line2}";
    }

    private void OnMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left) toggleFlyout();
        else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
    }

    private void ShowMenu()
    {
        var s = host.Last;
        var engine = host.Engine;
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        menu.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenu");

        MenuItem Item(string header, Action action, bool isChecked = false)
        {
            var item = new MenuItem { Header = header, IsChecked = isChecked };
            item.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuItem");
            item.Click += (_, _) => action();
            return item;
        }

        Separator Line()
        {
            var sep = new Separator();
            sep.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuSeparator");
            return sep;
        }

        // Title row with the Win Solar mark (not clickable).
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var logo = Ui.Logo(18);
        logo.Margin = new Thickness(-24, 0, 6, 0); // sits in the check-mark column, so the name lines up with the items
        titleRow.Children.Add(logo);
        titleRow.Children.Add(new TextBlock { Text = "Win Solar", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var title = new MenuItem { Header = titleRow, IsHitTestVisible = false, Focusable = false };
        title.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuItem");
        menu.Items.Add(title);
        menu.Items.Add(Line());

        var mode = s?.Mode ?? Mode.Auto;
        menu.Items.Add(Item("Auto (sunrise and sunset)", () => engine.SetMode(Mode.Auto), mode == Mode.Auto));
        menu.Items.Add(Item("Light", () => engine.SetMode(Mode.Light), mode == Mode.Light));
        menu.Items.Add(Item("Dark", () => engine.SetMode(Mode.Dark), mode == Mode.Dark));
        menu.Items.Add(Line());
        if (s?.Hold is { Kind: HoldKind.Pause } pause)
            menu.Items.Add(Item($"Resume switching (paused until {Format.Time(pause.Until, s.Place.TimeZoneId, host.Store.Settings.Hour12)})", engine.Resume));
        else
            menu.Items.Add(Item("Pause switching for 1 hour", () => engine.Pause(TimeSpan.FromHours(1))));
        menu.Items.Add(Line());
        menu.Items.Add(Item("Open Win Solar", toggleFlyout));
        menu.Items.Add(Item("Settings", openSettings));
        if (host.Updates.Available is { } update && host.Updates.State == UpdateState.Available)
            menu.Items.Add(Item($"Restart to update ({update.Version.ToString(3)})", () => _ = host.Updates.InstallAsync(quit)));
        menu.Items.Add(Item("Quit", quit));

        // A tray menu only closes on outside clicks when its window is in the foreground.
        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource source) Native.SetForegroundWindow(source.Handle);
        };
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        host.Changed -= Update;
        breath.Stop();
        icon.Visible = false;
        icon.Dispose();
        normal?.Dispose();
        dim?.Dispose();
    }
}
