using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinSolar.App.Services;
using WinSolar.App.Styling;
using WinSolar.App.Wallpapers;
using WinSolar.Core.Desktop;
using WinSolar.Core.Presentation;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;
using WinSolar.Core.Wallpaper;
using static WinSolar.App.SettingsUi.SettingsSheet;

namespace WinSolar.App.SettingsUi;

/// <summary>The Wallpaper section of Settings, the per-profile picture row, and the import sheet.</summary>
public static class WallpaperUi
{
    public static void Section(StackPanel body, SheetContext ctx)
    {
        var s = ctx.Settings;
        var service = ctx.Host.Wallpaper;
        body.Children.Add(SettingsSheet.Section("Wallpaper"));
        var rows = new List<UIElement>
        {
            Row("Wallpaper",
                s.Wallpaper switch
                {
                    WallpaperMode.Sun => "One scene that changes through the day",
                    WallpaperMode.PerProfile => "Each profile has its own picture",
                    _ => "Win Solar leaves your wallpaper alone",
                },
                Segmented<WallpaperMode>([("Moves with the sun", WallpaperMode.Sun), ("Per profile", WallpaperMode.PerProfile), ("Off", WallpaperMode.Off)],
                    s.Wallpaper, v => ctx.Change(x => x with { Wallpaper = v })), stacked: true),
        };

        if (s.Wallpaper == WallpaperMode.Sun)
        {
            var active = service.Active();
            foreach (var (set, folder) in service.Sets())
            {
                var isActive = active is { } a && a.Folder == folder;
                rows.Add(isActive ? ActiveSetRow(ctx, set, folder) : OtherSetRow(ctx, set, folder));
            }

            rows.Add(Row("Add a dynamic wallpaper", "A macOS .heic file. Pictures keep their full size.",
                Ui.Button("Pill", "Import…", () => Import(ctx, () => ctx.Flyout.OpenSheet("Settings", b => Build(b, ctx))))));
        }

        var saved = service.Saved;
        var restore = Ui.Button("Pill", "Restore", () =>
        {
            ctx.Change(x => x with { Wallpaper = WallpaperMode.Off }, rebuild: false);
            service.Restore();
            ctx.Flyout.RebuildSheet(b => Build(b, ctx));
        });
        restore.IsEnabled = saved is not null;
        rows.Add(Row("Your own wallpaper",
            saved is null ? "Saved before Win Solar first changes it" : "Saved before Win Solar changed it", restore));
        body.Children.Add(Group(rows));
    }

    /// <summary>"Wallpaper · Choose…" in a profile group (per-profile mode only).</summary>
    public static FrameworkElement ProfileRow(SheetContext ctx, Theme theme)
    {
        var p = theme == Theme.Light ? ctx.Settings.Day : ctx.Settings.Night;
        Func<AppSettings, string?, AppSettings> set = theme == Theme.Light
            ? (x, v) => x with { Day = x.Day with { Wallpaper = v } }
            : (x, v) => x with { Night = x.Night with { Wallpaper = v } };
        var choose = Ui.Button("Pill", "Choose…", () =>
        {
            if (ctx.PickFile("Pictures|*.jpg;*.jpeg;*.png;*.bmp", p.Wallpaper) is { } file) ctx.Change(x => set(x, file));
        });
        return Row("Wallpaper", p.Wallpaper is { } path ? Path.GetFileName(path) : "None chosen. Your wallpaper stays", choose);
    }

    private static FrameworkElement ActiveSetRow(SheetContext ctx, WallpaperSet set, string folder)
    {
        var place = ctx.Host.Last?.Place;
        var theme = ctx.Host.Last?.Applied ?? Theme.Light;
        var now = DateTimeOffset.Now;
        var frame = place is null ? -1 : FrameChooser.Frame(set.Schedule, now, place, theme);
        var next = place is null ? null : FrameChooser.NextChange(set.Schedule, now, place, theme);
        var hint = frame < 0 ? Kind(set.Schedule.Kind)
            : $"Now showing {frame + 1} of {set.Frames.Count}"
              + (next is { } n && place is not null ? $" · next at {Format.Time(n, place.TimeZoneId, ctx.Settings.Hour12)}" : "")
              + $" · {Kind(set.Schedule.Kind).ToLowerInvariant()}";
        var strip = Strip(set, folder, frame);
        var remove = RemoveButton(ctx, set, folder);
        remove.HorizontalAlignment = HorizontalAlignment.Left;
        remove.Margin = new Thickness(0, 8, 0, 0);
        return Row(set.Name, hint, new StackPanel { Children = { strip, remove } }, stacked: true);
    }

    private static FrameworkElement OtherSetRow(SheetContext ctx, WallpaperSet set, string folder)
    {
        var use = Ui.Button("Pill", "Use", () => ctx.Change(x => x with { ActiveSet = Path.GetFileName(folder) }));
        return Row(set.Name, $"{set.Frames.Count} pictures · {Kind(set.Schedule.Kind).ToLowerInvariant()}",
            Ui.Row(Orientation.Horizontal, 6, use, RemoveButton(ctx, set, folder)));
    }

    private static Button RemoveButton(SheetContext ctx, WallpaperSet set, string folder)
    {
        var armed = false;
        Button? button = null;
        button = Ui.Button("Pill", "Remove", () =>
        {
            if (!armed)
            {
                armed = true;
                button!.Content = "Remove for good?";
                return;
            }

            var wasActive = ctx.Settings.ActiveSet == Path.GetFileName(folder);
            if (wasActive) ctx.Change(x => x with { ActiveSet = null }, rebuild: false);
            try
            {
                ctx.Host.Wallpaper.Delete(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write($"Could not remove {set.Name}: {e.Message}");
            }

            if (wasActive) ctx.Host.Wallpaper.Restore();
            ctx.Flyout.RebuildSheet(b => Build(b, ctx));
        });
        return button;
    }

    private static string Kind(DynamicKind kind) => kind switch
    {
        DynamicKind.Solar => "Follows the sun",
        DynamicKind.Time => "Follows the time of day",
        _ => "Follows the theme",
    };

    /// <summary>Small previews of every frame; the one showing now has an accent ring.</summary>
    private static FrameworkElement Strip(WallpaperSet set, string folder, int current)
    {
        var panel = new WrapPanel();
        for (var i = 0; i < set.Frames.Count; i++)
        {
            var image = new Image { Stretch = Stretch.UniformToFill, Source = Thumb(Path.Combine(folder, WallpaperImporter.ThumbFile(set.Frames[i]))) };
            var cell = new Border
            {
                Width = 64,
                Height = 40,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(i == current ? 2 : 0),
                Child = image,
                ClipToBounds = true,
                ToolTip = $"{i + 1}",
            };
            cell.SetResourceReference(Border.BorderBrushProperty, "Accent");
            cell.SetResourceReference(Border.BackgroundProperty, "Card2");
            panel.Children.Add(cell);
        }

        return panel;
    }

    private static BitmapImage? Thumb(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            // Loaded into memory so the file is never locked (a set can be removed while Settings is open).
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            b.DecodePixelWidth = 128;
            b.UriSource = new Uri(path);
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void Build(StackPanel body, SheetContext ctx) => SettingsSheet.Build(body, ctx);

    // ---------- import ----------

    /// <summary>Picks a .heic file, shows what it holds, imports it, then offers "Use this wallpaper".</summary>
    public static void Import(SheetContext ctx, Action back)
    {
        if (ctx.PickFile("Dynamic wallpapers (*.heic)|*.heic;*.heif", null) is not { } file) return;
        var importer = ctx.Host.Wallpaper.Importer;

        void Show(Action<StackPanel> build) => ctx.Flyout.OpenSheet("Add a wallpaper", build);

        void Failed(string message) => Show(b =>
        {
            b.Children.Add(Message(message));
            b.Children.Add(Buttons(("Choose another file", true, () => Import(ctx, back)), ("Back", false, back)));
        });

        Show(b => b.Children.Add(Message($"Reading {Path.GetFileName(file)}…")));
        _ = Run();

        async Task Run()
        {
            ImportPreview preview;
            try
            {
                preview = await Task.Run(() => importer.Inspect(file));
            }
            catch (WallpaperImportException e)
            {
                Failed(e.Message);
                return;
            }

            Show(b =>
            {
                b.Children.Add(Group(
                    Row(preview.Name, $"{preview.Frames} pictures · {preview.Width} × {preview.Height} · {Kind(preview.Schedule.Kind).ToLowerInvariant()}", new Border()),
                    Row("Space", preview.FitsOnDisk
                        ? $"Needs about {Format.Size(preview.NeededBytes)}. {Format.Size(preview.FreeBytes)} free."
                        : $"Not enough free space: needs {Format.Size(preview.NeededBytes)}, {Format.Size(preview.FreeBytes)} free.", new Border())));
                b.Children.Add(preview.FitsOnDisk
                    ? Buttons(("Import", true, () => _ = Write(preview)), ("Cancel", false, back))
                    : Buttons(("Back", false, back)));
            });
        }

        async Task Write(ImportPreview preview)
        {
            var progress = Ui.Text("Importing… 0%", 13, FontWeights.SemiBold);
            var bar = new ProgressBar { Height = 4, Maximum = 1, Margin = new Thickness(0, 10, 0, 0) };
            bar.SetResourceReference(Control.ForegroundProperty, "Accent");
            bar.SetResourceReference(Control.BackgroundProperty, "Card2");
            bar.BorderThickness = new Thickness(0);
            Show(b => b.Children.Add(Ui.Card(new StackPanel { Children = { progress, bar } })));
            var report = new Progress<double>(v =>
            {
                progress.Text = $"Importing… {v:P0}";
                bar.Value = v;
            });
            (WallpaperSet Set, string Folder) result;
            try
            {
                result = await Task.Run(() => importer.Import(preview, report));
            }
            catch (WallpaperImportException e)
            {
                Failed(e.Message);
                return;
            }

            var name = Path.GetFileName(result.Folder);
            Show(b =>
            {
                b.Children.Add(Group(Row(result.Set.Name, $"{result.Set.Frames.Count} pictures · {Kind(result.Set.Schedule.Kind).ToLowerInvariant()}",
                    Strip(result.Set, result.Folder, -1), stacked: true)));
                b.Children.Add(Buttons(
                    ("Use this wallpaper", true, () =>
                    {
                        ctx.Change(x => x with { ActiveSet = name, Wallpaper = WallpaperMode.Sun }, rebuild: false);
                        back();
                    }),
                    ("Not now", false, back)));
            });
        }
    }

    private static FrameworkElement Message(string text)
    {
        var t = Ui.Text(text, 13);
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        return Ui.Card(t);
    }

    private static FrameworkElement Buttons(params (string Label, bool Primary, Action Click)[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (label, primary, click) in buttons)
        {
            var b = Ui.Button(primary ? "AccentPill" : "Pill", label, click);
            b.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(b);
        }

        return row;
    }
}
