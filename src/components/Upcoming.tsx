import { ChevronDown } from 'lucide-react';
import { useMemo, useState } from 'react';
import { fmtDuration, fmtTime } from '../lib/engine';
import { T } from '../lib/i18n';
import { addDays, partsInTz, sunForDate } from '../lib/sun';
import { Lang, Place } from '../types';

export function Upcoming({ place, now, t, lang, hour12 }: { place: Place; now: Date; t: T; lang: Lang; hour12: boolean }) {
  const [open, setOpen] = useState(false);
  const tz = place.timezone;
  const p = partsInTz(now, tz);
  const key = `${p.y}-${p.m}-${p.d}-${place.lat}-${place.lng}`;

  const rows = useMemo(() => Array.from({ length: 7 }, (_, n) => {
    const a = addDays(p.y, p.m, p.d, n);
    const sun = sunForDate(a.y, a.m, a.d, place.lat, place.lng);
    const label = new Intl.DateTimeFormat(lang === 'tr' ? 'tr-TR' : 'en-GB', { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(Date.UTC(a.y, a.m - 1, a.d, 12)));
    return { label, sun };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }), [key, lang]);

  return (
    <section className="card upcoming">
      <button type="button" className="upcoming-head" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        <h3>{t('next7')}</h3>
        <span>{open ? t('hide') : t('show')}<ChevronDown size={16} data-open={open} /></span>
      </button>
      {open && (
        <table className="upcoming-table">
          <tbody>
            {rows.map(({ label, sun }, i) => (
              <tr key={i} data-today={i === 0}>
                <th scope="row">{label}</th>
                {sun.polar ? (
                  <td colSpan={3} className="muted">{sun.polar === 'day' ? t('polarDay') : t('polarNight')}</td>
                ) : (
                  <>
                    <td>{fmtTime(sun.sunrise, tz, hour12)}</td>
                    <td>{fmtTime(sun.sunset, tz, hour12)}</td>
                    <td className="muted">{sun.sunrise && sun.sunset ? fmtDuration(Math.round((sun.sunset.getTime() - sun.sunrise.getTime()) / 60000), lang) : '--'}</td>
                  </>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
