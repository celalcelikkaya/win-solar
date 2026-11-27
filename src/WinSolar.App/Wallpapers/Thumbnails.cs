using System.IO;
using ImageMagick;

namespace WinSolar.App.Wallpapers;

/// <summary>Preview pictures for a set's frames (same look as the importer's).</summary>
internal static class Thumbnails
{
    /// <summary>Writes a PNG preview <paramref name="width"/> px wide; returns the source picture's size.</summary>
    public static (int Width, int Height) Write(string source, string thumbPath, int width)
    {
        try
        {
            using var image = new MagickImage(source);
            var size = ((int)image.Width, (int)image.Height);
            image.Thumbnail((uint)width, 0);
            image.Write(thumbPath, MagickFormat.Png);
            return size;
        }
        catch (MagickException e)
        {
            throw new IOException($"Could not read {Path.GetFileName(source)}: {e.Message}", e);
        }
    }
}
