using System.Collections.Concurrent;

namespace WinSolar.Core;

/// <summary>Time-zone helpers. All schedule math happens in the place's zone, never the PC's.</summary>
public static class Zones
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Cache = new();

    /// <summary>Forgets cached zones (after Windows' time-zone data or the PC zone changed).</summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>Finds a zone by IANA or Windows id (.NET 8 converts between them); unknown ids fall back to UTC.</summary>
    public static TimeZoneInfo Find(string id) => Cache.GetOrAdd(id, static key =>
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(key); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    });

    /// <summary>A wall-clock time in <paramref name="zone"/> as an instant. Times in a DST gap move forward an hour.</summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>The calendar date of an instant in <paramref name="zone"/>.</summary>
    public static DateOnly LocalDate(DateTimeOffset at, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
}
