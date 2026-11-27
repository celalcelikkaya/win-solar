using WinSolar.Core.Updates;

namespace WinSolar.Tests.Updates;

public class ReleaseInfoTests
{
    private static readonly Version Current = new(3, 0, 0);

    private static string Release(string tag, bool prerelease = false, bool draft = false, bool exe = true, bool sums = true)
    {
        var assets = new List<string>();
        if (exe)
            assets.Add($$"""{"name":"WinSolarSetup-{{tag.TrimStart('v')}}.exe","browser_download_url":"https://github.com/celalcelikkaya/win-solar/releases/download/{{tag}}/WinSolarSetup-{{tag.TrimStart('v')}}.exe"}""");
        if (sums)
            assets.Add($$"""{"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/celalcelikkaya/win-solar/releases/download/{{tag}}/SHA256SUMS.txt"}""");
        return $$"""
            {"tag_name":"{{tag}}","name":"Win Solar","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},
             "html_url":"https://github.com/celalcelikkaya/win-solar/releases/tag/{{tag}}","assets":[{{string.Join(",", assets)}}]}
            """;
    }

    [Fact]
    public void Newer_release_is_offered_with_its_installer_and_checksums()
    {
        var update = ReleaseInfo.Newer(Release("v3.0.1"), Current);

        Assert.NotNull(update);
        Assert.Equal(new Version(3, 0, 1), update.Version);
        Assert.Equal("WinSolarSetup-3.0.1.exe", update.InstallerName);
        Assert.EndsWith("/v3.0.1/WinSolarSetup-3.0.1.exe", update.InstallerUrl.AbsoluteUri);
        Assert.EndsWith("/v3.0.1/SHA256SUMS.txt", update.ChecksumsUrl.AbsoluteUri);
        Assert.EndsWith("/releases/tag/v3.0.1", update.PageUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("v3.0.0")]
    [InlineData("v1.0.0")]
    [InlineData("v2.9.9")]
    public void Same_or_older_release_is_not_offered(string tag) => Assert.Null(ReleaseInfo.Newer(Release(tag), Current));

    [Fact]
    public void Pre_releases_and_drafts_are_not_offered()
    {
        Assert.Null(ReleaseInfo.Newer(Release("v3.1.0", prerelease: true), Current));
        Assert.Null(ReleaseInfo.Newer(Release("v3.1.0", draft: true), Current));
    }

    [Fact]
    public void Release_without_installer_or_checksums_is_not_offered()
    {
        Assert.Null(ReleaseInfo.Newer(Release("v3.1.0", exe: false), Current));
        Assert.Null(ReleaseInfo.Newer(Release("v3.1.0", sums: false), Current));
    }

    [Fact]
    public void Asset_on_another_host_is_not_offered()
    {
        var json = Release("v3.1.0").Replace("https://github.com/celalcelikkaya/win-solar/releases/download/v3.1.0/WinSolarSetup",
            "https://example.com/WinSolarSetup");
        Assert.Null(ReleaseInfo.Newer(json, Current));
    }

    [Fact]
    public void Installer_named_for_another_version_is_not_offered()
    {
        var json = Release("v3.1.0").Replace("WinSolarSetup-3.1.0.exe", "WinSolarSetup-3.0.0.exe");
        Assert.Null(ReleaseInfo.Newer(json, Current));
    }

    [Fact]
    public void Installer_name_may_use_a_two_part_tag()
    {
        var json = Release("v3.1").Replace("WinSolarSetup-3.1.exe", "WinSolarSetup-3.1.0.exe");
        Assert.Equal("WinSolarSetup-3.1.0.exe", ReleaseInfo.Newer(json, Current)?.InstallerName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"message":"API rate limit exceeded"}""")]
    [InlineData("[]")]
    public void Not_a_release_throws(string json) => Assert.Throws<FormatException>(() => ReleaseInfo.Newer(json, Current));

    [Theory]
    [InlineData("v3.0.1", "3.0.1")]
    [InlineData("3.0.1", "3.0.1")]
    [InlineData("v3.1", "3.1.0")]
    [InlineData("V10.2.3", "10.2.3")]
    public void Tags_parse_to_three_part_versions(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), ReleaseInfo.ParseTag(tag));

    [Theory]
    [InlineData("v3.0.1-beta")]
    [InlineData("latest")]
    [InlineData("v3")]
    [InlineData("v3.0.1.4")]
    [InlineData("")]
    public void Odd_tags_do_not_parse(string tag) => Assert.Null(ReleaseInfo.ParseTag(tag));

    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Checksum_is_found_by_file_name()
    {
        var sums = $"{new string('f', 64)}  Other.exe\r\n{Hash.ToUpperInvariant()} *WinSolarSetup-3.0.1.exe\r\n";
        Assert.Equal(Hash, ReleaseInfo.ChecksumFor(sums, "winsolarsetup-3.0.1.exe"));
    }

    [Fact]
    public void Missing_or_broken_checksum_is_null()
    {
        Assert.Null(ReleaseInfo.ChecksumFor($"{Hash}  Other.exe", "WinSolarSetup-3.0.1.exe"));
        Assert.Null(ReleaseInfo.ChecksumFor("abc123  WinSolarSetup-3.0.1.exe", "WinSolarSetup-3.0.1.exe"));
        Assert.Null(ReleaseInfo.ChecksumFor("", "WinSolarSetup-3.0.1.exe"));
    }

    [Theory]
    [InlineData("https://github.com/celalcelikkaya/win-solar/releases/download/v3.0.1/x.exe", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1/2", true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1/2", true)]
    [InlineData("http://github.com/x.exe", false)]
    [InlineData("https://github.com.evil.example/x.exe", false)]
    [InlineData("https://example.com/x.exe", false)]
    public void Only_github_https_downloads_are_allowed(string url, bool allowed) =>
        Assert.Equal(allowed, ReleaseInfo.IsAllowedDownload(new Uri(url)));
}
