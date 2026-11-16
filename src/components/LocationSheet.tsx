import { Check, LocateFixed, MapPin, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Sheet } from './Sheet';
import { T } from '../lib/i18n';
import { getBrowserPosition, placeLabel, POPULAR, searchPlaces } from '../lib/places';
import { Lang, Place } from '../types';

interface Props { t: T; lang: Lang; current: Place; onSelect: (p: Place) => void; onClose: () => void }

export function LocationSheet({ t, lang, current, onSelect, onClose }: Props) {
  const [q, setQ] = useState('');
  const [results, setResults] = useState<Place[] | null>(null);
  const [state, setState] = useState<'idle' | 'loading' | 'error'>('idle');
  const [geoBusy, setGeoBusy] = useState(false);
  const [geoErr, setGeoErr] = useState(false);

  useEffect(() => {
    const query = q.trim();
    if (query.length < 2) { setResults(null); setState('idle'); return; }
    const ctrl = new AbortController();
    setState('loading');
    const id = setTimeout(async () => {
      try {
        setResults(await searchPlaces(query, lang, ctrl.signal));
        setState('idle');
      } catch (e) {
        if ((e as Error).name !== 'AbortError') { setResults(null); setState('error'); }
      }
    }, 350);
    return () => { clearTimeout(id); ctrl.abort(); };
  }, [q, lang]);

  const pick = (p: Place) => { onSelect(p); onClose(); };
  const useGps = async () => {
    setGeoBusy(true); setGeoErr(false);
    try { pick(await getBrowserPosition()); } catch { setGeoErr(true); } finally { setGeoBusy(false); }
  };
  const same = (p: Place) => Math.abs(p.lat - current.lat) < 0.01 && Math.abs(p.lng - current.lng) < 0.01;
  const list = results ?? POPULAR;

  return (
    <Sheet title={t('location')} closeLabel={t('close')} onClose={onClose}>
      <label className="search">
        <Search size={18} />
        <input autoFocus type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder={t('searchPlaceholder')} aria-label={t('searchPlaceholder')} />
      </label>

      <button type="button" className="btn-primary" onClick={useGps} disabled={geoBusy}>
        <LocateFixed size={16} className={geoBusy ? 'spin' : ''} />{t('useMyLocation')}
      </button>
      {geoErr && <p className="msg msg-warn">{t('geoFailed')}</p>}
      {state === 'error' && <p className="msg msg-warn">{t('searchFailed')}</p>}

      <h3 className="group-title">{results ? t('results') : t('popular')}</h3>
      {results && results.length === 0 && state === 'idle' && <p className="msg">{t('noResults')}</p>}
      <ul className="places">
        {(state === 'error' ? POPULAR : list).map((p) => (
          <li key={`${p.name}-${p.lat}-${p.lng}`}>
            <button type="button" className="place" data-active={same(p)} onClick={() => pick(p)}>
              <MapPin size={16} />
              <span className="place-text">
                <span className="place-name">{p.name}</span>
                <span className="place-sub">{[p.region, p.country].filter(Boolean).join(', ') || placeLabel(p)}</span>
              </span>
              {same(p) && <Check size={16} />}
            </button>
          </li>
        ))}
      </ul>
    </Sheet>
  );
}
