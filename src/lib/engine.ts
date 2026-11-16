import { Place, Theme } from '../types';
import { addDays, minutesOfDay, partsInTz, sunForDate, SunDay } from './sun';

export interface Transition {
  time: Date;
  theme: Theme;
  kind: 'sunrise' | 'sunset';
}

export type Phase = 'night' | 'dawn' | 'day' | 'dusk';

export interface Schedule {
  theme: Theme;
  phase: Phase;
  prev: Transition | null;
  next: Transition | null;
  /** the transition after `next` (used to "skip" a switch) */
  after: Transition | null;
  /** 0..1 progress between prev and next transition */
  progress: number;
  minutesToNext: number | null;
  today: SunDay;
  /** switch instants for today, with user offsets applied (null on polar days) */
  todaySwitch: { light: Date | null; dark: Date | null };
  dayLengthMin: number | null;
}

const NEAR_MS = 40 * 60000;

/**
 * Pure function of (now, place, offsets): always uses fresh sun data for
 * yesterday..+2 days, so it never goes stale while the app keeps running.
 */
export function computeSchedule(now: Date, place: Place, offSunrise: number, offSunset: number): Schedule {
  const p = partsInTz(now, place.timezone);
  const days = [-1, 0, 1, 2].map((n) => {
    const a = addDays(p.y, p.m, p.d, n);
    return sunForDate(a.y, a.m, a.d, place.lat, place.lng);
  });

  const transitions: Transition[] = [];
  for (const d of days) {
    if (d.sunrise) transitions.push({ time: new Date(d.sunrise.getTime() + offSunrise * 60000), theme: 'light', kind: 'sunrise' });
    if (d.sunset) transitions.push({ time: new Date(d.sunset.getTime() + offSunset * 60000), theme: 'dark', kind: 'sunset' });
  }
  transitions.sort((a, b) => a.time.getTime() - b.time.getTime());

  const nowMs = now.getTime();
  let prev: Transition | null = null;
  let next: Transition | null = null;
  let after: Transition | null = null;
  for (const t of transitions) {
    if (t.time.getTime() <= nowMs) prev = t;
    else if (!next) next = t;
    else { after = t; break; }
  }

  const today = days[1];
  const theme: Theme = prev ? prev.theme : today.polar === 'day' ? 'light' : 'dark';
  const progress = prev && next ? (nowMs - prev.time.getTime()) / (next.time.getTime() - prev.time.getTime()) : 0.5;

  let phase: Phase = theme === 'light' ? 'day' : 'night';
  const near = (t: Transition | null) => t && Math.abs(t.time.getTime() - nowMs) <= NEAR_MS;
  if (near(next) || near(prev)) {
    const t = near(next) ? next! : prev!;
    phase = t.kind === 'sunrise' ? 'dawn' : 'dusk';
  }

  const tsr = today.sunrise ? new Date(today.sunrise.getTime() + offSunrise * 60000) : null;
  const tss = today.sunset ? new Date(today.sunset.getTime() + offSunset * 60000) : null;

  return {
    theme,
    phase,
    prev,
    next,
    after,
    progress: Math.min(1, Math.max(0, progress)),
    minutesToNext: next ? Math.max(0, Math.ceil((next.time.getTime() - nowMs) / 60000)) : null,
    today,
    todaySwitch: { light: tsr, dark: tss },
    dayLengthMin: today.sunrise && today.sunset ? Math.round((today.sunset.getTime() - today.sunrise.getTime()) / 60000) : today.polar === 'day' ? 1440 : today.polar === 'night' ? 0 : null,
  };
}

export const fmtTime = (d: Date | null, tz: string, hour12: boolean) => {
  if (!d) return '--:--';
  try {
    return new Intl.DateTimeFormat(hour12 ? 'en-US' : 'en-GB', { timeZone: tz, hour: '2-digit', minute: '2-digit', hour12 }).format(d);
  } catch {
    return d.toLocaleTimeString();
  }
};

export const fmtDuration = (mins: number, lang: 'tr' | 'en') => {
  const h = Math.floor(mins / 60);
  const m = mins % 60;
  const H = lang === 'tr' ? 'sa' : 'h';
  const M = lang === 'tr' ? 'dk' : 'm';
  if (h === 0) return `${m} ${M}`;
  return `${h} ${H} ${m} ${M}`;
};

export { minutesOfDay };
