using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using WinSolar.App.Interop;
using WinSolar.App.Services;
using WinSolar.Core.Decisions;
using WinSolar.Core.Engine;
using WinSolar.Core.Presentation;
using WinSolar.Core.Scheduling;
using Forms = System.Windows.Forms;

namespace WinSolar.App.Tray;

/// <summary>Tray icon: live sun/moon glyph, a tooltip that says what happens next, and the right-click menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly EngineHost host;
    private readonly Action toggleFlyout;
    private readonly Action openSettings;
    private readonly Action quit;
    private readonly Forms.NotifyIcon icon;
    private (Theme, TrayBadge)? drawn;

    public TrayIcon(EngineHost host, Action toggleFlyout, Action openSettings, Action quit)
    {
        this.host = host;
        this.toggleFlyout = toggleFlyout;
        this.openSettings = openSettings;
        this.quit = quit;
        icon = new Forms.NotifyIcon { Text = "Win Solar", Visible = true };
        icon.MouseUp += OnMouseUp;
        host.Changed += Update;
    }

    public void Update(EngineSnapshot s)
    {
        var badge = s.WaitingForFullScreen ? TrayBadge.Waiting
            : s.Hold is { Kind: not HoldKind.SwitchNow } ? TrayBadge.Hold
            : s.Mode != Mode.Auto ? TrayBadge.Manual
            : TrayBadge.None;
        if (drawn != (s.Applied, badge))
        {
            var old = icon.Icon;
            icon.Icon = TrayGlyphs.Render(s.Applied, badge);
            old?.Dispose();
            drawn = (s.Applied, badge);
        }

        var text = Tooltip(s, host.Store.Settings.Hour12);
        icon.Text = text.Length > 127 ? text[..127] : text;
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

        MenuItem Item(string header, Action action, bool isChecked = false)
        {
            var item = new MenuItem { Header = header, IsChecked = isChecked };
            item.Click += (_, _) => action();
            return item;
        }

        var mode = s?.Mode ?? Mode.Auto;
        menu.Items.Add(Item("Auto (sunrise and sunset)", () => engine.SetMode(Mode.Auto), mode == Mode.Auto));
        menu.Items.Add(Item("Light", () => engine.SetMode(Mode.Light), mode == Mode.Light));
        menu.Items.Add(Item("Dark", () => engine.SetMode(Mode.Dark), mode == Mode.Dark));
        menu.Items.Add(new Separator());
        if (s?.Hold is { Kind: HoldKind.Pause } pause)
            menu.Items.Add(Item($"Resume switching (paused until {Format.Time(pause.Until, s.Place.TimeZoneId, host.Store.Settings.Hour12)})", engine.Resume));
        else
            menu.Items.Add(Item("Pause switching for 1 hour", () => engine.Pause(TimeSpan.FromHours(1))));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open Win Solar", toggleFlyout));
        menu.Items.Add(Item("Settings", openSettings));
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
        icon.Visible = false;
        icon.Icon?.Dispose();
        icon.Dispose();
    }
}
