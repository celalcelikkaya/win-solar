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

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StretchBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] uint[] bits, ref BitmapInfoHeader info, uint usage);

    /// <summary>
    /// Copies a screen area into a new GDI bitmap (caller deletes it). Windows excluded from capture are skipped.
    /// <paramref name="layered"/> also takes layered windows (flyout, cards); it makes the pointer flicker, so it is
    /// used only for the two pictures, never while polling.
    /// </summary>
    public static IntPtr CaptureScreen(Rect r, bool layered = true)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new System.ComponentModel.Win32Exception("GetDC failed");
        var mem = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, r.Right - r.Left, r.Bottom - r.Top);
        try
        {
            if (mem == IntPtr.Zero || bitmap == IntPtr.Zero) throw new System.ComponentModel.Win32Exception("Out of GDI memory");
            var old = SelectObject(mem, bitmap);
            var ok = BitBlt(mem, 0, 0, r.Right - r.Left, r.Bottom - r.Top, screen, r.Left, r.Top, SrcCopy | (layered ? CaptureBlt : 0));
            SelectObject(mem, old);
            if (!ok) throw new System.ComponentModel.Win32Exception("BitBlt failed");
            return bitmap;
        }
        catch
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            throw;
        }
        finally
        {
            if (mem != IntPtr.Zero) DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>A tiny (64 x 40) copy of a screen area, enough to tell when Windows has finished repainting. Cheap and no flicker.</summary>
    public static uint[] SampleScreen(Rect r)
    {
        const int w = 64, h = 40;
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new System.ComponentModel.Win32Exception("GetDC failed");
        var mem = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, w, h);
        try
        {
            if (mem == IntPtr.Zero || bitmap == IntPtr.Zero) throw new System.ComponentModel.Win32Exception("Out of GDI memory");
            var old = SelectObject(mem, bitmap);
            SetStretchBltMode(mem, 3); // COLORONCOLOR: pick pixels, no blending
            var ok = StretchBlt(mem, 0, 0, w, h, screen, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SrcCopy);
            SelectObject(mem, old);
            if (!ok) throw new System.ComponentModel.Win32Exception("StretchBlt failed");
            var pixels = new uint[w * h];
            var info = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = w, Height = -h, Planes = 1, BitCount = 32 };
            if (GetDIBits(mem, bitmap, 0, h, pixels, ref info, 0) != h) throw new System.ComponentModel.Win32Exception("GetDIBits failed");
            for (var i = 0; i < pixels.Length; i++) pixels[i] &= 0xFFFFFF;
            return pixels;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
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

    // --- roll: the frozen picture is cut away from the top, so the live screen shows above the edge ---
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    /// <summary>Limits a window to a rectangle (window coordinates, physical pixels). Windows owns the region afterwards.</summary>
    public static void ClipWindow(IntPtr hwnd, int left, int top, int right, int bottom) =>
        SetWindowRgn(hwnd, CreateRectRgn(left, top, right, bottom), true);

    // --- the soft band: our own premultiplied pixels, moved and redrawn in one call so they never drift apart ---
    public const int BandExStyle = 0x00080000 /* LAYERED */ | 0x20 /* TRANSPARENT */ | 0x8 /* TOPMOST */ | 0x80 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */;
    public const int BandStyle = unchecked((int)0x80000000) /* POPUP */ | 0x10000000 /* VISIBLE */;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32 { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size32 { public int Cx, Cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction { public byte Op, Flags, Alpha, Format; }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dcDst, ref Point32 dst, ref Size32 size, IntPtr dcSrc, ref Point32 src, uint key, ref BlendFunction blend, uint flags);

    public static void ExcludeFromCapture(IntPtr hwnd) => SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);

    /// <summary>Puts a window at the top of the topmost band (the soft band must sit above the frozen picture).</summary>
    public static void BringToTop(IntPtr hwnd) =>
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoSize | 0x0002 /* SWP_NOMOVE */ | SwpNoActivate);

    /// <summary>A width x height premultiplied BGRA surface shown in a layered window at a given screen position.</summary>
    public sealed class LayeredStrip : IDisposable
    {
        private readonly IntPtr hwnd, dc, dib, old, bits;
        private readonly int width, height;

        public LayeredStrip(IntPtr hwnd, int width, int height)
        {
            this.hwnd = hwnd;
            this.width = width;
            this.height = height;
            var screen = GetDC(IntPtr.Zero);
            dc = CreateCompatibleDC(screen);
            ReleaseDC(IntPtr.Zero, screen);
            var info = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            dib = CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
            if (dc == IntPtr.Zero || dib == IntPtr.Zero) throw new System.ComponentModel.Win32Exception("Out of GDI memory");
            old = SelectObject(dc, dib);
        }

        /// <summary>Copies <paramref name="pixels"/> in and puts the strip at (x, y) in the same call.</summary>
        public void Show(int[] pixels, int x, int y)
        {
            Marshal.Copy(pixels, 0, bits, width * height);
            var dst = new Point32 { X = x, Y = y };
            var size = new Size32 { Cx = width, Cy = height };
            var src = new Point32();
            var blend = new BlendFunction { Op = 0, Flags = 0, Alpha = 255, Format = 1 /* AC_SRC_ALPHA */ };
            UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, dc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        }

        public void Dispose()
        {
            SelectObject(dc, old);
            DeleteObject(dib);
            DeleteDC(dc);
        }
    }
}
