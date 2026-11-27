namespace WinSolar.Core.Wallpaper;

/// <summary>
/// "Lake", the dynamic wallpaper that comes with Win Solar: one scene at eight moments of the day (our own pictures,
/// made for Win Solar, so they can be shipped). Each frame is tied to a sun height and to the morning or afternoon
/// half of the day; <see cref="FrameChooser"/> shows the closest one.
/// </summary>
public static class BuiltInSet
{
    public const string Folder = "Lake";

    public static IReadOnlyList<string> Names { get; } =
        ["Night", "Dawn", "Sunrise", "Morning", "Noon", "Afternoon", "Golden hour", "Dusk"];

    public static IReadOnlyList<string> Files { get; } =
        Names.Select((n, i) => $"{i + 1}-{n.ToLowerInvariant().Replace(' ', '-')}.png").ToList();

    /// <summary>Sun height (°) and azimuth (° from north; below 180 = morning) of each frame.</summary>
    public static DynamicSchedule Schedule { get; } = new(DynamicKind.Solar,
    [
        new SolarFrame(-30, 0, 0),
        new SolarFrame(-4, 90, 1),
        new SolarFrame(3, 100, 2),
        new SolarFrame(20, 130, 3),
        new SolarFrame(45, 180, 4),
        new SolarFrame(20, 230, 5),
        new SolarFrame(3, 260, 6),
        new SolarFrame(-4, 270, 7),
    ], [], Light: 4, Dark: 0);
}
