using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinSolar.App.Interop;
using WinSolar.App.Services;
using WinSolar.Core;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;
using WinSolar.Core.Wallpaper;

namespace WinSolar.App.Wallpapers;

/// <summary>
/// The user's own wallpaper: a picture per monitor and its position, or a slideshow (folders or pictures, shuffle,
/// interval); and the picture Win Solar applied last.
/// </summary>
public sealed record SavedWallpaper(
    Dictionary<string, string> Own, int? Position, string? Applied,
    List<string>? Slideshow = null, bool Shuffle = false, uint SlideshowMs = 0);

/// <summary>
/// Shows the wallpaper <see cref="WallpaperChoice"/> picks (a frame of the active set, or a profile picture) on every
/// monitor with Fill, only when it changes, with its own timer to the next frame change. Before Win Solar first changes
/// the wallpaper, and whenever it finds one it did not set, the user's own is saved in <c>saved-wallpaper.json</c>;
/// <see cref="Restore"/> puts it back.
/// </summary>
public sealed class WallpaperService : IDisposable
{
    private const string SavedFile = "saved-wallpaper.json";

    private readonly JsonFileStore files;
    private readonly Func<AppSettings> settings;
    private readonly Func<Place> place;
    private readonly DispatcherTimer timer;
    private (string Name, WallpaperSet Set)? cache;
    private Theme theme = Theme.Light;
    private string? shown;

    public WallpaperService(string appFolder, Func<AppSettings> settings, Func<Place> place, Dispatcher dispatcher)
    {
        files = new JsonFileStore(appFolder);
        this.settings = settings;
        this.place = place;
        Folder = Path.Combine(appFolder, "Wallpapers");
        Importer = new WallpaperImporter(Folder);
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Update(theme, force: false);
        };
        Saved = files.Load<SavedWallpaper?>(SavedFile, () => null);
        shown = Saved?.Applied;
    }

    public string Folder { get; }

    public WallpaperImporter Importer { get; }

    /// <summary>The user's own wallpaper, once Win Solar has saved it.</summary>
    public SavedWallpaper? Saved { get; private set; }

    /// <summary>The picture Win Solar shows now, if any.</summary>
    public string? Showing => WallpaperChoice.For(settings(), Active(), DateTimeOffset.Now, place(), theme)?.Path;

    public event Action? Changed;

    /// <summary>Imported sets, newest first.</summary>
    public List<(WallpaperSet Set, string Folder)> Sets()
    {
        var list = new List<(WallpaperSet, string, DateTime)>();
        if (!Directory.Exists(Folder)) return [];
        foreach (var dir in Directory.EnumerateDirectories(Folder))
        {
            if (Path.GetFileName(dir).StartsWith('.')) continue;
            if (Load(dir) is { } set) list.Add((set, dir, Directory.GetCreationTimeUtc(dir)));
        }

        return list.OrderByDescending(s => s.Item3).Select(s => (s.Item1, s.Item2)).ToList();
    }

    public (WallpaperSet Set, string Folder)? Active()
    {
        var name = settings().ActiveSet;
        if (string.IsNullOrEmpty(name)) return null;
        var dir = Path.Combine(Folder, name);
        if (cache is { } c && c.Name == name) return (c.Set, dir);
        if (Load(dir) is not { } set) return null;
        cache = (name, set);
        return (set, dir);
    }

    /// <summary>Re-checks for the theme the engine applied (cheap when nothing changed).</summary>
    public void Update(Theme current) => Update(current, force: false);

    /// <summary>Settings or sets changed: apply again even if the picture looks the same.</summary>
    public void Refresh()
    {
        cache = null;
        Update(theme, force: true);
    }

    private void Update(Theme current, bool force)
    {
        theme = current;
        var s = settings();
        var now = DateTimeOffset.Now;
        WallpaperTarget? target;
        try
        {
            target = WallpaperChoice.For(s, Active(), now, place(), current);
        }
        catch (Exception e) when (e is IOException or FormatException or ArgumentException)
        {
            Log.Write($"Wallpaper: {e.Message}");
            target = null;
        }

        timer.Stop();
        if (target is null) return;
        if (target.Next is { } next)
        {
            var wait = next - now;
            timer.Interval = wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
            timer.Start();
        }

        if (!force && string.Equals(target.Path, shown, StringComparison.OrdinalIgnoreCase)) return;
        Show(target.Path);
    }

    private void Show(string path)
    {
        if (!File.Exists(path))
        {
            Log.Write($"Wallpaper missing: {path}");
            return;
        }

        try
        {
            var w = DesktopWallpaper.Create();
            var monitors = DesktopWallpaper.Monitors(w);
            var current = monitors.ToDictionary(m => m, m => Try(() => w.GetWallpaper(m)));
            // First change, or the user picked another wallpaper in Windows: that is theirs to restore later.
            var own = current.Where(c => WallpaperChoice.IsUsersOwn(c.Value, Saved?.Applied, Folder))
                .ToDictionary(c => c.Key, c => c.Value!);
            if (Saved is null || own.Count > 0)
            {
                // Setting a picture ends a slideshow, so a running one is saved too (Win Solar never starts one).
                var show = DesktopWallpaper.Slideshow(w);
                Saved = new SavedWallpaper(own, (int)w.GetPosition(), Saved?.Applied, show?.Paths, show?.Shuffle ?? false, show?.TickMs ?? 0);
            }

            if (current.Values.Any(c => !string.Equals(c, path, StringComparison.OrdinalIgnoreCase)) || monitors.Count == 0)
            {
                w.SetPosition(WallpaperPosition.Fill);
                foreach (var m in monitors) w.SetWallpaper(m, path);
            }

            shown = path;
            Saved = Saved! with { Applied = path };
            files.Save(SavedFile, Saved);
            Changed?.Invoke();
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            Log.Write($"Could not set the wallpaper: {e.Message}");
        }
    }

    /// <summary>Puts the user's own wallpaper back (the caller sets the mode to Off).</summary>
    public void Restore()
    {
        timer.Stop();
        if (Saved is null) return;
        try
        {
            var w = DesktopWallpaper.Create();
            if (Saved.Position is { } p && Enum.IsDefined((WallpaperPosition)p)) w.SetPosition((WallpaperPosition)p);
            var slideshow = Saved.Slideshow is { Count: > 0 } paths
                && DesktopWallpaper.StartSlideshow(w, paths, Saved.Shuffle, Saved.SlideshowMs);
            if (!slideshow)
            {
                foreach (var m in DesktopWallpaper.Monitors(w))
                {
                    var own = Saved.Own.TryGetValue(m, out var o) ? o : Saved.Own.Values.FirstOrDefault();
                    if (own is not null && (own.Length == 0 || File.Exists(own))) w.SetWallpaper(m, own);
                }
            }

            shown = null;
            Saved = Saved with { Applied = null };
            files.Save(SavedFile, Saved);
            Changed?.Invoke();
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            Log.Write($"Could not restore the wallpaper: {e.Message}");
        }
    }

    /// <summary>Deletes an imported set (the caller clears <see cref="AppSettings.ActiveSet"/> if it was active).</summary>
    public void Delete(string folder)
    {
        if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(folder)!), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(folder, recursive: true);
            cache = null;
            Changed?.Invoke();
        }
    }

    private static WallpaperSet? Load(string dir)
    {
        if (!File.Exists(Path.Combine(dir, WallpaperSet.FileName))) return null;
        var set = new JsonFileStore(dir).Load<WallpaperSet?>(WallpaperSet.FileName, () => null);
        return set is { Frames.Count: > 0 } ? set : null;
    }

    private static string? Try(Func<string> get)
    {
        try { return get(); }
        catch (COMException) { return null; }
    }

    public void Dispose() => timer.Stop();
}
