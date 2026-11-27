using System.Runtime.InteropServices;

namespace WinSolar.App.Interop;

/// <summary>Windows' own wallpaper API (Windows 8+), per monitor. Create and use it on the UI (STA) thread.</summary>
[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetMonitorDevicePathAt(uint index);

    uint GetMonitorDevicePathCount();

    Native.Rect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);

    void SetBackgroundColor(uint color);

    uint GetBackgroundColor();

    void SetPosition(WallpaperPosition position);

    WallpaperPosition GetPosition();

    void SetSlideshow(IShellItemArray items);

    IShellItemArray GetSlideshow();

    void SetSlideshowOptions(SlideshowOptions options, uint tickMs);

    void GetSlideshowOptions(out SlideshowOptions options, out uint tickMs);

    void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, int direction);

    SlideshowState GetStatus();

    void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

internal enum WallpaperPosition { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

[Flags]
internal enum SlideshowOptions { None = 0, Shuffle = 1 }

[Flags]
internal enum SlideshowState { None = 0, Enabled = 1, Slideshow = 2, DisabledByRemoteSession = 4 }

[ComImport]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray
{
    void BindToHandler(IntPtr bc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

    void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);

    void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);

    void GetAttributes(int attribFlags, uint sfgaoMask, out uint attribs);

    uint GetCount();

    IShellItem GetItemAt(uint index);

    void EnumItems(out IntPtr enumShellItems);
}

[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr bc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

    IShellItem GetParent();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetDisplayName(uint sigdn);

    uint GetAttributes(uint sfgaoMask);

    int Compare(IShellItem other, uint hint);
}

[ComImport]
[Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
internal class DesktopWallpaperClass;

internal static class DesktopWallpaper
{
    private const uint FileSystemPath = 0x80058000; // SIGDN_FILESYSPATH

    public static IDesktopWallpaper Create() => (IDesktopWallpaper)new DesktopWallpaperClass();

    /// <summary>Attached monitors (a detached one has no rectangle and is skipped).</summary>
    public static List<string> Monitors(IDesktopWallpaper w)
    {
        var list = new List<string>();
        var count = w.GetMonitorDevicePathCount();
        for (uint i = 0; i < count; i++)
        {
            var id = w.GetMonitorDevicePathAt(i);
            try
            {
                var r = w.GetMonitorRECT(id);
                if (r.Right > r.Left && r.Bottom > r.Top) list.Add(id);
            }
            catch (COMException)
            {
                // not attached
            }
        }

        return list;
    }

    /// <summary>The running slideshow's folders or pictures, or null when no slideshow runs.</summary>
    public static (List<string> Paths, bool Shuffle, uint TickMs)? Slideshow(IDesktopWallpaper w)
    {
        if (!w.GetStatus().HasFlag(SlideshowState.Slideshow)) return null;
        var items = w.GetSlideshow();
        var paths = new List<string>();
        for (uint i = 0; i < items.GetCount(); i++)
        {
            try { paths.Add(items.GetItemAt(i).GetDisplayName(FileSystemPath)); }
            catch (COMException) { /* not a file system item */ }
        }

        w.GetSlideshowOptions(out var options, out var tick);
        return paths.Count == 0 ? null : (paths, options.HasFlag(SlideshowOptions.Shuffle), tick);
    }

    /// <summary>Starts a slideshow of the given folders or pictures (the ones that still exist).</summary>
    public static bool StartSlideshow(IDesktopWallpaper w, IReadOnlyList<string> paths, bool shuffle, uint tickMs)
    {
        var pidls = new List<IntPtr>();
        try
        {
            foreach (var p in paths)
                if (SHParseDisplayName(p, IntPtr.Zero, out var pidl, 0, out _) == 0) pidls.Add(pidl);
            if (pidls.Count == 0) return false;
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Count, pidls.ToArray(), out var array));
            w.SetSlideshowOptions(shuffle ? SlideshowOptions.Shuffle : SlideshowOptions.None, tickMs);
            w.SetSlideshow(array);
            return true;
        }
        finally
        {
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromIDLists(uint count, IntPtr[] pidls, out IShellItemArray array);
}
