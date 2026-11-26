using WinSolar.Core.Decisions;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;

namespace WinSolar.Tests.Settings;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "winsolar-tests-" + Guid.NewGuid().ToString("N"));
    private readonly JsonFileStore store;

    public JsonFileStoreTests() => store = new JsonFileStore(dir);

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Round_trips_settings()
    {
        var saved = new AppSettings { Hour12 = true, Place = TestTime.Istanbul, FixedDark = new TimeOnly(20, 30) };
        store.Save("settings.json", saved);
        Assert.Equal(saved, store.Load("settings.json", () => new AppSettings()));
    }

    [Fact]
    public void Round_trips_state_with_hold()
    {
        var saved = new AppState(Mode.Auto, null, new Hold(Theme.Light, new DateTimeOffset(2026, 11, 26, 16, 10, 0, TimeSpan.Zero), HoldKind.Snooze), Theme.Light);
        store.Save("state.json", saved);
        Assert.Equal(saved, store.Load("state.json", () => new AppState()));
    }

    [Fact]
    public void Missing_file_gives_defaults() =>
        Assert.Equal(new AppSettings(), store.Load("settings.json", () => new AppSettings()));

    // Review Focus 4: a save cut off by a power loss.
    [Fact]
    public void Damaged_file_gives_defaults_and_is_kept_as_bad()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"Hour12\": tr");
        Assert.Equal(new AppSettings(), store.Load("settings.json", () => new AppSettings()));
        Assert.True(File.Exists(Path.Combine(dir, "settings.json.bad")));
    }

    [Fact]
    public void Leftover_tmp_file_does_not_break_save()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json.tmp"), "garbage");
        var saved = new AppSettings { OffsetSunsetMin = 10 };
        store.Save("settings.json", saved);
        Assert.Equal(saved, store.Load("settings.json", () => new AppSettings()));
    }
}
