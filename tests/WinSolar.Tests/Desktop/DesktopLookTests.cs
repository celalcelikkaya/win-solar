using WinSolar.Core.Desktop;
using WinSolar.Core.Scheduling;
using WinSolar.Core.Settings;

namespace WinSolar.Tests.Desktop;

public class DesktopLookTests
{
    private static readonly AppSettings Defaults = new();

    [Fact]
    public void Light_theme_uses_the_day_profile() =>
        Assert.Equal(new Look(Theme.Light, "#0067c0", Pointer.White, Sounds.Normal), DesktopLook.For(Theme.Light, Defaults));

    [Fact]
    public void Dark_theme_uses_the_night_profile() =>
        Assert.Equal(new Look(Theme.Dark, "#6b69d6", Pointer.Black, Sounds.Normal), DesktopLook.For(Theme.Dark, Defaults));

    [Fact]
    public void Pointer_and_sounds_stay_daytime_when_not_following()
    {
        var s = Defaults with { ProfilesFollow = false, Night = new Profile("#e3008c", Pointer.Black, Sounds.Off) };
        Assert.Equal(new Look(Theme.Dark, "#e3008c", Pointer.White, Sounds.Normal), DesktopLook.For(Theme.Dark, s));
    }

    [Fact]
    public void Parse_reads_hex()
    {
        Assert.Equal(((byte)0x6b, (byte)0x69, (byte)0xd6), Accents.Parse("#6b69d6"));
        Assert.Throws<FormatException>(() => Accents.Parse("purple"));
    }

    [Fact]
    public void Swatches_are_the_demo_colors() =>
        Assert.Equal(["#0067c0", "#6b69d6", "#8764b8", "#c239b3", "#e3008c", "#ca5010", "#0f7b0f", "#038387"], Accents.Swatches);
}
