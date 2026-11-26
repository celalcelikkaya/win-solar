using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using WinSolar.App.Interop;

namespace WinSolar.App.Services;

public enum Edge { Left, Top, Right, Bottom }

/// <summary>Where the taskbar is and the screen area next to it, in WPF units (DIP).</summary>
public static class TaskbarEdge
{
    public static (Edge Edge, Rect WorkArea) Get()
    {
        var data = new Native.AppBarData { Size = (uint)Marshal.SizeOf<Native.AppBarData>() };
        var edge = Native.SHAppBarMessage(Native.AbmGetTaskbarPos, ref data) == UIntPtr.Zero
            ? Edge.Bottom
            : data.Edge switch { 0 => Edge.Left, 1 => Edge.Top, 2 => Edge.Right, _ => Edge.Bottom };
        return (edge, SystemParameters.WorkArea);
    }
}

/// <summary>Start with Windows through the per-user Run key (no admin rights).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Win Solar";

    private static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    public static bool IsOn()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string v && v.Equals(Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(ValueName, Command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>True when running from the installed folder, not from a build output.</summary>
    public static bool IsInstalledCopy =>
        Environment.ProcessPath?.StartsWith(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Win + Shift + D, registered on a message-only window.</summary>
public sealed class Hotkey : IDisposable
{
    private const int Id = 0x5701;
    private const uint VkD = 0x44;
    private readonly HwndSource source;
    private readonly Action onPress;

    public Hotkey(Action onPress)
    {
        this.onPress = onPress;
        source = new HwndSource(new HwndSourceParameters("WinSolarHotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        source.AddHook(Hook);
    }

    public bool IsRegistered { get; private set; }

    /// <summary>False when another app already owns the combination.</summary>
    public bool Register()
    {
        if (!IsRegistered) IsRegistered = Native.RegisterHotKey(source.Handle, Id, Native.ModWin | Native.ModShift | Native.ModNoRepeat, VkD);
        return IsRegistered;
    }

    public void Unregister()
    {
        if (IsRegistered) Native.UnregisterHotKey(source.Handle, Id);
        IsRegistered = false;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WmHotkey && wParam.ToInt32() == Id)
        {
            handled = true;
            onPress();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        source.RemoveHook(Hook);
        source.Dispose();
    }
}
