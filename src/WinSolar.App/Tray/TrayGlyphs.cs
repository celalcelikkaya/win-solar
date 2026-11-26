using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using WinSolar.App.Interop;
using WinSolar.Core.Scheduling;

namespace WinSolar.App.Tray;

public enum TrayBadge { None, Hold, Waiting, Manual }

/// <summary>Draws the tray icon at the screen's DPI so it stays crisp at 200%: a sun or a moon, plus a status dot.</summary>
internal static class TrayGlyphs
{
    private static readonly Color Sun = Color.FromArgb(0xFF, 0xB9, 0x00);
    private static readonly Color Moon = Color.FromArgb(0xB9, 0xC6, 0xFF);

    public static Icon Render(Theme theme, TrayBadge badge, double alpha = 1)
    {
        var size = (int)Math.Round(16 * Native.GetDpiForSystem() / 96.0);
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float s = size;
            if (theme == Theme.Light) DrawSun(g, s); else DrawMoon(g, s);
            if (badge != TrayBadge.None) DrawBadge(g, s, badge);
        }

        using var final = alpha >= 1 ? (Bitmap)bmp.Clone() : Fade(bmp, alpha);
        var handle = final.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    private static Bitmap Fade(Bitmap source, double alpha)
    {
        var faded = new Bitmap(source.Width, source.Height);
        using var g = Graphics.FromImage(faded);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)alpha });
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return faded;
    }

    private static void DrawSun(Graphics g, float s)
    {
        float c = s / 2, r = s * 0.21f;
        using var brush = new SolidBrush(Sun);
        g.FillEllipse(brush, c - r, c - r, 2 * r, 2 * r);
        using var pen = new Pen(Sun, Math.Max(1f, s / 12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            float x1 = c + (float)Math.Cos(a) * s * 0.32f, y1 = c + (float)Math.Sin(a) * s * 0.32f;
            float x2 = c + (float)Math.Cos(a) * s * 0.44f, y2 = c + (float)Math.Sin(a) * s * 0.44f;
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    private static void DrawMoon(Graphics g, float s)
    {
        using var disc = new GraphicsPath();
        disc.AddEllipse(s * 0.12f, s * 0.12f, s * 0.76f, s * 0.76f);
        using var bite = new GraphicsPath();
        bite.AddEllipse(s * 0.36f, s * 0.02f, s * 0.66f, s * 0.66f);
        using var region = new Region(disc);
        region.Exclude(bite);
        using var brush = new SolidBrush(Moon);
        g.FillRegion(brush, region);
    }

    private static void DrawBadge(Graphics g, float s, TrayBadge badge)
    {
        var color = badge switch
        {
            TrayBadge.Hold => Color.FromArgb(0xF7, 0xB5, 0x00),
            TrayBadge.Waiting => Color.FromArgb(0xFF, 0x8A, 0x3D),
            _ => Color.FromArgb(0x4C, 0xC2, 0xFF),
        };
        float d = s * 0.42f, x = s - d, y = s - d;
        using var ring = new SolidBrush(Color.FromArgb(0x20, 0x20, 0x20));
        g.FillEllipse(ring, x - 1, y - 1, d + 2, d + 2);
        using var dot = new SolidBrush(color);
        g.FillEllipse(dot, x, y, d, d);
    }
}
