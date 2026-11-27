using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;

namespace WinSolar.Core.Wallpaper;

/// <summary>Moves with the sun (the active dynamic set), a picture per profile, or Off (Win Solar leaves it alone).</summary>
public enum WallpaperMode { Sun, PerProfile, Off }

/// <summary>The picture to show and when to look again (null: only when the theme, place or settings change).</summary>
public sealed record WallpaperTarget(string Path, DateTimeOffset? Next);

public static class WallpaperChoice
{
    /// <summary>What the wallpaper should be now; null means Win Solar does not touch it.</summary>
    public static WallpaperTarget? For(AppSettings s, (WallpaperSet Set, string Folder)? active, DateTimeOffset now,
        Place place, Theme theme)
    {
        switch (s.Wallpaper)
        {
            case WallpaperMode.Sun when active is { } a:
            {
                var frame = FrameChooser.Frame(a.Set.Schedule, now, place, theme);
                return new WallpaperTarget(System.IO.Path.Combine(a.Folder, a.Set.FrameFile(frame)),
                    FrameChooser.NextChange(a.Set.Schedule, now, place, theme));
            }
            case WallpaperMode.PerProfile:
            {
                var picture = (theme == Theme.Light ? s.Day : s.Night).Wallpaper;
                return string.IsNullOrWhiteSpace(picture) ? null : new WallpaperTarget(picture, null);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// True when the wallpaper Windows shows now was set by the user, not by Win Solar: not the picture Win Solar
    /// applied last and not one of its imported frames. "" (a solid colour) is the user's too; null is unknown.
    /// </summary>
    public static bool IsUsersOwn(string? current, string? applied, string wallpapersFolder)
    {
        if (current is null) return false;
        if (current.Length == 0) return true;
        if (applied is not null && string.Equals(current, applied, StringComparison.OrdinalIgnoreCase)) return false;
        var folder = wallpapersFolder.TrimEnd('\\', '/') + "\\";
        return !current.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }
}
