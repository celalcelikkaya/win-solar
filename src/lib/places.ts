import { Place } from '../types';

export const POPULAR: Place[] = [
  { name: 'Blantyre', country: 'Malawi', lat: -15.7861, lng: 35.0058, timezone: 'Africa/Blantyre' },
  { name: 'Lilongwe', country: 'Malawi', lat: -13.9669, lng: 33.7873, timezone: 'Africa/Blantyre' },
  { name: 'İstanbul', country: 'Türkiye', lat: 41.0138, lng: 28.9497, timezone: 'Europe/Istanbul' },
  { name: 'Ankara', country: 'Türkiye', lat: 39.9199, lng: 32.8543, timezone: 'Europe/Istanbul' },
  { name: 'London', country: 'United Kingdom', lat: 51.5085, lng: -0.1257, timezone: 'Europe/London' },
  { name: 'Berlin', country: 'Germany', lat: 52.5244, lng: 13.4105, timezone: 'Europe/Berlin' },
  { name: 'Dubai', country: 'United Arab Emirates', lat: 25.0657, lng: 55.1713, timezone: 'Asia/Dubai' },
  { name: 'New York', country: 'United States', lat: 40.7143, lng: -74.006, timezone: 'America/New_York' },
  { name: 'Tokyo', country: 'Japan', lat: 35.6895, lng: 139.6917, timezone: 'Asia/Tokyo' },
  { name: 'Sydney', country: 'Australia', lat: -33.8678, lng: 151.2073, timezone: 'Australia/Sydney' },
  { name: 'Reykjavík', country: 'Iceland', lat: 64.1355, lng: -21.8954, timezone: 'Atlantic/Reykjavik' },
];

const browserTz = () => Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';

/** Very rough guess used only until a real place is found (longitude from UTC offset). */
export function approxPlace(): Place {
  const tz = browserTz();
  const lng = Math.max(-180, Math.min(180, (-new Date().getTimezoneOffset() / 60) * 15));
  const name = tz.split('/').pop()!.replace(/_/g, ' ');
  return { name, lat: 30, lng, timezone: tz, approx: true };
}

interface GeoResult {
  name: string; admin1?: string; country?: string; latitude: number; longitude: number; timezone?: string;
}

export async function searchPlaces(query: string, lang: string, signal?: AbortSignal): Promise<Place[]> {
  const url = `https://geocoding-api.open-meteo.com/v1/search?name=${encodeURIComponent(query)}&count=8&language=${lang}&format=json`;
  const res = await fetch(url, { signal });
  if (!res.ok) throw new Error(`geocoding ${res.status}`);
  const json = (await res.json()) as { results?: GeoResult[] };
  return (json.results ?? []).map((r) => ({
    name: r.name, region: r.admin1, country: r.country,
    lat: r.latitude, lng: r.longitude, timezone: r.timezone || browserTz(),
  }));
}

/** First-run guess: geocode the city part of the browser's IANA time zone (no permission prompt). */
export async function detectPlaceFromTimezone(lang: string, signal?: AbortSignal): Promise<Place | null> {
  const tz = browserTz();
  const city = tz.split('/').pop()!.replace(/_/g, ' ');
  const results = await searchPlaces(city, lang, signal);
  return results.find((r) => r.timezone === tz) ?? null;
}

export async function getBrowserPosition(): Promise<Place> {
  // Desktop app: Chromium geolocation is unavailable in Electron, use the IP-based lookup from the main process.
  const desktop = window.electronAPI;
  if (desktop) {
    const r = await desktop.getLocation();
    if (r.success && typeof r.lat === 'number' && typeof r.lng === 'number') {
      return { name: r.name || 'My location', country: r.country, lat: r.lat, lng: r.lng, timezone: r.timezone || browserTz() };
    }
    throw new Error('location-failed');
  }
  return new Promise((resolve, reject) => {
    if (!navigator.geolocation) return reject(new Error('unsupported'));
    navigator.geolocation.getCurrentPosition(
      (pos) => resolve({
        name: 'GPS', lat: Math.round(pos.coords.latitude * 10000) / 10000,
        lng: Math.round(pos.coords.longitude * 10000) / 10000, timezone: browserTz(),
      }),
      (err) => reject(err),
      { timeout: 10000 },
    );
  });
}

export const placeLabel = (p: Place) => [p.name, p.country].filter(Boolean).join(', ');
