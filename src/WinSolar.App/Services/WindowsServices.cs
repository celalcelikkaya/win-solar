using Microsoft.Win32;
using WinSolar.App.Interop;
using WinSolar.App.Overlay;
using WinSolar.App.Wallpapers;
using WinSolar.Core.Desktop;
using WinSolar.Core.Engine;
using WinSolar.Core.Settings;
using WinSolar.Core.Scheduling;

namespace WinSolar.App.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

/// <summary>
/// Apps, taskbar and Start theme, the profile (accent, pointer, sounds) and the wallpaper, then one broadcast; the switch
/// itself runs behind <see cref="SwitchOverlay"/>. Writes only what changed. Outside changes are read from the apps theme.
/// </summary>
public sealed class WindowsTheme(
    Func<AppSettings> settings, DesktopProfile profile, WallpaperService wallpaper, SwitchOverlay overlay, IFullScreenProbe fullScreen)
    : IThemeApplier
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public Theme Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0 ? Theme.Dark : Theme.Light;
    }

    /// <summary>Switches behind the overlay, except over a full-screen app (a game or video must not be covered).</summary>
    public void Apply(Theme theme) =>
        overlay.Run(fullScreen.IsFullScreen() ? SwitchAnimation.Instant : settings().Animation, () => Write(theme));

    /// <summary>Settings changed: bring the profile in line with the current theme, without animation.</summary>
    public void Refresh() => Write(Read());

    private void Write(Theme theme)
    {
        var look = DesktopLook.For(theme, settings());
        var changed = false;
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
        {
            changed |= SetFlag(key, "AppsUseLightTheme", look.Theme == Theme.Light);
            changed |= SetFlag(key, "SystemUsesLightTheme", look.Theme == Theme.Light);
        }

        changed |= profile.Apply(look);
        wallpaper.Update(theme); // behind the overlay too, so a theme-following picture rolls in with the theme

        // The broadcast can wait on slow windows; never block the UI thread for it.
        if (changed) _ = Task.Run(() => Native.BroadcastSettingChange("ImmersiveColorSet"));
    }

    private static bool SetFlag(RegistryKey key, string name, bool on)
    {
        var value = on ? 1 : 0;
        if (key.GetValue(name) is int current && current == value) return false;
        key.SetValue(name, value, RegistryValueKind.DWord);
        return true;
    }
}

/// <summary>True while a full-screen game, video or slideshow is in front.</summary>
public sealed class FullScreenProbe : IFullScreenProbe
{
    public bool IsFullScreen()
    {
        if (Native.SHQueryUserNotificationState(out var state) == 0 &&
            state is Native.QunsBusy or Native.QunsRunningD3DFullScreen or Native.QunsPresentationMode)
            return true;

        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == Native.GetShellWindow() || hwnd == Native.GetDesktopWindow()) return false;

        var cls = new char[64];
        var len = Native.GetClassName(hwnd, cls, cls.Length);
        var name = new string(cls, 0, Math.Max(0, len));
        if (name is "WorkerW" or "Progman" or "Shell_TrayWnd") return false;

        if (!Native.GetWindowRect(hwnd, out var r)) return false;
        var info = new Native.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, Native.MonitorDefaultToNearest), ref info)) return false;
        var m = info.Monitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }
}
