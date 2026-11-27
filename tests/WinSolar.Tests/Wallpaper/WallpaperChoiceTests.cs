using WinSolar.Core.Desktop;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;
using WinSolar.Core.Wallpaper;
using static WinSolar.Tests.TestTime;

namespace WinSolar.Tests.Wallpaper;

public class WallpaperChoiceTests
{
    private static readonly WallpaperSet TwoFrames = new("Riverside",
        new DynamicSchedule(DynamicKind.Time, [], [new TimeFrame(0.25, 0), new TimeFrame(0.75, 1)], 0, 1),
        ["00.png", "01.png"], 100, 100);

    private static readonly (WallpaperSet, string) Active = (TwoFrames, @"C:\W\Riverside");

    private static DateTimeOffset Noon => At("2026-11-27 12:00", Istanbul.TimeZoneId);

    [Fact]
    public void Sun_mode_shows_the_frame_of_the_active_set()
    {
        var t = WallpaperChoice.For(new AppSettings(), Active, Noon, Istanbul, Theme.Light)!;
        Assert.Equal(Path.Combine(@"C:\W\Riverside", "00.png"), t.Path);
        Assert.Equal(At("2026-11-27 18:00", Istanbul.TimeZoneId), t.Next);
    }

    [Fact]
    public void Sun_mode_without_a_set_leaves_the_wallpaper_alone() =>
        Assert.Null(WallpaperChoice.For(new AppSettings(), null, Noon, Istanbul, Theme.Light));

    [Fact]
    public void Per_profile_mode_uses_the_profile_picture_for_the_theme()
    {
        var s = new AppSettings
        {
            Wallpaper = WallpaperMode.PerProfile,
            Day = new Profile("#0067c0", Pointer.White, Sounds.Normal, @"C:\Pics\day.jpg"),
            Night = new Profile("#6b69d6", Pointer.Black, Sounds.Normal, @"C:\Pics\night.jpg"),
        };
        Assert.Equal(new WallpaperTarget(@"C:\Pics\day.jpg", null), WallpaperChoice.For(s, Active, Noon, Istanbul, Theme.Light));
        Assert.Equal(new WallpaperTarget(@"C:\Pics\night.jpg", null), WallpaperChoice.For(s, Active, Noon, Istanbul, Theme.Dark));
    }

    [Fact]
    public void Per_profile_mode_without_a_picture_leaves_the_wallpaper_alone() =>
        Assert.Null(WallpaperChoice.For(new AppSettings { Wallpaper = WallpaperMode.PerProfile }, Active, Noon, Istanbul, Theme.Dark));

    [Fact]
    public void Off_leaves_the_wallpaper_alone() =>
        Assert.Null(WallpaperChoice.For(new AppSettings { Wallpaper = WallpaperMode.Off }, Active, Noon, Istanbul, Theme.Light));

    [Theory]
    [InlineData(@"C:\Users\me\Pictures\cat.jpg", null, true)]
    [InlineData(@"C:\Users\me\Pictures\cat.jpg", @"C:\W\Riverside\00.png", true)]
    [InlineData(@"c:\w\riverside\00.png", @"C:\W\Riverside\00.png", false)]
    [InlineData(@"C:\W\Other\03.png", @"C:\W\Riverside\00.png", false)]
    [InlineData(@"C:\Pics\day.jpg", @"C:\Pics\day.jpg", false)]
    [InlineData("", null, true)]
    [InlineData(null, null, false)]
    public void Users_own_wallpaper_is_anything_Win_Solar_did_not_set(string? current, string? applied, bool own) =>
        Assert.Equal(own, WallpaperChoice.IsUsersOwn(current, applied, @"C:\W"));
}
