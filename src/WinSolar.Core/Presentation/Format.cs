using System.Globalization;

namespace WinSolar.Core.Presentation;

/// <summary>Text shown in the UI. Times are always rendered in the place's zone.</summary>
public static class Format
{
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
