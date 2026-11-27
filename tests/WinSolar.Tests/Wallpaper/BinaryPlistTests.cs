using WinSolar.Core.Wallpaper;

namespace WinSolar.Tests.Wallpaper;

public class BinaryPlistTests
{
    // {s 'héllo', n -5, big 70000, f true, r 1.5, a [1, 'x']}
    private const string Mixed =
        "YnBsaXN0MDDWAQIDBAUGBwoLDA0OUWFTYmlnUWZRblFyUXOiCAkQAVF4EgABEXAJE//////////7Iz/4AAAAAAAAZQBoAOkAbABsAG8IFRcbHR8hIyYoKi8wOUIAAAAAAAABAQAAAAAAAAAPAAAAAAAAAAAAAAAAAAAATQ==";

    [Fact]
    public void Reads_every_value_type()
    {
        var d = Assert.IsType<Dictionary<string, object?>>(BinaryPlist.Parse(Convert.FromBase64String(Mixed)));
        Assert.Equal("héllo", d["s"]);
        Assert.Equal(-5L, d["n"]);
        Assert.Equal(70000L, d["big"]);
        Assert.Equal(true, d["f"]);
        Assert.Equal(1.5, d["r"]);
        var a = Assert.IsType<List<object?>>(d["a"]);
        Assert.Equal(new object?[] { 1L, "x" }, a);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bm90IGEgcGxpc3Q=")]
    [InlineData("YnBsaXN0MDDSAQID")]
    public void Rejects_bad_data(string base64) =>
        Assert.Throws<FormatException>(() => BinaryPlist.Parse(Convert.FromBase64String(base64)));
}
