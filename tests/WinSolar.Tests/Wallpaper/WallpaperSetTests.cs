using WinSolar.Core.Settings;
using WinSolar.Core.Wallpaper;

namespace WinSolar.Tests.Wallpaper;

public class WallpaperSetTests
{
    [Fact]
    public void Set_round_trips_through_json()
    {
        var folder = Path.Combine(Path.GetTempPath(), "winsolar-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var schedule = new DynamicSchedule(DynamicKind.Time, [], [new TimeFrame(0, 1), new TimeFrame(0.5, 0)], 0, 1);
            var set = new WallpaperSet("Riverside", schedule, ["00.png", "01.png"], 1884, 1061);
            var store = new JsonFileStore(folder);
            store.Save(WallpaperSet.FileName, set);
            var back = store.Load<WallpaperSet?>(WallpaperSet.FileName, () => null)!;
            Assert.Equal("Riverside", back.Name);
            Assert.Equal(DynamicKind.Time, back.Schedule.Kind);
            Assert.Equal(schedule.Time, back.Schedule.Time);
            Assert.Equal(new[] { "00.png", "01.png" }, back.Frames);
            Assert.Equal("01.png", back.FrameFile(1));
            Assert.Equal("00.png", back.FrameFile(7));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("Riverside by ArseniXC", "Riverside by ArseniXC")]
    [InlineData("a:b*c?", "a b c")]
    [InlineData("  ..  ", "Wallpaper")]
    [InlineData("CON", "CON wallpaper")]
    public void Folder_names_are_safe(string name, string expected) =>
        Assert.Equal(expected, WallpaperSet.SafeFolderName(name));

    [Fact]
    public void Folder_names_do_not_clash() =>
        Assert.Equal("Riverside 3", WallpaperSet.UniqueFolderName("Riverside", n => n is "Riverside" or "Riverside 2"));
}
