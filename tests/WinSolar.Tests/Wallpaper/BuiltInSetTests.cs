using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;
using WinSolar.Core.Wallpaper;
using static WinSolar.Tests.TestTime;

namespace WinSolar.Tests.Wallpaper;

public class BuiltInSetTests
{
    private static string FrameAt(string local) =>
        BuiltInSet.Names[FrameChooser.Frame(BuiltInSet.Schedule, At(local, Istanbul.TimeZoneId), Istanbul, Theme.Light)];

    [Theory]
    [InlineData("2026-11-27 03:00", "Night")]
    [InlineData("2026-11-27 07:55", "Dawn")]
    [InlineData("2026-11-27 08:30", "Sunrise")]
    [InlineData("2026-11-27 10:30", "Morning")]
    [InlineData("2026-11-27 17:15", "Golden hour")]
    [InlineData("2026-11-27 17:55", "Dusk")]
    [InlineData("2026-11-27 22:00", "Night")]
    public void Lake_follows_the_sun_through_a_November_day_in_Istanbul(string local, string expected) =>
        Assert.Equal(expected, FrameAt(local));

    [Fact]
    public void Lake_shows_noon_on_a_high_summer_sun() =>
        Assert.Equal("Noon", BuiltInSet.Names[FrameChooser.Frame(BuiltInSet.Schedule,
            At("2026-06-21 13:15", Istanbul.TimeZoneId), Istanbul, Theme.Light)]);

    [Fact]
    public void Lake_has_one_file_per_frame() =>
        Assert.Equal(["1-night.png", "2-dawn.png", "3-sunrise.png", "4-morning.png", "5-noon.png", "6-afternoon.png",
            "7-golden-hour.png", "8-dusk.png"], BuiltInSet.Files);

    [Fact]
    public void No_imported_set_means_the_built_in_one() =>
        Assert.Equal(BuiltInSet.Folder, WallpaperChoice.ActiveSetName(new AppSettings()));

    [Fact]
    public void An_imported_set_wins_over_the_built_in_one() =>
        Assert.Equal("Riverside", WallpaperChoice.ActiveSetName(new AppSettings { ActiveSet = "Riverside" }));
}
