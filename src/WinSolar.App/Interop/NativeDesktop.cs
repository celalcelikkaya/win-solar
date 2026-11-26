using System.Runtime.InteropServices;

namespace WinSolar.App.Interop;

/// <summary>Win32 calls for desktop profiles (accent, pointer) and the switch overlay (screen capture).</summary>
internal static partial class Native
{
    // --- accent: uxtheme's own color preference (the registry alone is ignored by Windows 11) ---
    [StructLayout(LayoutKind.Sequential)]
    public struct ColorPreference
    {
        public uint Start;
        public uint Accent;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ColorPreferenceCall(ref ColorPreference preference, int flag);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

    private static ColorPreferenceCall? getColorPreference;
    private static ColorPreferenceCall? setColorPreference;

    private static bool LoadColorPreference()
    {
        if (getColorPreference is not null && setColorPreference is not null) return true;
        var module = LoadLibrary("uxtheme.dll");
        if (module == IntPtr.Zero) return false;
        var get = GetProcAddress(module, new IntPtr(120)); // GetUserColorPreference
        var set = GetProcAddress(module, new IntPtr(122)); // SetUserColorPreference
        if (get == IntPtr.Zero || set == IntPtr.Zero) return false;
        getColorPreference = Marshal.GetDelegateForFunctionPointer<ColorPreferenceCall>(get);
        setColorPreference = Marshal.GetDelegateForFunctionPointer<ColorPreferenceCall>(set);
        return true;
    }

    /// <summary>The current accent as 0xAABBGGRR, or null when Windows does not offer the call.</summary>
    public static uint? GetAccent()
    {
        if (!LoadColorPreference()) return null;
        var p = new ColorPreference();
        return getColorPreference!(ref p, 0) == 0 ? p.Accent : null;
    }

    /// <summary>Sets the accent (0xAABBGGRR); Windows derives the palette, Start and title bar colors itself.</summary>
    public static bool SetAccent(uint abgr)
    {
        if (!LoadColorPreference()) return false;
        var p = new ColorPreference();
        if (getColorPreference!(ref p, 0) != 0) return false;
        p.Start = abgr;
        p.Accent = abgr;
        return setColorPreference!(ref p, 1) == 0;
    }

    // --- pointer ---
    private const uint SpiSetCursors = 0x0057;
    private const uint SpifUpdateIniFile = 0x01, SpifSendChange = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint flags);

    /// <summary>Reloads the pointer scheme from <c>HKCU\Control Panel\Cursors</c>.</summary>
    public static bool ReloadCursors() => SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange);

    // --- screen capture for the switch overlay ---
    private const uint SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;
    private const uint WdaExcludeFromCapture = 0x11;
    private const long WsExTransparent = 0x00000020;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    /// <summary>Every monitor's bounds in physical pixels (the app is per-monitor DPI aware).</summary>
    public static List<Rect> Monitors()
    {
        var list = new List<Rect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref Rect r, IntPtr _) => { list.Add(r); return true; }, IntPtr.Zero);
        return list;
    }

    /// <summary>Copies a screen area into a new GDI bitmap (caller deletes it). Windows excluded from capture are skipped.</summary>
    public static IntPtr CaptureScreen(Rect r)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, r.Right - r.Left, r.Bottom - r.Top);
        var old = SelectObject(mem, bitmap);
        BitBlt(mem, 0, 0, r.Right - r.Left, r.Bottom - r.Top, screen, r.Left, r.Top, SrcCopy | CaptureBlt);
        SelectObject(mem, old);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, screen);
        return bitmap;
    }

    /// <summary>Topmost, click-through, never focused, hidden from screen capture, placed exactly over <paramref name="r"/>.</summary>
    public static void MakeOverlay(IntPtr hwnd, Rect r)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow));
        SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
        SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
        SetWindowPos(hwnd, HwndTopmost, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SwpNoActivate | SwpShowWindow);
    }
}
