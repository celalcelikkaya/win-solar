import { Sun, Sunrise, Sunset, Timer } from 'lucide-react';
import { fmtDuration, fmtTime, Schedule } from '../lib/engine';
import { T } from '../lib/i18n';
import { Lang, Place, Settings } from '../types';

export function Stats({ sched, place, settings, t }: { sched: Schedule; place: Place; settings: Settings; t: T }) {
  const tz = place.timezone;
  const { today, todaySwitch, dayLengthMin } = sched;
  const h = settings.hour12;
  const lang: Lang = settings.lang;
  const tiles = [
    { icon: <Sunrise size={18} />, label: t('sunrise'), value: fmtTime(today.sunrise, tz, h), sub: settings.offsetSunrise !== 0 ? `${t('switchAt')} ${fmtTime(todaySwitch.light, tz, h)}` : '' },
    { icon: <Sunset size={18} />, label: t('sunset'), value: fmtTime(today.sunset, tz, h), sub: settings.offsetSunset !== 0 ? `${t('switchAt')} ${fmtTime(todaySwitch.dark, tz, h)}` : '' },
    { icon: <Sun size={18} />, label: t('noon'), value: fmtTime(today.noon, tz, h), sub: '' },
    { icon: <Timer size={18} />, label: t('dayLength'), value: dayLengthMin === null ? '--' : fmtDuration(dayLengthMin, lang), sub: '' },
  ];
  return (
    <div className="stats">
      {tiles.map((x) => (
        <div className="stat" key={x.label}>
          <div className="stat-icon">{x.icon}</div>
          <div className="stat-text">
            <div className="stat-label">{x.label}</div>
            <div className="stat-value">{x.value}</div>
            {x.sub && <div className="stat-sub">{x.sub}</div>}
          </div>
        </div>
      ))}
    </div>
  );
}
