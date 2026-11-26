using System.Runtime.InteropServices;

namespace WinSolar.App.Interop;

/// <summary>The few Win32 calls Win Solar needs. No PowerShell, no child processes.</summary>
internal static partial class Native
{
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

    /// <summary>Tells every window that a setting changed (taskbar and Explorer repaint right away).</summary>
    public static void BroadcastSettingChange(string area) =>
        SendMessageTimeout(HwndBroadcast, WmSettingChange, UIntPtr.Zero, area, SmtoAbortIfHung, 200, out _);

    // --- full screen ---
    public const int QunsBusy = 2;
    public const int QunsRunningD3DFullScreen = 3;
    public const int QunsPresentationMode = 4;

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, char[] className, int maxCount);

    public const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    // --- tray ---
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    // --- windows that never take focus (notification card, OSD) ---
    public const int GwlExStyle = -20;
    public const long WsExNoActivate = 0x08000000;
    public const long WsExToolWindow = 0x00000080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    public static void MakeNoActivate(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
    }

    // --- flyout frame and motion (DWM rounds, borders and shadows it; the window itself slides and fades) ---
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    private const long WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x2;
    private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Windows 11 rounded corners (ignored on Windows 10, which keeps square corners).</summary>
    public static void RoundCorners(IntPtr hwnd)
    {
        var round = DwmwcpRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
    }

    /// <summary>Colour of the thin border DWM draws around a rounded window.</summary>
    public static void SetBorderColor(IntPtr hwnd, System.Windows.Media.Color color)
    {
        var colorRef = color.R | (color.G << 8) | (color.B << 16);
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref colorRef, sizeof(int));
    }

    /// <summary>Lets the whole window fade with one constant alpha that DWM applies (no per-pixel copy).</summary>
    public static void MakeLayered(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExLayered));
    }

    public static void SetAlpha(IntPtr hwnd, double opacity) =>
        SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), LwaAlpha);

    /// <summary>Moves a window in device pixels without resizing, activating or restacking it.</summary>
    public static void MoveWindow(IntPtr hwnd, int x, int y) =>
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);

    // --- taskbar position ---
    public const uint AbmGetTaskbarPos = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct AppBarData
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Rc;
        public IntPtr LParam;
    }

    [DllImport("shell32.dll")]
    public static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    // --- global shortcut ---
    public const int WmHotkey = 0x0312;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // --- notification state ---
    public const int QunsNotPresent = 1;
    public const int QunsQuietTime = 6;
}
