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

    // After Current: static initializers run in order, and the client's User-Agent reads it.
    private static readonly HttpClient Http = CreateClient();

    /// <summary>Downloads stream past HttpClient.Timeout (headers only), so the whole update gets its own limit.</summary>
    private static readonly TimeSpan DownloadLimit = TimeSpan.FromMinutes(10);

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
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Available = null; // no release published yet
                Set(UpdateState.UpToDate, "Win Solar is up to date");
                return;
            }

            response.EnsureSuccessStatusCode(); // 403 when rate-limited
            var json = await response.Content.ReadAsStringAsync(cancel.Token);
            Available = ReleaseInfo.Newer(json, Current);
            if (Available is { } update) Set(UpdateState.Available, $"Win Solar {update.Version.ToString(3)} is available");
            else Set(UpdateState.UpToDate, "Win Solar is up to date");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or FormatException)
        {
            Log.Write($"Update check failed: {e.Message}");
            if (Available is { } known) Set(UpdateState.Available, $"Win Solar {known.Version.ToString(3)} is available");
            else Set(UpdateState.Failed, "Could not check for updates");
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
            using var cancel = new CancellationTokenSource(DownloadLimit);

            var sums = await Http.GetStringAsync(update.ChecksumsUrl, cancel.Token);
            var expected = ReleaseInfo.ChecksumFor(sums, update.InstallerName)
                ?? throw new InvalidDataException("The release has no checksum for its installer.");

            // The SHA-256 from the release is what makes the file trusted; GitHub picks the redirect host.
            using (var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancel.Token))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancel.Token);
                await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                    await source.CopyToAsync(file, cancel.Token);
            }

            File.Move(part, exe, overwrite: true);
            string actual;
            // Held open (read sharing only) from the hash until setup has started: nothing can swap the file in between.
            using (var held = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(held, cancel.Token)).ToLowerInvariant();
                if (actual == expected)
                {
                    Log.Write($"Installing Win Solar {update.Version.ToString(3)}.");
                    Process.Start(new ProcessStartInfo(exe, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1") { UseShellExecute = true });
                }
            }

            if (actual != expected)
            {
                Log.Write($"Update {update.InstallerName}: checksum {actual} does not match {expected}.");
                Delete(exe);
                Set(UpdateState.Failed, "The download did not match its checksum");
                return;
            }

            quit();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                       or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Log.Write($"Update failed: {e.Message}");
            Delete(part);
            Delete(exe);
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
