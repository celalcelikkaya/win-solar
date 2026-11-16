/**
 * Offline sunrise/sunset calculation (NOAA / "sunrise equation").
 * Accuracy is within ~1-2 minutes; no network needed. Handles polar day/night.
 */
const RAD = Math.PI / 180;

export interface SunDay {
  sunrise: Date | null;
  sunset: Date | null;
  noon: Date;
  polar: 'day' | 'night' | null;
}

const jdToDate = (jd: number) => new Date((jd - 2440587.5) * 86400000);
const mod = (a: number, n: number) => ((a % n) + n) % n;

/** Sun events for the civil date (y, m, d) at the given coordinates. */
export function sunForDate(y: number, m: number, d: number, lat: number, lng: number): SunDay {
  // Local mean solar noon of that date, expressed in UTC ms
  const localNoonMs = Date.UTC(y, m - 1, d, 12, 0, 0) - (lng / 15) * 3600000;
  const J = localNoonMs / 86400000 + 2440587.5;
  const n = Math.round(J - 2451545.0 + 0.0008);
  const Js = n - lng / 360; // east longitude = earlier transit in UTC
  const M = mod(357.5291 + 0.98560028 * Js, 360);
  const Mr = M * RAD;
  const C = 1.9148 * Math.sin(Mr) + 0.02 * Math.sin(2 * Mr) + 0.0003 * Math.sin(3 * Mr);
  const lam = mod(M + C + 180 + 102.9372, 360) * RAD;
  const Jt = 2451545.0 + Js + 0.0053 * Math.sin(Mr) - 0.0069 * Math.sin(2 * lam);
  const sinDec = Math.sin(lam) * Math.sin(23.4397 * RAD);
  const dec = Math.asin(sinDec);
  const latR = lat * RAD;
  const cosW = (Math.sin(-0.833 * RAD) - Math.sin(latR) * sinDec) / (Math.cos(latR) * Math.cos(dec));
  const noon = jdToDate(Jt);
  if (cosW > 1) return { sunrise: null, sunset: null, noon, polar: 'night' };
  if (cosW < -1) return { sunrise: null, sunset: null, noon, polar: 'day' };
  const w = Math.acos(cosW) / RAD;
  return { sunrise: jdToDate(Jt - w / 360), sunset: jdToDate(Jt + w / 360), noon, polar: null };
}

/** Wall-clock parts of an instant in a given IANA time zone. */
export function partsInTz(date: Date, tz: string) {
  let f: Intl.DateTimeFormat;
  try {
    f = new Intl.DateTimeFormat('en-CA', {
      timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
    });
  } catch {
    f = new Intl.DateTimeFormat('en-CA', {
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
    });
  }
  const p: Record<string, string> = {};
  for (const x of f.formatToParts(date)) p[x.type] = x.value;
  return { y: +p.year, m: +p.month, d: +p.day, h: +p.hour % 24, min: +p.minute };
}

export function addDays(y: number, m: number, d: number, n: number) {
  const t = new Date(Date.UTC(y, m - 1, d + n));
  return { y: t.getUTCFullYear(), m: t.getUTCMonth() + 1, d: t.getUTCDate() };
}

/** Convert a wall-clock time in a time zone to an absolute Date. */
export function zonedTimeToDate(y: number, m: number, d: number, h: number, min: number, tz: string) {
  const guess = Date.UTC(y, m - 1, d, h, min);
  const p = partsInTz(new Date(guess), tz);
  const shown = Date.UTC(p.y, p.m - 1, p.d, p.h, p.min);
  return new Date(guess - (shown - guess));
}

export const minutesOfDay = (date: Date, tz: string) => {
  const p = partsInTz(date, tz);
  return p.h * 60 + p.min;
};
