using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Threading;
using WinSolar.Core.Updates;

namespace WinSolar.App.Services;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Failed }

/// <summary>
/// Looks for a newer Win Solar on GitHub Releases (a minute after start, then daily, or when asked) and installs it:
/// downloads the installer, checks its SHA-256 against the release's SHA256SUMS.txt, runs it silently and quits.
/// The installer starts Win Solar again (<c>/UPDATE=1</c>). This is the only process Win Solar ever starts.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string LatestUrl = "https://api.github.com/repos/celalcelikkaya/win-solar/releases/latest";
    private static readonly HttpClient Http = CreateClient();

    private readonly DispatcherTimer timer;
    private readonly string folder;

    public UpdateService(Dispatcher dispatcher)
    {
        folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Win Solar", "Updates");
        timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, async (_, _) =>
        {
            timer!.Interval = TimeSpan.FromHours(24);
            await CheckAsync();
        }, dispatcher);
        timer.Stop();
    }

    public static Version Current { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version);

    public UpdateState State { get; private set; }

    public AvailableUpdate? Available { get; private set; }

    /// <summary>What the About row says under the version.</summary>
    public string? Message { get; private set; }

    public event Action? Changed;

    public bool Busy => State is UpdateState.Checking or UpdateState.Downloading;

    public void Start()
    {
        CleanUp();
        timer.Start();
    }

    public async Task CheckAsync()
    {
        if (Busy) return;
        Set(UpdateState.Checking, "Checking…");
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request, cancel.Token);
            response.EnsureSuccessStatusCode(); // 403 when rate-limited, 404 before the first release
            var json = await response.Content.ReadAsStringAsync(cancel.Token);
            Available = ReleaseInfo.Newer(json, Current);
            if (Available is { } update) Set(UpdateState.Available, $"Win Solar {update.Version.ToString(3)} is available");
            else Set(UpdateState.UpToDate, "Win Solar is up to date");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or FormatException)
        {
            Log.Write($"Update check failed: {e.Message}");
            Set(Available is null ? UpdateState.Failed : UpdateState.Available, "Could not check for updates");
        }
    }

    /// <summary>Downloads, verifies and runs the installer, then calls <paramref name="quit"/>. Nothing runs unverified.</summary>
    public async Task InstallAsync(Action quit)
    {
        if (Busy || Available is not { } update) return;
        Set(UpdateState.Downloading, "Downloading…");
        var part = Path.Combine(folder, update.InstallerName + ".part");
        var exe = Path.Combine(folder, update.InstallerName);
        try
        {
            if (!ReleaseInfo.IsAllowedDownload(update.InstallerUrl) || !ReleaseInfo.IsAllowedDownload(update.ChecksumsUrl))
                throw new InvalidDataException("The download address is not GitHub.");
            CleanUp();
            Directory.CreateDirectory(folder);

            var sums = await Http.GetStringAsync(update.ChecksumsUrl);
            var expected = ReleaseInfo.ChecksumFor(sums, update.InstallerName)
                ?? throw new InvalidDataException("The release has no checksum for its installer.");

            string actual;
            using (var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri is { } final && !ReleaseInfo.IsAllowedDownload(final))
                    throw new InvalidDataException("The download was redirected away from GitHub.");
                await using var source = await response.Content.ReadAsStreamAsync();
                await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                    await source.CopyToAsync(file);
            }

            await using (var check = File.OpenRead(part))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(check)).ToLowerInvariant();
            if (actual != expected)
            {
                Log.Write($"Update {update.InstallerName}: checksum {actual} does not match {expected}.");
                Delete(part);
                Set(UpdateState.Failed, "The download did not match its checksum");
                return;
            }

            File.Move(part, exe, overwrite: true);
            Log.Write($"Installing Win Solar {update.Version.ToString(3)}.");
            Process.Start(new ProcessStartInfo(exe, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1") { UseShellExecute = true });
            quit();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                       or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Log.Write($"Update failed: {e.Message}");
            Delete(part);
            Set(UpdateState.Available, e is InvalidDataException ? e.Message : "Could not download the update");
        }
    }

    private void Set(UpdateState state, string? message)
    {
        State = state;
        Message = message;
        Changed?.Invoke();
    }

    /// <summary>Installers from earlier updates (or a cut-off download) are not kept.</summary>
    private void CleanUp()
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder)) Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not clean the update folder: {e.Message}");
        }
    }

    private static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* in use (still running): next time */ }
    }

    private static Version Normalize(Version? v) => v is null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"WinSolar/{Current.ToString(3)}");
        return client;
    }

    public void Dispose() => timer.Stop();
}
