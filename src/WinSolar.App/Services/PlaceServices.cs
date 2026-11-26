using System.Runtime.InteropServices;
using System.Net.Http;
using System.Text.Json;
using Windows.Devices.Geolocation;
using WinSolar.Core;

namespace WinSolar.App.Services;

/// <summary>City search through Open-Meteo's free geocoding API (as in v2).</summary>
public static class PlaceSearch
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task<IReadOnlyList<Place>> SearchAsync(string query, CancellationToken ct)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=8&language=en&format=json";
        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("results", out var results)) return [];

        var list = new List<Place>();
        foreach (var r in results.EnumerateArray())
        {
            var name = r.GetProperty("name").GetString() ?? "";
            var country = r.TryGetProperty("country", out var c) ? c.GetString() : null;
            var region = r.TryGetProperty("admin1", out var a) ? a.GetString() : null;
            var tz = r.TryGetProperty("timezone", out var z) ? z.GetString() : null;
            var label = region is null || region == name ? country : $"{region}, {country}";
            list.Add(new Place(name, label, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), tz ?? LocalIana()));
        }

        return list;
    }

    public static string LocalIana()
    {
        var id = TimeZoneInfo.Local.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) ? iana : id;
    }
}

/// <summary>Windows location (city accuracy). Null when location is off or denied.</summary>
public static class WindowsLocation
{
    public static async Task<Place?> GetAsync()
    {
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed) return null;
            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var pos = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(10));
            double lat = pos.Coordinate.Point.Position.Latitude, lng = pos.Coordinate.Point.Position.Longitude;

            // Name it after the nearest known city when one is close, otherwise "My location".
            var near = Places.Popular.MinBy(p => Distance(lat, lng, p.Lat, p.Lng));
            if (near is not null && Distance(lat, lng, near.Lat, near.Lng) < 60) return near with { Lat = lat, Lng = lng };
            return new Place("My location", null, Math.Round(lat, 4), Math.Round(lng, 4), PlaceSearch.LocalIana());
        }
        catch (Exception e) when (e is UnauthorizedAccessException or TimeoutException or COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private static double Distance(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371, rad = Math.PI / 180;
        var dLat = (lat2 - lat1) * rad;
        var dLng = (lng2 - lng1) * rad;
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Sqrt(h));
    }
}
