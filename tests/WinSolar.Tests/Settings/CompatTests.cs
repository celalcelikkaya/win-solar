using WinSolar.Core.Desktop;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Power;
using WinSolar.Core.Settings;
using WinSolar.Core.Wallpaper;

namespace WinSolar.Tests.Settings;

public class CompatTests
{
    [Fact]
    public void Plan1_settings_file_loads_with_new_defaults()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "Source": "Sun", "OffsetSunsetMin": -10, "Hour12": true, "Schema": 3, "SomethingNew": 1 }""");
        var s = new JsonFileStore(dir).Load("settings.json", () => new AppSettings());
        Assert.Equal(-10, s.OffsetSunsetMin);
        Assert.True(s.Hour12);
        Assert.True(s.FollowRealLight);
        Assert.Equal(60, s.MaxShiftMin);
        Assert.Equal(BatteryRule.DarkBelow20, s.Battery);
    }

    [Theory]
    [InlineData(5, 30)]
    [InlineData(60, 60)]
    [InlineData(500, 90)]
    public void MaxShift_is_clamped_to_30_to_90_minutes(int setting, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), new AppSettings { MaxShiftMin = setting }.MaxShift);

    [Fact]
    public void Room_light_mode_uses_sun_times_for_its_schedule() =>
        Assert.Equal(ScheduleSource.Sun, new AppSettings { Source = ScheduleSource.RoomLight }.ToScheduleOptions().Source);

    [Fact]
    public void Plan_2_settings_get_profile_defaults()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "Source": "RoomLight", "FollowRealLight": true, "Schema": 3 }""");
        var s = new JsonFileStore(dir).Load("settings.json", () => new AppSettings());
        Assert.Equal(SwitchAnimation.Roller, s.Animation);
        Assert.Equal(new Profile("#0067c0", Pointer.White, Sounds.Normal), s.Day);
        Assert.Equal(new Profile("#6b69d6", Pointer.Black, Sounds.Normal), s.Night);
        Assert.True(s.ProfilesFollow);
    }

    [Fact]
    public void Plan_3_settings_get_wallpaper_defaults()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "settings.json"),
            """{ "Animation": "Fade", "Day": { "Accent": "#0f7b0f", "Pointer": "White", "Sounds": "Normal" }, "Schema": 3 }""");
        var s = new JsonFileStore(dir).Load("settings.json", () => new AppSettings());
        Assert.Equal(SwitchAnimation.Fade, s.Animation);
        Assert.Equal("#0f7b0f", s.Day.Accent);
        Assert.Null(s.Day.Wallpaper);
        Assert.Equal(WallpaperMode.Sun, s.Wallpaper);
        Assert.Null(s.ActiveSet);
    }
}
