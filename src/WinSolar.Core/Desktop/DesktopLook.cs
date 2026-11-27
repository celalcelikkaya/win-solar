using System.Globalization;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;

namespace WinSolar.Core.Desktop;

/// <summary>How a switch changes the screen: painted over top to bottom, or at once (over a full-screen app).</summary>
public enum SwitchAnimation { Roller, Instant }

/// <summary>Windows' own pointer schemes: Windows Default (white) or Windows Black.</summary>
public enum Pointer { White, Black }

/// <summary>The user's own sound scheme, or none.</summary>
public enum Sounds { Normal, Off }

/// <summary>
/// What a Day or Night profile sets. <paramref name="Accent"/> is one of <see cref="Accents.Swatches"/>;
/// <paramref name="Wallpaper"/> is a picture used when the wallpaper mode is per profile.
/// </summary>
public sealed record Profile(string Accent, Pointer Pointer, Sounds Sounds, string? Wallpaper = null);

/// <summary>Everything Windows should show for one theme (apps, taskbar and Start all follow <paramref name="Theme"/>).</summary>
public sealed record Look(Theme Theme, string Accent, Pointer Pointer, Sounds Sounds);

public static class DesktopLook
{
    /// <summary>Light uses the Day profile, Dark the Night profile; pointer and sounds stay daytime unless they follow.</summary>
    public static Look For(Theme theme, AppSettings s)
    {
        var profile = theme == Theme.Light ? s.Day : s.Night;
        var daytime = s.ProfilesFollow ? profile : s.Day;
        return new Look(theme, profile.Accent, daytime.Pointer, daytime.Sounds);
    }
}

public static class Accents
{
    /// <summary>The demo's eight Windows-style swatches.</summary>
    public static IReadOnlyList<string> Swatches { get; } =
        ["#0067c0", "#6b69d6", "#8764b8", "#c239b3", "#e3008c", "#ca5010", "#0f7b0f", "#038387"];

    public static (byte R, byte G, byte B) Parse(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"Not a #rrggbb color: {hex}");
        return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
