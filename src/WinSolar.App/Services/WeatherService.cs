using System.Net.Http;
using System.Windows.Threading;
using WinSolar.Core;
using WinSolar.Core.Engine;
using WinSolar.Core.Light;

namespace WinSolar.App.Services;

/// <summary>
/// Hourly cloud cover from Open-Meteo, used only when real light is on and there is no sensor. Refreshes about every
/// hour and when the place changes (5 min after a failure); a failed refresh keeps the last forecast, which expires by itself after 6 h.
/// </summary>
public sealed class WeatherService : IWeather, IDisposable
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan Every = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    private readonly DispatcherTimer hourly;
    private WeatherForecast? forecast;
    private Place? fetchedFor;
    private Place? wanted;
    private bool busy;

    public WeatherService(Dispatcher dispatcher)
    {
        hourly = new DispatcherTimer(Every, DispatcherPriority.Background, (_, _) => Refresh(), dispatcher);
        hourly.Stop();
    }

    public event Action? Changed;

    public HourWeather? At(DateTimeOffset t) =>
        forecast is not null && fetchedFor == wanted ? forecast.At(t, DateTimeOffset.Now) : null;

    /// <summary>Called with every engine snapshot: on while weather is in use, for the current place.</summary>
    public void Use(bool enabled, Place place)
    {
        wanted = place;
        if (!enabled)
        {
            hourly.Stop();
            return;
        }

        if (!hourly.IsEnabled) hourly.Start();
        if (fetchedFor != place || forecast is null || DateTimeOffset.Now - forecast.FetchedAt > TimeSpan.FromHours(1)) Refresh();
    }

    private async void Refresh()
    {
        if (busy || wanted is not { } place) return;
        busy = true;
        try
        {
            var json = await Http.GetStringAsync(OpenMeteo.ForecastUrl(place));
            forecast = new WeatherForecast(OpenMeteo.Parse(json), DateTimeOffset.Now);
            fetchedFor = place;
            hourly.Interval = Every;
            Changed?.Invoke();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or FormatException)
        {
            Log.Write($"Weather unavailable: {e.Message}");
            hourly.Interval = RetryAfter; // a slow or offline network: try again soon, not in an hour
        }
        finally
        {
            busy = false;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WinSolar/3.0");
        return client;
    }

    public void Dispose() => hourly.Stop();
}
