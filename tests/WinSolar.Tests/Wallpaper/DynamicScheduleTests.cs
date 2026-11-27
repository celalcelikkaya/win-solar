using WinSolar.Core.Scheduling;
using WinSolar.Core.Wallpaper;
using static WinSolar.Tests.TestTime;

namespace WinSolar.Tests.Wallpaper;

public class DynamicScheduleTests
{
    // Read from D:\WinSolarTest\Riverside by ArseniXC.heic (dynamicwallpaper.club):
    // {ti: [{t 0.2917 i 0}, {t 0 i 1}, {t 0.75 i 2}, {t 0.8333 i 3}], ap: {d 0, l 0}}
    private const string RiversideH24 =
        "YnBsaXN0MDDSAQIDElJ0aVJhcKQECQwP0gUGBwhRdFFpIz/SqqqqqqqrEADSBQYKCyMAAAAAAAAAABAB0gUGDQ4jP+gAAAAAAAAQAtIFBhARIz/qqqqqqqqrEAPSExQICFFkUWwIDRATGB0fISosMTo8QUpMUVpcYWMAAAAAAAABAQAAAAAAAAAVAAAAAAAAAAAAAAAAAAAAZQ==";

    // {ti: [{t 0.25 i 0}, {t 0.75 i 1}], ap: {d 1, l 0}}
    private const string TwoItemH24 =
        "YnBsaXN0MDDSAQIDCFJhcFJ0adIEBQYHUWRRbBABEACiCQ3SCgsHDFFpUXQjP9AAAAAAAADSCgsGDiM/6AAAAAAAAAgNEBMYGhweICMoKiw1OgAAAAAAAAEBAAAAAAAAAA8AAAAAAAAAAAAAAAAAAABD";

    // {si: [{a -30 z 0 i 0}, {a -2 z 95 i 1}, {a 30 z 100 i 2}, {a 60 z 180 i 3}, {a 30 z 260 i 4}], ap: {d 0, l 3}}
    private const string Solar =
        "YnBsaXN0MDDSAQIDCFJhcFJzadIEBQYHUWRRbBAAEAOlCREUGBvUCgsMDQ4GDxBRYVFpUW9ReiPAPgAAAAAAABABIwAAAAAAAAAA1AoLDA0SDwYTI8AAAAAAAAAAI0BXwAAAAAAA1AoLDA0VFgYXI0A+AAAAAAAAEAIjQFkAAAAAAADUCgsMDRkHBhojQE4AAAAAAAAjQGaAAAAAAADUCgsMDRUcBh0QBCNAcEAAAAAAAAgNEBMYGhweICYvMTM1N0BCS1RdZm94eoOMlZ6nqQAAAAAAAAEBAAAAAAAAAB4AAAAAAAAAAAAAAAAAAACy";

    // {ap: {d 1, l 0}}
    private const string Apr =
        "YnBsaXN0MDDRAQJSYXDSAwQFBlFkUWwQARAACAsOExUXGQAAAAAAAAEBAAAAAAAAAAcAAAAAAAAAAAAAAAAAAAAb";

    private static string Xmp(string key, string value) =>
        $"""<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description rdf:about="" xmlns:apple_desktop="http://ns.apple.com/namespace/1.0/" apple_desktop:{key}="{value}"/></rdf:RDF></x:xmpmeta>""";

    private static string XmpElement(string key, string value) =>
        $"""<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF><rdf:Description xmlns:apple_desktop="http://ns.apple.com/namespace/1.0/"><apple_desktop:{key}>{value}</apple_desktop:{key}></rdf:Description></rdf:RDF></x:xmpmeta>""";

    [Fact]
    public void Riverside_h24_is_read()
    {
        var s = DynamicSchedule.FromXmp(Xmp("h24", RiversideH24));
        Assert.Equal(DynamicKind.Time, s.Kind);
        Assert.Equal(4, s.Time.Count);
        Assert.Equal(new[] { 1, 0, 2, 3 }, s.Time.Select(t => t.Index));
        Assert.Equal(0.0, s.Time[0].DayFraction, 4);
        Assert.Equal(0.2917, s.Time[1].DayFraction, 4);
        Assert.Equal(0.75, s.Time[2].DayFraction, 4);
        Assert.Equal(0.8333, s.Time[3].DayFraction, 4);
        Assert.Equal(0, s.Light);
        Assert.Equal(0, s.Dark);
    }

    [Fact]
    public void H24_picks_latest_item_before_now()
    {
        var s = DynamicSchedule.FromXmp(Xmp("h24", RiversideH24));
        Assert.Equal(0, FrameChooser.Frame(s, At("2026-11-27 12:00", Istanbul.TimeZoneId), Istanbul, Theme.Light));
        Assert.Equal(2, FrameChooser.Frame(s, At("2026-11-27 19:00", Istanbul.TimeZoneId), Istanbul, Theme.Light));
        Assert.Equal(3, FrameChooser.Frame(s, At("2026-11-27 21:00", Istanbul.TimeZoneId), Istanbul, Theme.Light));
        Assert.Equal(1, FrameChooser.Frame(s, At("2026-11-27 03:00", Istanbul.TimeZoneId), Istanbul, Theme.Light));
    }

    [Fact]
    public void H24_before_first_item_wraps_to_last()
    {
        var s = DynamicSchedule.FromXmp(Xmp("h24", TwoItemH24));
        Assert.Equal(1, FrameChooser.Frame(s, At("2026-11-27 02:00", Istanbul.TimeZoneId), Istanbul, Theme.Light));
        Assert.Equal(0, FrameChooser.Frame(s, At("2026-11-27 06:00", Istanbul.TimeZoneId), Istanbul, Theme.Light));
    }

    [Fact]
    public void Next_change_is_the_next_frame_boundary()
    {
        var s = DynamicSchedule.FromXmp(Xmp("h24", TwoItemH24));
        var zone = Istanbul.TimeZoneId;
        Assert.Equal(At("2026-11-27 18:00", zone), FrameChooser.NextChange(s, At("2026-11-27 12:00", zone), Istanbul, Theme.Light));
        Assert.Equal(At("2026-11-28 06:00", zone), FrameChooser.NextChange(s, At("2026-11-27 20:00", zone), Istanbul, Theme.Light));
        Assert.Equal(At("2026-11-27 06:00", zone), FrameChooser.NextChange(s, At("2026-11-27 02:00", zone), Istanbul, Theme.Light));
    }

    [Fact]
    public void Solar_is_read_from_an_element()
    {
        var s = DynamicSchedule.FromXmp(XmpElement("solar", Solar));
        Assert.Equal(DynamicKind.Solar, s.Kind);
        Assert.Equal(5, s.Solar.Count);
        Assert.Equal(new SolarFrame(60, 180, 3), s.Solar[3]);
        Assert.Equal(3, s.Light);
        Assert.Equal(0, s.Dark);
    }

    [Fact]
    public void Solar_picks_closest_sun_position()
    {
        var s = DynamicSchedule.FromXmp(Xmp("solar", Solar));
        var zone = Istanbul.TimeZoneId;
        // Late November in Istanbul: noon ≈ 28°, so the 30° frames win; morning and afternoon are told apart.
        Assert.Equal(2, FrameChooser.Frame(s, At("2026-11-27 11:00", zone), Istanbul, Theme.Light));
        Assert.Equal(4, FrameChooser.Frame(s, At("2026-11-27 14:00", zone), Istanbul, Theme.Light));
        Assert.Equal(1, FrameChooser.Frame(s, At("2026-11-27 07:20", zone), Istanbul, Theme.Light));
        Assert.Equal(0, FrameChooser.Frame(s, At("2026-11-27 01:00", zone), Istanbul, Theme.Dark));
    }

    [Fact]
    public void Solar_works_in_polar_night()
    {
        var s = DynamicSchedule.FromXmp(Xmp("solar", Solar));
        var zone = Tromso.TimeZoneId;
        // Midwinter in Tromsø the sun stays below the horizon: the night frames show, nothing throws.
        Assert.Equal(1, FrameChooser.Frame(s, At("2026-12-21 11:00", zone), Tromso, Theme.Dark));
        Assert.Equal(0, FrameChooser.Frame(s, At("2026-12-21 00:00", zone), Tromso, Theme.Dark));
        Assert.NotNull(FrameChooser.NextChange(s, At("2026-12-21 00:00", zone), Tromso, Theme.Dark));
    }

    [Fact]
    public void Solar_next_change_is_when_the_frame_differs()
    {
        var s = DynamicSchedule.FromXmp(Xmp("solar", Solar));
        var now = At("2026-11-27 11:00", Istanbul.TimeZoneId);
        var next = FrameChooser.NextChange(s, now, Istanbul, Theme.Light)!.Value;
        Assert.True(next > now);
        Assert.Equal(2, FrameChooser.Frame(s, next.AddMinutes(-1), Istanbul, Theme.Light));
        Assert.NotEqual(2, FrameChooser.Frame(s, next, Istanbul, Theme.Light));
    }

    [Fact]
    public void Apr_follows_theme()
    {
        var s = DynamicSchedule.FromXmp(Xmp("apr", Apr));
        Assert.Equal(DynamicKind.Appearance, s.Kind);
        var noon = At("2026-11-27 12:00", Istanbul.TimeZoneId);
        Assert.Equal(0, FrameChooser.Frame(s, noon, Istanbul, Theme.Light));
        Assert.Equal(1, FrameChooser.Frame(s, noon, Istanbul, Theme.Dark));
        Assert.Null(FrameChooser.NextChange(s, noon, Istanbul, Theme.Light));
    }

    [Fact]
    public void FromXmp_rejects_plain_xmp() =>
        Assert.Throws<FormatException>(() => DynamicSchedule.FromXmp(
            """<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF><rdf:Description tiff:Make="Apple"/></rdf:RDF></x:xmpmeta>"""));

    [Fact]
    public void FromXmp_rejects_a_damaged_value() =>
        Assert.Throws<FormatException>(() => DynamicSchedule.FromXmp(Xmp("h24", "YnBsaXN0MDDSAQID")));
}
