using System.Globalization;

namespace WinSolar.Tests;

/// <summary>Helpers to write test instants as local wall times in a named zone.</summary>
internal static class TestTime
{
    public static DateTimeOffset At(string localIso, string timeZoneId)
    {
        var dt = DateTime.Parse(localIso, CultureInfo.InvariantCulture);
        return Zones.ToInstant(DateOnly.FromDateTime(dt), TimeOnly.FromDateTime(dt), Zones.Find(timeZoneId));
    }

    public static TimeOnly Local(DateTimeOffset? utc, string timeZoneId) =>
        TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc!.Value, Zones.Find(timeZoneId)).DateTime);

    public static void AssertNear(TimeOnly expected, TimeOnly actual, int minutes)
    {
        var diff = Math.Abs((actual.ToTimeSpan() - expected.ToTimeSpan()).TotalMinutes);
        Assert.True(diff <= minutes, $"Expected {expected:HH:mm} ±{minutes} min, got {actual:HH:mm}");
    }

    public static readonly Place Blantyre = new("Blantyre", "Malawi", -15.7861, 35.0058, "Africa/Blantyre");
    public static readonly Place Istanbul = new("Istanbul", "Türkiye", 41.0138, 28.9497, "Europe/Istanbul");
    public static readonly Place Tromso = new("Tromsø", "Norway", 69.6496, 18.957, "Europe/Oslo");
}
