using System.Globalization;

namespace WinSolar.Core.Presentation;

/// <summary>Text shown in the UI. Times are always rendered in the place's zone.</summary>
public static class Format
{
    /// <summary>A file size in decimal units with at most one decimal under 10: "1.5 MB", "48 MB", "2.4 GB".</summary>
    public static string Size(long bytes)
    {
        var units = new[] { "KB", "MB", "GB", "TB" };
        var v = Math.Max(bytes, 1000) / 1000.0;
        var u = 0;
        while (v >= 1000 && u < units.Length - 1)
        {
            v /= 1000;
            u++;
        }

        return (v < 10 ? Math.Round(v, 1) : Math.Round(v)).ToString("0.#", CultureInfo.InvariantCulture) + " " + units[u];
    }

    /// <summary>100 → "1 h 40 m", 5 → "5 min".</summary>
    public static string Duration(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return h > 0 ? $"{h} h {m} m" : $"{m} min";
    }

    public static string Time(DateTimeOffset at, string timeZoneId, bool hour12)
    {
        var local = TimeZoneInfo.ConvertTime(at, Zones.Find(timeZoneId));
        return local.ToString(hour12 ? "h:mm tt" : "HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Like <see cref="Time"/>, plus " tomorrow" when the date differs from today's in that zone.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now, string timeZoneId, bool hour12)
    {
        var zone = Zones.Find(timeZoneId);
        var text = Time(at, timeZoneId, hour12);
        return Zones.LocalDate(at, zone) == Zones.LocalDate(now, zone) ? text : text + " tomorrow";
    }
}
