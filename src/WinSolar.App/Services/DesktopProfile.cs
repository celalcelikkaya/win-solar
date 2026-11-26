using System.IO;
using Microsoft.Win32;
using WinSolar.App.Interop;
using WinSolar.Core.Desktop;
using WinSolar.Core.Settings;

namespace WinSolar.App.Services;

/// <summary>The user's own sounds, kept while Win Solar has them turned off.</summary>
public sealed record SavedSounds(string Scheme, Dictionary<string, string> Current);

/// <summary>
/// Applies a profile's accent color, mouse pointer and sounds. Each part is written only when it differs; a part
/// Windows refuses is logged and skipped (accent: <see cref="AccentAvailable"/> turns false and its rows hide).
/// </summary>
public sealed class DesktopProfile(string folder)
{
    private const string CursorsKey = @"Control Panel\Cursors";
    private const string SchemesKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes";
    private const string SoundsKey = @"AppEvents\Schemes";
    private const string SavedFile = "saved-sounds.json";
    private static readonly string[] CursorNames =
        ["Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person"];

    private readonly JsonFileStore files = new(folder);

    public bool AccentAvailable { get; private set; } = Native.GetAccent() is not null;

    /// <summary>Applies everything but the theme itself. Returns true when something changed.</summary>
    public bool Apply(Look look)
    {
        var changed = false;
        changed |= Try("accent", () => ApplyAccent(look.Accent));
        changed |= Try("pointer", () => ApplyPointer(look.Pointer));
        changed |= Try("sounds", () => ApplySounds(look.Sounds));
        return changed;
    }

    private static bool Try(string part, Func<bool> apply)
    {
        try
        {
            return apply();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or FormatException)
        {
            Log.Write($"Could not change the {part}: {e.Message}");
            return false;
        }
    }

    private bool ApplyAccent(string hex)
    {
        if (!AccentAvailable) return false;
        var (r, g, b) = Accents.Parse(hex);
        var abgr = 0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | r;
        if (Native.GetAccent() is { } now && (now & 0xFFFFFF) == (abgr & 0xFFFFFF)) return false;
        if (Native.SetAccent(abgr)) return true;
        AccentAvailable = false;
        Log.Write("Windows did not accept the accent color; accent colors are off");
        return false;
    }

    private static bool ApplyPointer(Pointer pointer)
    {
        var (name, scheme) = pointer == Pointer.Black ? ("Windows Black", "Windows Black") : ("Windows Default", "Windows Aero");
        using var schemes = Registry.LocalMachine.OpenSubKey(SchemesKey);
        if (schemes?.GetValue(scheme) is not string list) return false;
        var paths = list.Split(',');
        using var cursors = Registry.CurrentUser.CreateSubKey(CursorsKey);
        if (Equals(cursors.GetValue(""), name) && Equals(cursors.GetValue("Arrow"), Environment.ExpandEnvironmentVariables(paths[0]))) return false;
        for (var i = 0; i < CursorNames.Length && i < paths.Length; i++)
            cursors.SetValue(CursorNames[i], paths[i], RegistryValueKind.ExpandString);
        cursors.SetValue("", name);
        cursors.SetValue("Scheme Source", 2, RegistryValueKind.DWord);
        Native.ReloadCursors();
        return true;
    }

    private bool ApplySounds(Sounds sounds)
    {
        using var root = Registry.CurrentUser.CreateSubKey(SoundsKey);
        var scheme = root.GetValue("") as string ?? ".Default";
        var saved = files.Load<SavedSounds?>(SavedFile, () => null);
        if (sounds == Sounds.Off)
        {
            if (scheme == ".None") return false;
            files.Save(SavedFile, new SavedSounds(scheme, ReadCurrent(root)));
            WriteCurrent(root, _ => "");
            root.SetValue("", ".None");
            return true;
        }

        if (saved is null) return false; // Win Solar never turned them off: leave the user's sounds alone
        WriteCurrent(root, key => saved.Current.GetValueOrDefault(key, ""));
        root.SetValue("", saved.Scheme);
        File.Delete(Path.Combine(files.Folder, SavedFile));
        return true;
    }

    private static Dictionary<string, string> ReadCurrent(RegistryKey root)
    {
        var map = new Dictionary<string, string>();
        ForEachEvent(root, (key, current) => map[key] = current.GetValue("") as string ?? "");
        return map;
    }

    private static void WriteCurrent(RegistryKey root, Func<string, string> value) =>
        ForEachEvent(root, (key, current) => current.SetValue("", value(key), RegistryValueKind.ExpandString));

    private static void ForEachEvent(RegistryKey root, Action<string, RegistryKey> action)
    {
        using var apps = root.OpenSubKey("Apps", writable: true);
        if (apps is null) return;
        foreach (var app in apps.GetSubKeyNames())
        {
            using var appKey = apps.OpenSubKey(app, writable: true);
            if (appKey is null) continue;
            foreach (var evt in appKey.GetSubKeyNames())
            {
                using var current = appKey.OpenSubKey(evt + @"\.Current", writable: true);
                if (current is not null) action($@"{app}\{evt}", current);
            }
        }
    }
}
