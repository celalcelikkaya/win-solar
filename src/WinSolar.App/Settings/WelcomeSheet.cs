using System.Windows;
using System.Windows.Controls;
using WinSolar.App.Services;
using WinSolar.App.Styling;
using WinSolar.Core.Desktop;
using WinSolar.Core.Power;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Wallpaper;
using static WinSolar.App.SettingsUi.SettingsSheet;

namespace WinSolar.App.SettingsUi;

/// <summary>First run: every feature is on; one switch each to turn it off, then "Start using Win Solar".</summary>
public static class WelcomeSheet
{
    public static void Build(StackPanel body, SheetContext ctx)
    {
        var s = ctx.Settings;
        void Set(Func<Core.Settings.AppSettings, Core.Settings.AppSettings> change)
        {
            ctx.Change(change, rebuild: false);
            ctx.Flyout.RebuildSheet(b => Build(b, ctx));
        }

        var intro = Ui.Text("Everything is turned on so you can try it all. Switch off anything you don't want. You can change all of it later in Settings.", 12.5, null, "Ink2");
        intro.TextWrapping = TextWrapping.Wrap;
        intro.TextTrimming = TextTrimming.None;
        intro.Margin = new Thickness(4, 0, 4, 8);
        body.Children.Add(intro);

        var place = ctx.Host.Last?.Place;
        var rows = new List<UIElement>();
        if (ctx.Host.Light.Available)
            rows.Add(Row("Switch by room light", "Light sensor found: a dark room turns Windows dark, a bright one light",
                Switch(s.Source == ScheduleSource.RoomLight, v => Set(x => x with { Source = v ? ScheduleSource.RoomLight : ScheduleSource.Sun }))));
        else
            rows.Add(Row("Follow the weather", $"Clouds over {place?.Name ?? "your city"} move sunrise and sunset",
                Switch(s.FollowRealLight, v => Set(x => x with { FollowRealLight = v }))));

        var wallpaperOn = s.Wallpaper == WallpaperMode.Sun;
        rows.Add(Row("Wallpaper moves with the sun", "Your own wallpaper is saved and can be restored",
            Switch(wallpaperOn, v => Set(x => x with { Wallpaper = v ? WallpaperMode.Sun : WallpaperMode.Off }))));
        if (wallpaperOn && ctx.Host.Wallpaper.Active() is null)
        {
            var choose = Ui.Button("Pill", "Choose a dynamic wallpaper", () =>
                WallpaperUi.Import(ctx, () => ctx.Flyout.OpenSheet("Welcome to Win Solar", b => Build(b, ctx))));
            choose.HorizontalAlignment = HorizontalAlignment.Left;
            choose.Margin = new Thickness(12, 0, 12, 10);
            rows.Add(choose);
        }

        rows.Add(Row("Pointer and sounds at night", "Black pointer after dark",
            Switch(s.ProfilesFollow, v => Set(x => x with { ProfilesFollow = v }))));
        rows.Add(Row("Roller animation", "The new theme is painted over the screen",
            Switch(s.Animation != SwitchAnimation.Instant, v => Set(x => x with { Animation = v ? SwitchAnimation.Roller : SwitchAnimation.Instant }))));
        rows.Add(Row("Warn before switching", "A small card with Snooze and Skip",
            Switch(s.HeadsUp, v => Set(x => x with { HeadsUp = v }))));
        rows.Add(Row("Wait for full-screen apps", null,
            Switch(s.WaitForFullScreen, v => Set(x => x with { WaitForFullScreen = v }))));
        rows.Add(Row("Dark on low battery", "Below 20%",
            Switch(s.Battery != BatteryRule.NoChange, v => Set(x => x with { Battery = v ? BatteryRule.DarkBelow20 : BatteryRule.NoChange }))));
        rows.Add(Row("Shortcut Win + Shift + D", null,
            Switch(s.Shortcut, v => { ctx.SetShortcut(v); Set(x => x with { Shortcut = v }); })));
        rows.Add(Row("Start with Windows", null,
            Switch(s.StartWithWindows, v => { Autostart.Set(v); Set(x => x with { StartWithWindows = v }); })));
        body.Children.Add(Group(rows));

        var start = Ui.Button("AccentPill", "Start using Win Solar", ctx.Flyout.CloseSheet);
        start.Margin = new Thickness(0, 12, 0, 0);
        start.Padding = new Thickness(12, 9, 12, 9);
        start.HorizontalAlignment = HorizontalAlignment.Stretch;
        body.Children.Add(start);
    }
}
