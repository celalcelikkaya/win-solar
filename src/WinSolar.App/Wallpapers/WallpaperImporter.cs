using System.IO;
using System.Text;
using ImageMagick;
using WinSolar.App.Services;
using WinSolar.Core.Presentation;
using WinSolar.Core.Settings;
using WinSolar.Core.Wallpaper;

namespace WinSolar.App.Wallpapers;

/// <summary>An import failed; <see cref="Exception.Message"/> is short UI text.</summary>
public sealed class WallpaperImportException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What a file holds, read before anything is written, so the user sees the size and free space first.</summary>
public sealed record ImportPreview(
    string Path, string Name, DynamicSchedule Schedule, int Frames, int Width, int Height, long NeededBytes, long FreeBytes)
{
    public bool FitsOnDisk => FreeBytes >= NeededBytes;
}

/// <summary>
/// Imports a macOS dynamic wallpaper (.heic) into <c>%AppData%\Win Solar\Wallpapers\&lt;name&gt;</c>: every frame at its
/// original size as a lossless PNG (an embedded colour profile, such as Display P3, converted to sRGB), a small
/// preview of each frame, and <c>set.json</c>. Frames are decoded one at a time (Apple's sets are 16 × 6K pictures),
/// written into a temporary folder and moved into place at the end, so a failed import leaves nothing behind.
/// </summary>
public sealed class WallpaperImporter(string wallpapersFolder)
{
    public const int ThumbWidth = 320;

    public string Folder { get; } = wallpapersFolder;

    public static string ThumbFile(string frameFile) => System.IO.Path.ChangeExtension(frameFile, ".thumb.png");

    public ImportPreview Inspect(string path)
    {
        if (!HeicReadable()) throw new WallpaperImportException("This PC can't read HEIC pictures.");
        try
        {
            using var images = new MagickImageCollection();
            images.Ping(path);
            if (images.Count == 0) throw new WallpaperImportException("This file has no pictures.");
            var schedule = Schedule(images[0]);
            if (images.Count < 2) throw new WallpaperImportException("This picture is not a dynamic wallpaper.");
            var w = (int)images[0].Width;
            var h = (int)images[0].Height;
            // Lossless PNG of a photo is about half the raw RGB size; the previews are a rounding error.
            var needed = (long)w * h * 3 / 2 * images.Count + 1_000_000;
            return new ImportPreview(path, System.IO.Path.GetFileNameWithoutExtension(path), schedule,
                images.Count, w, h, needed, FreeSpace());
        }
        catch (FormatException e)
        {
            throw new WallpaperImportException(e.Message, e);
        }
        catch (MagickException e)
        {
            throw new WallpaperImportException("This file is damaged or is not a HEIC picture.", e);
        }
    }

    /// <summary>Writes the set and returns it with the folder it lives in.</summary>
    public (WallpaperSet Set, string Folder) Import(ImportPreview preview, IProgress<double>? progress = null,
        CancellationToken cancel = default)
    {
        var free = FreeSpace();
        if (free < preview.NeededBytes)
            throw new WallpaperImportException($"Not enough free space: needs {Format.Size(preview.NeededBytes)}, {Format.Size(free)} free.");

        var temp = System.IO.Path.Combine(Folder, ".import-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            var frames = new List<string>();
            for (var i = 0; i < preview.Frames; i++)
            {
                cancel.ThrowIfCancellationRequested();
                using var image = new MagickImage();
                image.Read(preview.Path, new MagickReadSettings { FrameIndex = (uint)i, FrameCount = 1 });
                if (image.GetColorProfile() is not null) image.TransformColorSpace(ColorProfiles.SRGB);
                image.RemoveProfile("xmp");
                image.RemoveProfile("exif");
                var file = $"{i:00}.png";
                image.Format = MagickFormat.Png;
                // Still lossless; ~4x faster than the default level for ~7% larger files (measured on the laptop).
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", "3");
                image.Write(System.IO.Path.Combine(temp, file));
                image.Thumbnail(ThumbWidth, 0);
                image.Write(System.IO.Path.Combine(temp, ThumbFile(file)));
                frames.Add(file);
                progress?.Report((i + 1.0) / preview.Frames);
            }

            // The folder name is the set's id (settings keep it as the active set), so set.json carries the same.
            var name = WallpaperSet.UniqueFolderName(preview.Name, n => Directory.Exists(System.IO.Path.Combine(Folder, n)));
            var set = new WallpaperSet(name, preview.Schedule, frames, preview.Width, preview.Height);
            new JsonFileStore(temp).Save(WallpaperSet.FileName, set);
            var target = System.IO.Path.Combine(Folder, name);
            Directory.Move(temp, target);
            return (set, target);
        }
        catch (Exception e)
        {
            TryDelete(temp);
            throw e switch
            {
                WallpaperImportException or OperationCanceledException => e,
                MagickException => new WallpaperImportException("This file is damaged or is not a HEIC picture.", e),
                IOException or UnauthorizedAccessException => new WallpaperImportException("The wallpaper could not be saved.", e),
                _ => e,
            };
        }
    }

    /// <summary>Removes leftovers of an import that was cut short (the app closed or crashed mid-import).</summary>
    public void CleanUp()
    {
        if (!Directory.Exists(Folder)) return;
        foreach (var dir in Directory.EnumerateDirectories(Folder, ".import-*")) TryDelete(dir);
    }

    private static DynamicSchedule Schedule(IMagickImage<byte> first)
    {
        var xmp = first.GetXmpProfile()?.ToByteArray();
        if (xmp is null) throw new WallpaperImportException("This picture is not a dynamic wallpaper.");
        return DynamicSchedule.FromXmp(Encoding.UTF8.GetString(xmp));
    }

    private static bool HeicReadable() =>
        MagickNET.SupportedFormats.Any(f => f.Format == MagickFormat.Heic && f.SupportsReading);

    private long FreeSpace()
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(Folder));
            return root is null ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write($"Could not remove {dir}: {e.Message}"); }
    }
}
