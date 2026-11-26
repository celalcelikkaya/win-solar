namespace WinSolar.Core.Sun;

public enum PolarState { None, Day, Night }

/// <summary>Sun events of one civil date, all in UTC. Sunrise and sunset are null on polar days and nights.</summary>
public sealed record SunDay(DateTimeOffset? Sunrise, DateTimeOffset? Sunset, DateTimeOffset Noon, PolarState Polar);
