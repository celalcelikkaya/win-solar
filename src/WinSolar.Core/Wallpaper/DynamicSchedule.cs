using System.Globalization;
using System.Text.RegularExpressions;

namespace WinSolar.Core.Wallpaper;

/// <summary>How a dynamic wallpaper picks its frame: by sun position, by time of day, or by the theme only.</summary>
public enum DynamicKind { Solar, Time, Appearance }

/// <summary>A frame shot with the sun at <paramref name="Altitude"/>° and <paramref name="Azimuth"/>° (from north, clockwise).</summary>
public sealed record SolarFrame(double Altitude, double Azimuth, int Index);

/// <summary>A frame that starts at <paramref name="DayFraction"/> of the local day (0 = midnight, 0.5 = noon).</summary>
public sealed record TimeFrame(double DayFraction, int Index);

/// <summary>
/// The schedule of a macOS dynamic wallpaper, read from its XMP (<c>apple_desktop:solar</c>, <c>h24</c> or <c>apr</c>:
/// base64 binary plists). <see cref="Solar"/> and <see cref="Time"/> are sorted; <see cref="Light"/> / <see cref="Dark"/>
/// are the frames for each theme when the set says so.
/// </summary>
public sealed record DynamicSchedule(
    DynamicKind Kind, IReadOnlyList<SolarFrame> Solar, IReadOnlyList<TimeFrame> Time, int? Light, int? Dark)
{
    private static readonly Regex Value = new(
        """apple_desktop:(solar|h24|apr)\s*(?:=\s*["']([^"']*)["']|>\s*([^<]*)<)""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Reads the schedule from an image's XMP; throws <see cref="FormatException"/> when it is not a dynamic wallpaper.</summary>
    public static DynamicSchedule FromXmp(string xmp)
    {
        // The closing tag of an element matches too, with an empty value: keep the first value of each key.
        var found = Value.Matches(xmp)
            .Select(m => (Key: m.Groups[1].Value, Value: m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value))
            .Where(m => !string.IsNullOrWhiteSpace(m.Value))
            .GroupBy(m => m.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);
        // A set can carry several; the most specific one wins.
        foreach (var key in new[] { "solar", "h24", "apr" })
            if (found.TryGetValue(key, out var base64)) return Parse(key, base64);
        throw new FormatException("This picture is not a dynamic wallpaper.");
    }

    private static DynamicSchedule Parse(string key, string base64)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException e) { throw new FormatException("The wallpaper's schedule is damaged.", e); }

        if (BinaryPlist.Parse(bytes) is not Dictionary<string, object?> root)
            throw new FormatException("The wallpaper's schedule is damaged.");

        int? light = null, dark = null;
        if (root.TryGetValue("ap", out var ap) && ap is Dictionary<string, object?> apd)
        {
            light = Int(apd, "l");
            dark = Int(apd, "d");
        }

        switch (key)
        {
            case "solar":
            {
                var frames = Items(root, "si")
                    .Select(d => new SolarFrame(Number(d, "a"), Number(d, "z"), Int(d, "i") ?? throw Damaged()))
                    .OrderBy(f => f.Index).ToList();
                if (frames.Count == 0) throw Damaged();
                return new DynamicSchedule(DynamicKind.Solar, frames, [], light, dark);
            }
            case "h24":
            {
                var frames = Items(root, "ti")
                    .Select(d => new TimeFrame(Math.Clamp(Number(d, "t"), 0, 1), Int(d, "i") ?? throw Damaged()))
                    .OrderBy(f => f.DayFraction).ToList();
                if (frames.Count == 0) throw Damaged();
                return new DynamicSchedule(DynamicKind.Time, [], frames, light, dark);
            }
            default:
                if (light is null || dark is null) throw Damaged();
                return new DynamicSchedule(DynamicKind.Appearance, [], [], light, dark);
        }
    }

    private static FormatException Damaged() => new("The wallpaper's schedule is damaged.");

    private static IEnumerable<Dictionary<string, object?>> Items(Dictionary<string, object?> root, string key) =>
        root.TryGetValue(key, out var v) && v is List<object?> list
            ? list.Select(o => o as Dictionary<string, object?> ?? throw Damaged())
            : throw Damaged();

    private static double Number(Dictionary<string, object?> d, string key) => d.TryGetValue(key, out var v)
        ? v switch
        {
            long l => l,
            double x when double.IsFinite(x) => x,
            _ => throw Damaged(),
        }
        : throw Damaged();

    private static int? Int(Dictionary<string, object?> d, string key) =>
        d.TryGetValue(key, out var v) && v is long l && l is >= 0 and < 1000 ? (int)l : null;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Kind} ({Solar.Count + Time.Count} items, light {Light}, dark {Dark})");
}
