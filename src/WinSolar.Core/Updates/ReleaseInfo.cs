using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinSolar.Core.Updates;

/// <summary>A newer Win Solar on GitHub Releases: its installer, the release's SHA256SUMS.txt and its page.</summary>
public sealed record AvailableUpdate(Version Version, string InstallerName, Uri InstallerUrl, Uri ChecksumsUrl, Uri PageUrl);

/// <summary>
/// Reads GitHub's "latest release" answer (<c>/repos/{owner}/{repo}/releases/latest</c>) and the release's
/// <c>SHA256SUMS.txt</c> as <c>build/release.ps1</c> writes them.
/// </summary>
public static class ReleaseInfo
{
    public const string ChecksumsName = "SHA256SUMS.txt";

    private static readonly Regex Tag = new(@"^[vV]?(\d{1,4})\.(\d{1,4})(?:\.(\d{1,4}))?$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256 = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The update this release offers over <paramref name="current"/>, or null when it is not newer, a draft or
    /// pre-release, or lacks the installer or checksums (on GitHub). Throws <see cref="FormatException"/> when the
    /// answer is not a release (an error message, a rate limit, garbage).
    /// </summary>
    public static AvailableUpdate? Newer(string latestReleaseJson, Version current)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(latestReleaseJson); }
        catch (JsonException e) { throw new FormatException("Not a GitHub release.", e); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out var tagElement)
                || tagElement.ValueKind != JsonValueKind.String)
                throw new FormatException("Not a GitHub release.");

            if (Flag(root, "draft") || Flag(root, "prerelease")) return null;
            if (ParseTag(tagElement.GetString()!) is not { } version || version <= Normalize(current)) return null;

            // Tied to the tag, so a release with the wrong installer is not offered (and re-offered) every day.
            var wantedInstaller = $"WinSolarSetup-{version.ToString(3)}.exe";
            Uri? installer = null, sums = null;
            string? installerName = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.ValueKind != JsonValueKind.Object) continue;
                    var name = Text(asset, "name");
                    var url = Url(asset, "browser_download_url");
                    if (name is null || url is null || !IsAllowedDownload(url)) continue;
                    if (installer is null && string.Equals(name, wantedInstaller, StringComparison.OrdinalIgnoreCase)) (installer, installerName) = (url, name);
                    else if (sums is null && string.Equals(name, ChecksumsName, StringComparison.OrdinalIgnoreCase)) sums = url;
                }
            }

            if (installer is null || sums is null) return null;
            var page = Url(root, "html_url") ?? new Uri("https://github.com/celalcelikkaya/win-solar/releases");
            return new AvailableUpdate(version, installerName!, installer, sums, page);
        }
    }

    /// <summary><c>v3.0.1</c>, <c>3.0.1</c>, <c>v3.1</c> → a three-part version; anything else (pre-release tags too) → null.</summary>
    public static Version? ParseTag(string tag)
    {
        var m = Tag.Match(tag.Trim());
        if (!m.Success) return null;
        int Part(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
        return new Version(Part(1), Part(2), Part(3));
    }

    /// <summary>The lower-case SHA-256 listed for <paramref name="fileName"/> (sha256sum format), or null.</summary>
    public static string? ChecksumFor(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space <= 0) continue;
            var hash = line[..space];
            var name = line[space..].Trim().TrimStart('*');
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                return Sha256.IsMatch(hash) ? hash.ToLowerInvariant() : null;
        }

        return null;
    }

    private static readonly string[] Hosts = ["github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"];

    /// <summary>
    /// Downloads come only from GitHub over HTTPS: release assets live on github.com and redirect to
    /// release-assets.githubusercontent.com (objects.githubusercontent.com before 2025).
    /// </summary>
    public static bool IsAllowedDownload(Uri url) =>
        url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps
        && Hosts.Any(h => url.IdnHost.Equals(h, StringComparison.OrdinalIgnoreCase));

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Uri? Url(JsonElement e, string name) =>
        Text(e, name) is { } s && Uri.TryCreate(s, UriKind.Absolute, out var u) ? u : null;
}
