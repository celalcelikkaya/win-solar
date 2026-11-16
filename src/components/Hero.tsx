import { Moon, Sun, Sunrise, Sunset } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { fmtDuration, fmtTime, Schedule } from '../lib/engine';
import { partsInTz } from '../lib/sun';
import { T } from '../lib/i18n';
import { Lang, Place, ThemeMode } from '../types';

/** Eases a number toward its target (sun sweeps along the arc on open and on every update). */
function useTween(target: number, ms = 1300) {
  const [v, setV] = useState(0);
  const from = useRef(0);
  useEffect(() => {
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduce) { from.current = target; setV(target); return; }
    const start = performance.now();
    const f = from.current;
    let raf = 0;
    const step = (now: number) => {
      const k = Math.min(1, (now - start) / ms);
      const cur = f + (target - f) * (1 - Math.pow(1 - k, 3));
      from.current = cur;
      setV(cur);
      if (k < 1) raf = requestAnimationFrame(step);
    };
    raf = requestAnimationFrame(step);
    return () => cancelAnimationFrame(raf);
  }, [target, ms]);
  return v;
}

const STARS = [[30, 22, 1.3], [64, 48, 1], [102, 18, 1.1], [138, 40, 0.9], [196, 16, 1.2], [232, 44, 1], [270, 24, 1.4], [292, 58, 0.9], [46, 70, 0.8], [250, 76, 0.9]];

export function Hero({ sched, place, now, mode, t, lang, hour12 }: { sched: Schedule; place: Place; now: Date; mode: ThemeMode; t: T; lang: Lang; hour12: boolean }) {
  const tz = place.timezone;
  const { phase, prev, next, minutesToNext, theme, today } = sched;
  const progress = useTween(sched.progress);
  const cx = 160, cy = 104, r = 92;
  const ang = Math.PI * (1 - progress);
  const sx = cx + r * Math.cos(ang);
  const sy = cy - r * Math.sin(ang);
  const isNightSky = phase === 'night' || phase === 'dusk';
  const phaseLabel = { night: t('phaseNight'), dawn: t('phaseDawn'), day: t('phaseDay'), dusk: t('phaseDusk') }[phase];
  const kindIcon = (k?: 'sunrise' | 'sunset') => (k === 'sunset' ? <Sunset size={16} /> : <Sunrise size={16} />);

  let big: string;
  let cap: string;
  if (next && minutesToNext !== null) {
    big = minutesToNext === 0 ? t('now') : fmtDuration(minutesToNext, lang);
    const label = mode === 'auto' ? (next.theme === 'dark' ? t('toDark') : t('toLight')) : next.kind === 'sunset' ? t('toSunset') : t('toSunrise');
    const p0 = partsInTz(now, tz);
    const p1 = partsInTz(next.time, tz);
    const tomorrow = p1.d !== p0.d ? ` · ${t('tomorrow')}` : '';
    cap = `${label} · ${fmtTime(next.time, tz, hour12)}${tomorrow}`;
  } else {
    big = today.polar === 'day' ? '☀' : '☾';
    cap = today.polar === 'day' ? t('polarDay') : today.polar === 'night' ? t('polarNight') : t('noEvent');
  }

  return (
    <section className="hero" data-phase={phase} aria-label={phaseLabel}>
      <div className="hero-top">
        <span className="hero-chip">{theme === 'light' ? <Sun size={14} /> : <Moon size={14} />}{phaseLabel}</span>
        <span className="hero-clock">{fmtTime(now, tz, hour12)}</span>
      </div>

      <svg className="hero-arc" viewBox="0 0 320 124" role="img" aria-hidden="true">
        <defs>
          <mask id="moon-mask"><rect x="-20" y="-20" width="40" height="40" fill="#fff" /><circle cx="5" cy="-3" r="8" fill="#000" /></mask>
        </defs>
        <g className="stars" style={{ opacity: isNightSky ? (phase === 'night' ? 1 : 0.55) : 0 }}>
          {STARS.map(([x, y, s], i) => <circle key={i} className="star" style={{ animationDelay: `${(i * 0.37) % 3}s` }} cx={x} cy={y} r={s} fill="#fff" />)}
        </g>
        <line x1="14" y1={cy} x2="306" y2={cy} className="horizon" />
        <path d={`M ${cx - r} ${cy} A ${r} ${r} 0 0 1 ${cx + r} ${cy}`} className="arc-track" />
        <path d={`M ${cx - r} ${cy} A ${r} ${r} 0 0 1 ${cx + r} ${cy}`} className="arc-progress" pathLength={100} strokeDasharray={`${progress * 100} 200`} />
        <g transform={`translate(${sx} ${sy})`} className="body">
          {theme === 'light' || phase === 'dawn' ? (
            <>
              <circle r="20" fill="#ffd166" className="glow" />
              <circle r="9.5" fill="#ffd166" />
            </>
          ) : (
            <>
              <circle r="20" fill="#cfd8ff" className="glow glow-moon" />
              <circle r="9.5" fill="#eef1ff" mask="url(#moon-mask)" />
            </>
          )}
        </g>
      </svg>

      <div className="arc-times">
        <span>{kindIcon(prev?.kind)}{prev ? fmtTime(prev.time, tz, hour12) : '--:--'}</span>
        <span>{next ? fmtTime(next.time, tz, hour12) : '--:--'}{kindIcon(next?.kind)}</span>
      </div>

      <div className="hero-count">
        <div className="hero-big">{big}</div>
        <div className="hero-cap">{cap}</div>
      </div>
    </section>
  );
}
