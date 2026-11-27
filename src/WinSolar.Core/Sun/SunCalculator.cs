namespace WinSolar.Core.Sun;

/// <summary>
/// Offline sunrise/sunset (NOAA "sunrise equation"), a port of v2's src/lib/sun.ts. Accuracy ~1–2 minutes.
/// </summary>
public static class SunCalculator
{
    private const double Rad = Math.PI / 180;
    private const double MsPerDay = 86_400_000.0;
    private const double JdUnixEpoch = 2440587.5;
    private const double J2000 = 2451545.0;

    private static double Mod(double a, double n) => ((a % n) + n) % n;
    private static DateTimeOffset FromJd(double jd) => DateTimeOffset.UnixEpoch.AddMilliseconds((jd - JdUnixEpoch) * MsPerDay);

    /// <summary>Sun events for the civil date at the given coordinates.</summary>
    public static SunDay ForDate(DateOnly date, double lat, double lng)
    {
        var noonUtc = new DateTimeOffset(date.Year, date.Month, date.Day, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var localNoonMs = noonUtc - lng / 15 * 3_600_000;
        var j = localNoonMs / MsPerDay + JdUnixEpoch;
        var n = Math.Floor(j - J2000 + 0.0008 + 0.5); // JavaScript Math.round
        var js = n - lng / 360;
        var m = Mod(357.5291 + 0.98560028 * js, 360);
        var mr = m * Rad;
        var c = 1.9148 * Math.Sin(mr) + 0.02 * Math.Sin(2 * mr) + 0.0003 * Math.Sin(3 * mr);
        var lam = Mod(m + c + 180 + 102.9372, 360) * Rad;
        var jt = J2000 + js + 0.0053 * Math.Sin(mr) - 0.0069 * Math.Sin(2 * lam);
        var sinDec = Math.Sin(lam) * Math.Sin(23.4397 * Rad);
        var dec = Math.Asin(sinDec);
        var latR = lat * Rad;
        var cosW = (Math.Sin(-0.833 * Rad) - Math.Sin(latR) * sinDec) / (Math.Cos(latR) * Math.Cos(dec));
        var noon = FromJd(jt);
        if (cosW > 1) return new SunDay(null, null, noon, PolarState.Night);
        if (cosW < -1) return new SunDay(null, null, noon, PolarState.Day);
        var w = Math.Acos(cosW) / Rad;
        return new SunDay(FromJd(jt - w / 360), FromJd(jt + w / 360), noon, PolarState.None);
    }

    /// <summary>Sun elevation in degrees at an instant (SunCalc model, as in the demo).</summary>
    public static double Elevation(DateTimeOffset at, double lat, double lng)
    {
        var (dec, h, latR) = Position(at, lat, lng);
        return Math.Asin(Math.Sin(latR) * Math.Sin(dec) + Math.Cos(latR) * Math.Cos(dec) * Math.Cos(h)) / Rad;
    }

    /// <summary>Sun azimuth in degrees at an instant, from north, clockwise (90 = east, 180 = south).</summary>
    public static double Azimuth(DateTimeOffset at, double lat, double lng)
    {
        var (dec, h, latR) = Position(at, lat, lng);
        var fromSouth = Math.Atan2(Math.Sin(h), Math.Cos(h) * Math.Sin(latR) - Math.Tan(dec) * Math.Cos(latR));
        return Mod(fromSouth / Rad + 180, 360);
    }

    /// <summary>Declination, local hour angle and latitude, in radians.</summary>
    private static (double Dec, double HourAngle, double Lat) Position(DateTimeOffset at, double lat, double lng)
    {
        var d = at.ToUnixTimeMilliseconds() / MsPerDay + JdUnixEpoch - J2000;
        var m = Mod(357.5291 + 0.98560028 * d, 360) * Rad;
        var c = (1.9148 * Math.Sin(m) + 0.02 * Math.Sin(2 * m) + 0.0003 * Math.Sin(3 * m)) * Rad;
        var l = m + c + Math.PI + 102.9372 * Rad;
        var e = 23.4397 * Rad;
        var dec = Math.Asin(Math.Sin(l) * Math.Sin(e));
        var ra = Math.Atan2(Math.Sin(l) * Math.Cos(e), Math.Cos(l));
        var h = (280.16 + 360.9856235 * d) * Rad + lng * Rad - ra;
        return (dec, h, lat * Rad);
    }
}
