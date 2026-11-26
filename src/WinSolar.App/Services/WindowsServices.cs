using Microsoft.Win32;
using WinSolar.App.Interop;
using WinSolar.Core.Engine;
using WinSolar.Core.Scheduling;

namespace WinSolar.App.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

/// <summary>Apps and system theme through the Personalize registry key, then one broadcast. Writes only what changed.</summary>
public sealed class WindowsTheme : IThemeApplier
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly string[] Values = ["AppsUseLightTheme", "SystemUsesLightTheme"];

    public Theme Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0 ? Theme.Dark : Theme.Light;
    }

    public void Apply(Theme theme)
    {
        var value = theme == Theme.Light ? 1 : 0;
        var changed = false;
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
        {
            foreach (var name in Values)
            {
                if (key.GetValue(name) is int current && current == value) continue;
                key.SetValue(name, value, RegistryValueKind.DWord);
                changed = true;
            }
        }

        // The broadcast can wait on slow windows; never block the UI thread for it.
        if (changed) _ = Task.Run(() => Native.BroadcastSettingChange("ImmersiveColorSet"));
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
