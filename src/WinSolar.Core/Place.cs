namespace WinSolar.Core;

/// <summary>A city the schedule is computed for. <paramref name="TimeZoneId"/> is an IANA or Windows zone id.</summary>
public sealed record Place(string Name, string? Country, double Lat, double Lng, string TimeZoneId);
