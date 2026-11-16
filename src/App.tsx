import { ChevronDown, MapPin, Settings as SettingsIcon, X } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Banner } from './components/Banner';
import { DayBar } from './components/DayBar';
import { Hero } from './components/Hero';
import { LocationSheet } from './components/LocationSheet';
import { ModeSwitch } from './components/ModeSwitch';
import { SettingsSheet } from './components/SettingsSheet';
import { Stats } from './components/Stats';
import { Upcoming } from './components/Upcoming';
import { api, applyWindowsTheme, isElectron, setLoginItem } from './lib/bridge';
import { computeSchedule, fmtTime } from './lib/engine';
import { fmt, makeT } from './lib/i18n';
import { approxPlace, detectPlaceFromTimezone } from './lib/places';
import { partsInTz, zonedTimeToDate } from './lib/sun';
import { DEFAULT_SETTINGS, Place, Settings, Theme, ThemeMode } from './types';

const STORAGE_KEY = 'winsolar_v2_settings';
const SNOOZE_MIN = 15;
const HEADSUP_MIN = 5;

function loadSettings(): Settings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw);
      const merged: Settings = { ...DEFAULT_SETTINGS, ...parsed };
      // one-time migration: older builds defaulted to Turkish, the default is now English
      if ((parsed.schema ?? 1) < 2) { merged.lang = 'en'; merged.schema = 2; }
      return merged;
    }
  } catch { /* ignore */ }
  return DEFAULT_SETTINGS;
}

export default function App() {
  const [settings, setSettings] = useState<Settings>(loadSettings);
  const [sheet, setSheet] = useState<null | 'settings' | 'location'>(null);
  const [sim, setSim] = useState<number | null>(null);
  const [tick, setTick] = useState(0);
  const [toast, setToast] = useState<string | null>(null);
  /** temporarily keeps the current theme (snooze / skip) while in Auto mode */
  const [hold, setHold] = useState<{ theme: Theme; until: number } | null>(null);

  const settingsRef = useRef(settings);
  settingsRef.current = settings;
  const t = useMemo(() => makeT(settings.lang), [settings.lang]);
  const tRef = useRef(t);
  tRef.current = t;
  const place: Place = useMemo(() => settings.place ?? approxPlace(), [settings.place]);
  const desktop = isElectron();

  const update = useCallback((patch: Partial<Settings>, silent = false) => {
    const merged = { ...settingsRef.current, ...patch };
    settingsRef.current = merged;
    setSettings(merged);
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(merged)); } catch { /* ignore */ }
    if (!silent && (patch.autoLaunch !== undefined || (patch.startMinimized !== undefined && merged.autoLaunch))) {
      void setLoginItem(merged.autoLaunch, merged.startMinimized)?.then((res) => {
        if (!res.success) {
          if (res.reason === 'dev') setToast(tRef.current('devOnly'));
          if (patch.autoLaunch) update({ autoLaunch: false }, true);
        }
      });
    }
    if (patch.hideOnBlur !== undefined) api()?.setHideOnBlur(merged.hideOnBlur);
    if (patch.notify && 'Notification' in window && Notification.permission === 'default') {
      void Notification.requestPermission().then((p) => { if (p !== 'granted') update({ notify: false }); });
    }
  }, []);

  useEffect(() => { document.documentElement.lang = settings.lang; }, [settings.lang]);

  // Desktop app wiring: transparent window styling, real autostart state, tray menu -> mode, wake from sleep.
  const setModeRef = useRef<(m: ThemeMode) => void>(() => {});
  useEffect(() => {
    const d = api();
    if (!d) return;
    document.documentElement.dataset.electron = 'true';
    d.setHideOnBlur(settingsRef.current.hideOnBlur);
    void d.getLoginItem().then((r) => { if (r.packaged) update({ autoLaunch: r.enabled }, true); });
    const offMode = d.onSetMode((m) => setModeRef.current(m));
    const offResume = d.onResume(() => setTick((n) => n + 1));
    return () => { offMode(); offResume(); };
  }, [update]);

  // Keep everything fresh: recompute every 15 s (never relies on data fetched once).
  useEffect(() => {
    const id = setInterval(() => setTick((n) => n + 1), 15000);
    return () => clearInterval(id);
  }, []);

  // First run: guess the city from the browser time zone (no permission prompt).
  useEffect(() => {
    if (settings.place && !settings.place.approx) return;
    const ctrl = new AbortController();
    detectPlaceFromTimezone(settings.lang, ctrl.signal)
      .then((p) => { if (p) update({ place: p }); })
      .catch(() => { /* offline: keep approximate location */ });
    return () => ctrl.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const now = useMemo(() => {
    void tick;
    if (sim === null) return new Date();
    const p = partsInTz(new Date(), place.timezone);
    return zonedTimeToDate(p.y, p.m, p.d, Math.floor(sim / 60), sim % 60, place.timezone);
  }, [tick, sim, place.timezone]);
  const nowMs = now.getTime();

  const sched = useMemo(
    () => computeSchedule(now, place, settings.offsetSunrise, settings.offsetSunset),
    [now, place, settings.offsetSunrise, settings.offsetSunset],
  );

  // Snooze / skip hold (Auto mode only) and manual-mode expiry
  const holdActive = settings.mode === 'auto' && hold !== null && nowMs < hold.until;
  useEffect(() => { if (hold && (nowMs >= hold.until || settings.mode !== 'auto')) setHold(null); }, [nowMs, hold, settings.mode]);
  useEffect(() => {
    if (settings.mode !== 'auto' && settings.manualUntil !== null && nowMs >= settings.manualUntil) {
      update({ mode: 'auto', manualUntil: null });
    }
  }, [nowMs, settings.mode, settings.manualUntil, update]);

  const effective: Theme = settings.mode === 'auto' ? (holdActive ? hold!.theme : sched.theme) : settings.mode;

  // Apply theme (UI + Windows via bridge); the UI change is a circular reveal from the sun/moon.
  const last = useRef<Theme | null>(null);
  useEffect(() => {
    const root = document.documentElement;
    const apply = () => { root.dataset.theme = effective; };
    const changed = last.current !== null && last.current !== effective;
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const svt = (document as unknown as { startViewTransition?: (cb: () => void) => { ready: Promise<void> } }).startViewTransition;
    if (changed && svt && !reduce) {
      const r = document.querySelector('.hero .body')?.getBoundingClientRect();
      const x = r ? r.left + r.width / 2 : window.innerWidth / 2;
      const y = r ? r.top + r.height / 2 : window.innerHeight / 3;
      const radius = Math.hypot(Math.max(x, window.innerWidth - x), Math.max(y, window.innerHeight - y));
      svt.call(document, apply).ready.then(() => {
        root.animate(
          { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${radius}px at ${x}px ${y}px)`] },
          { duration: 750, easing: 'cubic-bezier(0.4, 0, 0.2, 1)', pseudoElement: '::view-transition-new(root)' },
        );
      }).catch(() => { /* transition skipped */ });
    } else {
      apply();
    }
    if (sim === null) {
      void applyWindowsTheme(effective)?.then((r) => { if (!r.success && r.reason === 'registry-failed') setToast(tRef.current('themeFailed')); });
    }
    if (changed) {
      const msg = effective === 'light' ? t('switchedLight') : t('switchedDark');
      setToast(msg);
      if (settings.notify && 'Notification' in window && Notification.permission === 'granted') {
        new Notification('Win Solar', { body: msg, icon: './icon.png' });
      }
    }
    last.current = effective;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [effective]);

  useEffect(() => {
    if (!toast) return;
    const id = setTimeout(() => setToast(null), 3500);
    return () => clearTimeout(id);
  }, [toast]);

  const setMode = (mode: ThemeMode) => {
    setHold(null);
    if (mode === 'auto') update({ mode, manualUntil: null });
    else update({ mode, manualUntil: settings.manualUntilNext && sched.next ? sched.next.time.getTime() : null });
  };

  setModeRef.current = setMode;

  // Tray tooltip + menu mirror the current state.
  const nextTime = sched.next?.time.getTime();
  useEffect(() => {
    const d = api();
    if (!d) return;
    const themeName = (th: Theme) => (th === 'light' ? t('light') : t('dark'));
    const nextTxt = sched.next ? ` → ${themeName(sched.next.theme)} ${fmtTime(sched.next.time, place.timezone, settings.hour12)}` : '';
    d.publishState({ mode: settings.mode, theme: effective, tip: `Win Solar · ${t(settings.mode)} · ${themeName(effective)}${nextTxt}`,
      labels: { windows: t('trayWindows'), light: t('light'), dark: t('dark'), auto: t('trayAuto'), open: t('trayOpen'), quit: t('trayQuit') } });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settings.mode, effective, nextTime, place.timezone, settings.hour12, settings.lang]);

  // Optional: switch the desktop wallpaper together with the theme.
  useEffect(() => {
    const d = api();
    if (!d || !settings.wallpaperSync || sim !== null) return;
    const file = effective === 'light' ? settings.wallpaperLight : settings.wallpaperDark;
    if (!file) return;
    void d.setWallpaper(file).then((r) => { if (!r.success) setToast(tRef.current('wallpaperFailed')); });
  }, [effective, settings.wallpaperSync, settings.wallpaperLight, settings.wallpaperDark, sim]);

  const snooze = () => { if (sched.next) setHold({ theme: effective, until: sched.next.time.getTime() + SNOOZE_MIN * 60000 }); };
  const skip = () => { if (sched.next) setHold({ theme: effective, until: sched.after ? sched.after.time.getTime() : sched.next.time.getTime() + 12 * 3600000 }); };

  const headsUpNext =
    settings.headsUp && settings.mode === 'auto' && !holdActive && sched.next && sched.minutesToNext !== null &&
    sched.minutesToNext > 0 && sched.minutesToNext <= HEADSUP_MIN && sched.next.theme !== effective
      ? sched.next : null;

  const lockedUntil = settings.mode !== 'auto' && settings.manualUntil !== null
    ? fmt(t('lockedUntil'), { time: fmtTime(new Date(settings.manualUntil), place.timezone, settings.hour12) })
    : undefined;

  const closeSheet = useCallback(() => setSheet(null), []);

  return (
    <div className="stage">
      <div className="widget">
        <header className="topbar">
          <button type="button" className="place-btn" onClick={() => setSheet('location')} aria-label={t('location')}>
            <MapPin size={16} />
            <span className="place-btn-name">{place.name}</span>
            <ChevronDown size={14} />
          </button>
          {place.approx && <span className="pill-warn">{t('approx')}</span>}
          <span className="spacer" />
          <button type="button" className="icon-btn" onClick={() => setSheet('settings')} aria-label={t('settings')}>
            <SettingsIcon size={18} />
          </button>
          {desktop && (
            <button type="button" className="icon-btn" onClick={() => api()?.hideWindow()} aria-label={t('hideWindow')}>
              <X size={18} />
            </button>
          )}
        </header>

        <main className="content">
          {headsUpNext && (
            <Banner
              kind="headsup"
              text={fmt(t('switchingIn'), { theme: headsUpNext.theme === 'dark' ? t('dark') : t('light'), n: sched.minutesToNext ?? 0 })}
              actions={[{ label: t('snooze'), onClick: snooze, primary: true }, { label: t('skip'), onClick: skip }]}
            />
          )}
          {holdActive && hold && (
            <Banner
              kind="held"
              text={fmt(t('postponed'), { time: fmtTime(new Date(hold.until), place.timezone, settings.hour12) })}
              actions={[{ label: t('switchNow'), onClick: () => setHold(null), primary: true }]}
            />
          )}
          <Hero sched={sched} place={place} now={now} mode={settings.mode} t={t} lang={settings.lang} hour12={settings.hour12} />
          <ModeSwitch mode={settings.mode} onChange={setMode} t={t} lockedUntil={lockedUntil} />
          <Stats sched={sched} place={place} settings={settings} t={t} />
          <DayBar sched={sched} place={place} now={now} t={t} />
          <Upcoming place={place} now={now} t={t} lang={settings.lang} hour12={settings.hour12} />
        </main>

        {sim !== null && (
          <footer className="statusbar">
            <span className="status-dot" data-sim="true" />
            <span>{`${t('simTime')} · ${t('test')}`}</span>
          </footer>
        )}

        {sheet === 'settings' && (
          <SettingsSheet t={t} settings={settings} update={update} place={place} sim={sim} setSim={setSim} desktop={desktop} onClose={closeSheet} />
        )}
        {sheet === 'location' && (
          <LocationSheet t={t} lang={settings.lang} current={place} onSelect={(p) => update({ place: p })} onClose={closeSheet} />
        )}
        {toast && <div className="toast" role="status">{toast}</div>}
      </div>
    </div>
  );
}
