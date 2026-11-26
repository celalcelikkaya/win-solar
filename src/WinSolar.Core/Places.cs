namespace WinSolar.Core;

/// <summary>The demo's popular cities and the first-run guess used until the user picks a place.</summary>
public static class Places
{
    public static readonly IReadOnlyList<Place> Popular =
    [
        new("Blantyre", "Malawi", -15.7861, 35.0058, "Africa/Blantyre"),
        new("Lilongwe", "Malawi", -13.9669, 33.7873, "Africa/Blantyre"),
        new("Istanbul", "Türkiye", 41.0138, 28.9497, "Europe/Istanbul"),
        new("Ankara", "Türkiye", 39.9199, 32.8543, "Europe/Istanbul"),
        new("London", "United Kingdom", 51.5085, -0.1257, "Europe/London"),
        new("Berlin", "Germany", 52.5244, 13.4105, "Europe/Berlin"),
        new("Dubai", "United Arab Emirates", 25.0657, 55.1713, "Asia/Dubai"),
        new("New York", "United States", 40.7143, -74.006, "America/New_York"),
        new("Tokyo", "Japan", 35.6895, 139.6917, "Asia/Tokyo"),
        new("Sydney", "Australia", -33.8678, 151.2073, "Australia/Sydney"),
        new("Reykjavík", "Iceland", 64.1355, -21.8954, "Atlantic/Reykjavik"),
        new("Tromsø", "Norway", 69.6496, 18.957, "Europe/Oslo"),
    ];

    /// <summary>
    /// Best guess from the PC's zone: a popular city in the same zone, else one with the same UTC offset right now,
    /// else an approximate place (latitude 30, longitude from the offset) named after the zone's city.
    /// </summary>
    public static Place Guess(string ianaId, TimeSpan utcOffset, DateTimeOffset at)
    {
        var exact = Popular.FirstOrDefault(p => p.TimeZoneId == ianaId);
        if (exact is not null) return exact;

        var sameOffset = Popular.FirstOrDefault(p => Zones.Find(p.TimeZoneId).GetUtcOffset(at) == utcOffset);
        if (sameOffset is not null) return sameOffset;

        var city = ianaId.Split('/')[^1].Replace('_', ' ');
        return new Place(city, null, 30, Math.Clamp(utcOffset.TotalHours * 15, -180, 180), ianaId);
    }
}
