import { Schedule } from '../lib/engine';
import { minutesOfDay } from '../lib/sun';
import { T } from '../lib/i18n';
import { Place } from '../types';

export function DayBar({ sched, place, now, t }: { sched: Schedule; place: Place; now: Date; t: T }) {
  const tz = place.timezone;
  const { light, dark } = sched.todaySwitch;
  const pct = (m: number) => `${(m / 1440) * 100}%`;
  const segs: Array<[number, number]> = [];
  if (light && dark) {
    const a = minutesOfDay(light, tz);
    const b = minutesOfDay(dark, tz);
    if (a <= b) segs.push([a, b]);
    else segs.push([0, b], [a, 1440]);
  } else if (sched.today.polar === 'day') segs.push([0, 1440]);
  const nowM = minutesOfDay(now, tz);

  return (
    <section className="card daybar" aria-label={t('todayFlow')}>
      <div className="daybar-head">
        <h3>{t('todayFlow')}</h3>
        <div className="legend">
          <span><i className="dot dot-light" />{t('light')}</span>
          <span><i className="dot dot-dark" />{t('dark')}</span>
        </div>
      </div>
      <div className="track">
        {segs.map(([a, b], i) => <span key={i} className="track-day" style={{ left: pct(a), width: `calc(${pct(b - a)})` }} />)}
        <span className="track-now" style={{ left: pct(nowM) }} />
      </div>
      <div className="ticks" aria-hidden="true">
        {['00', '06', '12', '18', '24'].map((x) => <span key={x}>{x}</span>)}
      </div>
    </section>
  );
}
