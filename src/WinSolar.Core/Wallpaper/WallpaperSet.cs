using System.Text.RegularExpressions;

namespace WinSolar.Core.Wallpaper;

/// <summary>
/// An imported dynamic wallpaper: <c>%AppData%\Win Solar\Wallpapers\&lt;folder&gt;\set.json</c> next to its frames
/// (<see cref="Frames"/>[i] is the PNG of frame index i, at the original <see cref="Width"/> x <see cref="Height"/>).
/// </summary>
public sealed record WallpaperSet(string Name, DynamicSchedule Schedule, IReadOnlyList<string> Frames, int Width, int Height)
{
    public const string FileName = "set.json";

    /// <summary>The file of a frame; an index the set does not have falls back to the first frame.</summary>
    public string FrameFile(int index) => index >= 0 && index < Frames.Count ? Frames[index] : Frames[0];

    private static readonly Regex Unsafe = new("""[\\/:*?"<>|\x00-\x1F]+""", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>A Windows-safe folder name for a set's name (no reserved names, no trailing dots or spaces).</summary>
    public static string SafeFolderName(string name)
    {
        var s = Regex.Replace(Unsafe.Replace(name, " "), " {2,}", " ").Trim().TrimEnd('.', ' ').Trim();
        if (s.Trim('.').Length == 0) return "Wallpaper";
        if (s.Length > 60) s = s[..60].TrimEnd('.', ' ');
        return Reserved.Contains(s) ? s + " wallpaper" : s;
    }

    /// <summary>The safe name, or "name 2", "name 3"… when a folder of that name exists.</summary>
    public static string UniqueFolderName(string name, Func<string, bool> exists)
    {
        var safe = SafeFolderName(name);
        if (!exists(safe)) return safe;
        for (var i = 2; ; i++)
            if (!exists($"{safe} {i}")) return $"{safe} {i}";
    }
}
