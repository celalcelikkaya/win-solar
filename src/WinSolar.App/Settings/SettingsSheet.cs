using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinSolar.App.Flyout;
using WinSolar.App.Services;
using WinSolar.App.Styling;
using WinSolar.Core;
using WinSolar.Core.Desktop;
using WinSolar.Core.Power;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;

namespace WinSolar.App.SettingsUi;

/// <summary>What the sheets need from the app.</summary>
public sealed class SheetContext
{
    public required EngineHost Host { get; init; }
    public required FlyoutWindow Flyout { get; init; }

    /// <summary>Turns Win + Shift + D on/off; returns false when another app owns it.</summary>
    public required Func<bool, bool> SetShortcut { get; init; }

    public required Func<bool> ShortcutTaken { get; init; }
    public required Action Quit { get; init; }

    public AppSettings Settings => Host.Store.Settings;

    /// <summary>A file picker over the flyout (which stays open meanwhile); null when cancelled.</summary>
    public string? PickFile(string filter, string? current)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (current is not null && System.IO.Path.GetDirectoryName(current) is { } dir && System.IO.Directory.Exists(dir))
            dialog.InitialDirectory = dir;
        Flyout.KeepOpen = true;
        try
        {
            return dialog.ShowDialog(Flyout) == true ? dialog.FileName : null;
        }
        finally
        {
            Flyout.KeepOpen = false;
            Flyout.Activate();
        }
    }

    /// <summary>Saves a change, lets the engine react, and redraws the open settings sheet.</summary>
    public void Change(Func<AppSettings, AppSettings> change, bool rebuild = true)
    {
        var before = Host.Store.Settings;
        Host.Store.UpdateSettings(change);
        var after = Host.Store.Settings;
        // Only a profile change re-applies the look, so an unrelated setting never undoes the user's own Windows choice.
        if (before.Day != after.Day || before.Night != after.Night || before.ProfilesFollow != after.ProfilesFollow) Host.RefreshLook();
        if (before.Wallpaper != after.Wallpaper || before.ActiveSet != after.ActiveSet
            || before.Day.Wallpaper != after.Day.Wallpaper || before.Night.Wallpaper != after.Night.Wallpaper)
            Host.Wallpaper.Refresh();
        // Off means Win Solar leaves the wallpaper alone, so its own picture makes way for the user's.
        if (after.Wallpaper == Core.Wallpaper.WallpaperMode.Off && before.Wallpaper != Core.Wallpaper.WallpaperMode.Off)
            Host.Wallpaper.Restore();
        Host.Tick();
        if (rebuild) Flyout.RebuildSheet(body => SettingsSheet.Build(body, this));
    }
}

public static class SettingsSheet
{
    public static void Build(StackPanel body, SheetContext ctx)
    {
        var s = ctx.Settings;
        var light = ctx.Host.Light;

        body.Children.Add(Section("Schedule"));
        List<(string, ScheduleSource)> sources = [("Sunrise and sunset", ScheduleSource.Sun)];
        if (light.Available || s.Source == ScheduleSource.RoomLight) sources.Add(("Room light", ScheduleSource.RoomLight));
        sources.Add(("Fixed times", ScheduleSource.Fixed));
        var schedule = new List<UIElement>
        {
            Row("Switch by", null, Segmented<ScheduleSource>([.. sources], s.Source, v => ctx.Change(x => x with { Source = v })), stacked: true),
        };
        if (s.Source == ScheduleSource.RoomLight)
        {
            var t = light.Thresholds;
            var reset = Ui.Button("Pill", "Reset learning", () =>
            {
                light.ResetLearning();
                ctx.Flyout.RebuildSheet(b => Build(b, ctx));
            });
            reset.VerticalAlignment = VerticalAlignment.Center;
            schedule.Add(Row("Room light",
                light.Available
                    ? $"Dark below {t.DarkBelow:0} lx, light above {t.LightAbove:0} lx, at any hour · {(light.Learned ? "learned" : "learning this room")}"
                    : "No light sensor found. Follows sunrise and sunset instead",
                light.Available ? reset : new Border()));
        }
        else if (s.Source == ScheduleSource.Sun)
        {
            schedule.Add(Row("Light theme", "Minutes after sunrise. Minus means before.",
                Stepper(s.OffsetSunriseMin, v => ctx.Change(x => x with { OffsetSunriseMin = v }))));
            schedule.Add(Row("Dark theme", "Minutes after sunset. Minus means before.",
                Stepper(s.OffsetSunsetMin, v => ctx.Change(x => x with { OffsetSunsetMin = v }))));
        }
        else
        {
            schedule.Add(Row("Light theme at", null, TimeBox(s.FixedLight, v => ctx.Change(x => x with { FixedLight = v }))));
            schedule.Add(Row("Dark theme at", null, TimeBox(s.FixedDark, v => ctx.Change(x => x with { FixedDark = v }))));
        }

        var place = ctx.Host.Last?.Place;
        if (s.Source != ScheduleSource.Fixed && !(s.Source == ScheduleSource.RoomLight && light.Available))
        {
            schedule.Add(Row("Follow the weather", $"Clouds over {place?.Name ?? "your city"} move sunrise and sunset",
                Switch(s.FollowRealLight, v => ctx.Change(x => x with { FollowRealLight = v }))));
            if (s.FollowRealLight)
                schedule.Add(Row("Move a switch by at most", "So a passing cloud never flips the theme at noon",
                    Segmented<int>([("30 min", 30), ("1 hour", 60), ("1.5 hours", 90)], s.MaxShiftMin,
                        v => ctx.Change(x => x with { MaxShiftMin = v })), stacked: true));
        }

        var locationButton = Ui.Button("FlatButton", LinkRow("Location", place is null ? "" : string.Join(", ", new[] { place.Name, place.Country }.Where(x => !string.IsNullOrEmpty(x)))),
            () => ctx.Flyout.OpenSheet("Location", b => LocationSheet.Build(b, ctx)));
        locationButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        locationButton.Padding = new Thickness(12, 10, 12, 10);
        schedule.Add(locationButton);
        body.Children.Add(Group(schedule));

        body.Children.Add(Section("Around a switch"));
        var shortcutHint = s.Shortcut && ctx.ShortcutTaken() ? "Win + Shift + D is used by another app." : "Win + Shift + D switches the theme";
        body.Children.Add(Group(
            Row("Warn before switching", "A small card 5 min before, with Snooze and Skip", Switch(s.HeadsUp, v => ctx.Change(x => x with { HeadsUp = v }))),
            Row("Wait for full-screen apps", "Videos, games and slideshows finish first", Switch(s.WaitForFullScreen, v => ctx.Change(x => x with { WaitForFullScreen = v }))),
            Row("Manual mode ends by itself", "Light or Dark goes back to Auto at the next switch", Switch(s.ManualEndsByItself, v => ctx.Change(x => x with { ManualEndsByItself = v }))),
            Row("Keyboard shortcut", shortcutHint, Switch(s.Shortcut, v => { ctx.SetShortcut(v); ctx.Change(x => x with { Shortcut = v }); })),
            Row("Notify after switching", null, Switch(s.NotifyAfterSwitch, v => ctx.Change(x => x with { NotifyAfterSwitch = v })))));

        body.Children.Add(Section("Battery"));
        body.Children.Add(Group(
            Row("On battery", "Dark pixels use less power on OLED screens",
                Segmented<BatteryRule>([("No change", BatteryRule.NoChange), ("Dark below 20%", BatteryRule.DarkBelow20), ("Always dark", BatteryRule.AlwaysDark)],
                    s.Battery, v => ctx.Change(x => x with { Battery = v })), stacked: true)));

        WallpaperUi.Section(body, ctx);

        foreach (var (title, theme) in new[] { ("Day profile · Light theme", Core.Scheduling.Theme.Light), ("Night profile · Dark theme", Core.Scheduling.Theme.Dark) })
        {
            var p = theme == Core.Scheduling.Theme.Light ? s.Day : s.Night;
            Func<AppSettings, Profile, AppSettings> set = theme == Core.Scheduling.Theme.Light ? (x, v) => x with { Day = v } : (x, v) => x with { Night = v };
            body.Children.Add(Section(title));
            var rows = new List<UIElement>();
            if (ctx.Host.Desktop.AccentAvailable)
                rows.Add(Row("Accent color", null, Swatches(p.Accent, v => ctx.Change(x => set(x, (theme == Core.Scheduling.Theme.Light ? x.Day : x.Night) with { Accent = v }))), stacked: true));
            rows.Add(Row("Mouse pointer", null, Segmented<Pointer>([("White", Pointer.White), ("Black", Pointer.Black)], p.Pointer,
                v => ctx.Change(x => set(x, (theme == Core.Scheduling.Theme.Light ? x.Day : x.Night) with { Pointer = v })))));
            rows.Add(Row("Sounds", null, Segmented<Sounds>([("Normal", Sounds.Normal), ("Off", Sounds.Off)], p.Sounds,
                v => ctx.Change(x => set(x, (theme == Core.Scheduling.Theme.Light ? x.Day : x.Night) with { Sounds = v })))));
            if (s.Wallpaper == Core.Wallpaper.WallpaperMode.PerProfile) rows.Add(WallpaperUi.ProfileRow(ctx, theme));
            body.Children.Add(Group(rows));
        }

        body.Children.Add(Section("Windows"));
        body.Children.Add(Group(
            Row("Pointer and sounds follow the profile", "Off keeps your daytime pointer and sounds",
                Switch(s.ProfilesFollow, v => ctx.Change(x => x with { ProfilesFollow = v })))));

        body.Children.Add(Section("General"));
        body.Children.Add(Group(
            Row("Start with Windows", "Starts quietly in the tray", Switch(s.StartWithWindows, v => { Autostart.Set(v); ctx.Change(x => x with { StartWithWindows = v }); })),
            Row("Hide when clicking outside", "Off keeps the window open", Switch(s.HideOnClickOutside, v => ctx.Change(x => x with { HideOnClickOutside = v }))),
            Row("24-hour clock", null, Switch(!s.Hour12, v => ctx.Change(x => x with { Hour12 = !v })))));

        var version = typeof(SettingsSheet).Assembly.GetName().Version;
        var logo = Ui.Logo(40);
        logo.VerticalAlignment = VerticalAlignment.Center;
        var aboutText = Ui.Row(Orientation.Vertical, 0,
            Ui.Text($"Win Solar {version?.ToString(3)}", 13, FontWeights.SemiBold),
            Ui.Text("Celal Çelikkaya", 12, null, "Ink2"));
        aboutText.Margin = new Thickness(12, 0, 0, 0);
        aboutText.VerticalAlignment = VerticalAlignment.Center;
        var quit = Ui.Button("Pill", Ui.Row(Orientation.Horizontal, 6, Ui.IconInherit(Icons.Power, 13), new TextBlock { Text = "Quit" }), ctx.Quit);
        quit.VerticalAlignment = VerticalAlignment.Center;
        var about = new DockPanel { Margin = new Thickness(4, 14, 4, 0) };
        DockPanel.SetDock(quit, Dock.Right);
        about.Children.Add(quit);
        about.Children.Add(logo);
        about.Children.Add(aboutText);
        body.Children.Add(about);
    }

    // ---------- building blocks ----------

    internal static TextBlock Section(string title)
    {
        var t = Ui.Text(title.ToUpperInvariant(), 11, FontWeights.SemiBold, "Ink2");
        t.Margin = new Thickness(4, 12, 0, 6);
        return t;
    }

    internal static Border Group(IEnumerable<UIElement> rows) => Group(rows.ToArray());

    internal static Border Group(params UIElement[] rows)
    {
        var stack = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var line = new Border { Height = 1 };
                line.SetResourceReference(Border.BackgroundProperty, "Line");
                stack.Children.Add(line);
            }

            stack.Children.Add(rows[i]);
        }

        return Ui.Card(stack, new Thickness(0));
    }

    internal static FrameworkElement Row(string title, string? hint, FrameworkElement control, bool stacked = false)
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Ui.Text(title, 13, FontWeights.SemiBold));
        if (hint is not null)
        {
            var h = Ui.Text(hint, 11.5, null, "Ink2");
            h.TextWrapping = TextWrapping.Wrap;
            h.TextTrimming = TextTrimming.None;
            texts.Children.Add(h);
        }

        if (stacked)
        {
            control.Margin = new Thickness(0, 8, 0, 0);
            return new StackPanel { Margin = new Thickness(12, 10, 12, 10), Children = { texts, control } };
        }

        control.Margin = new Thickness(12, 0, 0, 0);
        control.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
        DockPanel.SetDock(control, Dock.Right);
        dock.Children.Add(control);
        dock.Children.Add(texts);
        return dock;
    }

    private static FrameworkElement LinkRow(string title, string value)
    {
        var right = Ui.Row(Orientation.Horizontal, 6, Ui.Text(value, 12, null, "Ink2"), Ui.Icon(Icons.Pin, 15));
        right.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(Ui.Text(title, 13, FontWeights.SemiBold));
        return dock;
    }

    internal static CheckBox Switch(bool on, Action<bool> changed)
    {
        var box = new CheckBox { IsChecked = on };
        box.SetResourceReference(FrameworkElement.StyleProperty, "Switch");
        box.Click += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    /// <summary>The eight accent swatches; the chosen one has a ring.</summary>
    private static FrameworkElement Swatches(string selected, Action<string> picked)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var hex in Accents.Swatches)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var dot = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(hex == selected ? 2.5 : 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex,
            };
            dot.SetResourceReference(Border.BorderBrushProperty, "Ink");
            dot.MouseLeftButtonUp += (_, _) => picked(hex);
            panel.Children.Add(dot);
        }

        return panel;
    }

    internal static FrameworkElement Segmented<T>((string Label, T Value)[] options, T selected, Action<T> picked)
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        var group = Guid.NewGuid().ToString("N");
        foreach (var (label, value) in options)
        {
            var radio = new RadioButton { Content = label, GroupName = group, IsChecked = EqualityComparer<T>.Default.Equals(value, selected) };
            radio.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
            radio.Checked += (_, _) => { if (!EqualityComparer<T>.Default.Equals(value, selected)) picked(value); };
            grid.Children.Add(radio);
        }

        var border = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(2), Child = grid };
        border.SetResourceReference(Border.BackgroundProperty, "Card2");
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        return border;
    }

    private static FrameworkElement Stepper(int value, Action<int> changed)
    {
        const int min = -120, max = 120, step = 5;
        var text = value > 0 ? $"+{value} min" : value < 0 ? $"−{-value} min" : "0 min";
        var minus = Ui.Button("Pill", "−", () => changed(Math.Max(min, value - step)));
        var plus = Ui.Button("Pill", "+", () => changed(Math.Min(max, value + step)));
        minus.Padding = plus.Padding = new Thickness(10, 3, 10, 3);
        minus.IsEnabled = value > min;
        plus.IsEnabled = value < max;
        var label = Ui.Text(text, 13, FontWeights.SemiBold);
        label.MinWidth = 64;
        label.TextAlignment = TextAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        return Ui.Row(Orientation.Horizontal, 2, minus, label, plus);
    }

    private static FrameworkElement TimeBox(TimeOnly value, Action<TimeOnly> changed)
    {
        var box = new TextBox { Text = value.ToString("HH:mm", CultureInfo.InvariantCulture), Width = 76, TextAlignment = TextAlignment.Center };
        box.SetResourceReference(FrameworkElement.StyleProperty, "Field");
        void Commit()
        {
            if (TimeOnly.TryParseExact(box.Text.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            {
                if (t != value) changed(t);
            }
            else
            {
                box.Text = value.ToString("HH:mm", CultureInfo.InvariantCulture);
            }
        }

        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return box;
    }
}

public static class LocationSheet
{
    public static void Build(StackPanel body, SheetContext ctx)
    {
        var current = ctx.Host.Last?.Place;
        var message = Ui.Text("", 12, null, "Ink2");
        message.TextWrapping = TextWrapping.Wrap;
        message.TextTrimming = TextTrimming.None;
        message.Visibility = Visibility.Collapsed;
        var listTitle = SettingsSheet.Section("Popular");
        var list = new StackPanel();

        void Pick(Place p)
        {
            ctx.Change(x => x with { Place = p }, rebuild: false);
            ctx.Flyout.CloseSheet();
        }

        void Fill(IReadOnlyList<Place> places, string title)
        {
            listTitle.Text = title.ToUpperInvariant();
            list.Children.Clear();
            if (places.Count == 0)
            {
                list.Children.Add(Ui.Text("No city found", 12, null, "Ink2"));
                return;
            }

            foreach (var p in places)
            {
                var isCurrent = current is not null && Math.Abs(p.Lat - current.Lat) < 0.01 && Math.Abs(p.Lng - current.Lng) < 0.01;
                var texts = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
                texts.Children.Add(Ui.Text(p.Name, 13, FontWeights.SemiBold));
                if (p.Country is not null) texts.Children.Add(Ui.Text(p.Country, 11.5, null, "Ink2"));
                var row = new DockPanel();
                var pin = Ui.Icon(Icons.Pin, 15);
                pin.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(pin);
                if (isCurrent)
                {
                    var check = Ui.Icon(Geometry("M20,6 L9,17 L4,12"), 15, "Accent");
                    DockPanel.SetDock(check, Dock.Right);
                    row.Children.Add(check);
                }

                row.Children.Add(texts);
                var button = Ui.Button("FlatButton", row, () => Pick(p));
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.Padding = new Thickness(10, 7, 10, 7);
                list.Children.Add(button);
            }
        }

        var search = new TextBox();
        search.SetResourceReference(FrameworkElement.StyleProperty, "Field");
        var placeholder = Ui.Text("Search for a city", 13, null, "Ink2");
        placeholder.IsHitTestVisible = false;
        placeholder.Margin = new Thickness(10, 0, 0, 0);
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        var searchBox = new Grid();
        searchBox.Children.Add(search);
        searchBox.Children.Add(placeholder);

        CancellationTokenSource? cts = null;
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        debounce.Tick += async (_, _) =>
        {
            debounce.Stop();
            var query = search.Text.Trim();
            cts?.Cancel();
            if (query.Length < 2)
            {
                message.Visibility = Visibility.Collapsed;
                Fill(Places.Popular, "Popular");
                return;
            }

            cts = new CancellationTokenSource();
            try
            {
                var results = await PlaceSearch.SearchAsync(query, cts.Token);
                message.Visibility = Visibility.Collapsed;
                Fill(results, "Results");
            }
            catch (OperationCanceledException)
            {
                // a newer search replaced this one
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
            {
                message.Text = "Search is unavailable right now. Pick from the list below.";
                message.Visibility = Visibility.Visible;
                Fill(Places.Popular, "Popular");
            }
        };
        search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            debounce.Stop();
            debounce.Start();
        };

        var locate = Ui.Button("AccentPill", Ui.Row(Orientation.Horizontal, 6, Ui.IconInherit(Icons.Locate, 14), new TextBlock { Text = "Use Windows location" }), () => { });
        locate.Click += async (_, _) =>
        {
            locate.IsEnabled = false;
            message.Text = "Asking Windows for your location…";
            message.Visibility = Visibility.Visible;
            var place = await WindowsLocation.GetAsync();
            locate.IsEnabled = true;
            if (place is null)
            {
                message.Text = "Could not get your location. Turn on location in Windows Settings > Privacy & security > Location.";
                return;
            }

            Pick(place);
        };
        locate.Margin = new Thickness(0, 8, 0, 0);
        locate.HorizontalAlignment = HorizontalAlignment.Stretch;

        body.Children.Add(searchBox);
        body.Children.Add(locate);
        message.Margin = new Thickness(4, 8, 4, 0);
        body.Children.Add(message);
        body.Children.Add(listTitle);
        body.Children.Add(list);
        Fill(Places.Popular, "Popular");
        search.Focus();
    }

    private static System.Windows.Media.Geometry Geometry(string d) => System.Windows.Media.Geometry.Parse(d);
}
