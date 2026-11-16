import { Power, RotateCcw } from 'lucide-react';
import { Sheet } from './Sheet';
import { Row, Stepper, Toggle } from './ui';
import { T } from '../lib/i18n';
import { fmtTime } from '../lib/engine';
import { partsInTz, zonedTimeToDate } from '../lib/sun';
import { APP_NAME, APP_VERSION } from '../lib/about';
import { api } from '../lib/bridge';
import { Place, Settings } from '../types';

interface Props {
  t: T;
  settings: Settings;
  update: (p: Partial<Settings>) => void;
  place: Place;
  sim: number | null;
  setSim: (v: number | null) => void;
  desktop: boolean;
  onClose: () => void;
}

export function SettingsSheet({ t, settings, update, place, sim, setSim, desktop, onClose }: Props) {
  const real = partsInTz(new Date(), place.timezone);
  const simValue = sim ?? real.h * 60 + real.min;
  const simLabel = fmtTime(zonedTimeToDate(real.y, real.m, real.d, Math.floor(simValue / 60), simValue % 60, place.timezone), place.timezone, settings.hour12);

  const fileName = (p: string | null) => (p ? p.split(/[\\/]/).pop() ?? p : t('notSet'));
  const pick = async (key: 'wallpaperLight' | 'wallpaperDark') => {
    const r = await api()?.pickWallpaper();
    if (r?.success && r.path) update({ [key]: r.path });
  };

  return (
    <Sheet title={t('settings')} closeLabel={t('close')} onClose={onClose}>
      <h3 className="group-title">{t('timing')}</h3>
      <div className="group">
        <Row title={t('sunriseOffset')} hint={t('offsetHint')}>
          <Stepper value={settings.offsetSunrise} onChange={(v) => update({ offsetSunrise: v })} unit={t('min')} />
        </Row>
        <Row title={t('sunsetOffset')}>
          <Stepper value={settings.offsetSunset} onChange={(v) => update({ offsetSunset: v })} unit={t('min')} />
        </Row>
        <Row title={t('headsUp')} hint={t('headsUpHint')}>
          <Toggle label={t('headsUp')} checked={settings.headsUp} onChange={(v) => update({ headsUp: v })} />
        </Row>
        <Row title={t('manualUntil')} hint={t('manualUntilHint')}>
          <Toggle label={t('manualUntil')} checked={settings.manualUntilNext} onChange={(v) => update({ manualUntilNext: v })} />
        </Row>
      </div>

      <h3 className="group-title">{t('general')}</h3>
      <div className="group">
        <Row title={t('autoLaunch')} hint={desktop ? undefined : t('desktopOnly')}>
          <Toggle label={t('autoLaunch')} checked={settings.autoLaunch} onChange={(v) => update({ autoLaunch: v })} disabled={!desktop} />
        </Row>
        <Row title={t('startMinimized')} hint={desktop ? undefined : t('desktopOnly')}>
          <Toggle label={t('startMinimized')} checked={settings.startMinimized} onChange={(v) => update({ startMinimized: v })} disabled={!desktop} />
        </Row>
        <Row title={t('notify')}>
          <Toggle label={t('notify')} checked={settings.notify} onChange={(v) => update({ notify: v })} />
        </Row>
        {desktop && (
          <Row title={t('hideOnBlur')} hint={t('hideOnBlurHint')}>
            <Toggle label={t('hideOnBlur')} checked={settings.hideOnBlur} onChange={(v) => update({ hideOnBlur: v })} />
          </Row>
        )}
        <Row title={t('wallpaper')} hint={desktop ? undefined : t('desktopOnly')}>
          <Toggle label={t('wallpaper')} checked={settings.wallpaperSync} onChange={(v) => update({ wallpaperSync: v })} disabled={!desktop} />
        </Row>
        {desktop && settings.wallpaperSync && (
          <>
            <Row title={t('wallpaperLight')} hint={fileName(settings.wallpaperLight)}>
              <button type="button" className="btn-ghost" onClick={() => pick('wallpaperLight')}>{t('choose')}</button>
            </Row>
            <Row title={t('wallpaperDark')} hint={fileName(settings.wallpaperDark)}>
              <button type="button" className="btn-ghost" onClick={() => pick('wallpaperDark')}>{t('choose')}</button>
            </Row>
          </>
        )}
      </div>

      <h3 className="group-title">{t('appearance')}</h3>
      <div className="group">
        <Row title={t('language')}>
          <div className="mini-seg" role="radiogroup" aria-label={t('language')}>
            {(['tr', 'en'] as const).map((l) => (
              <button key={l} type="button" role="radio" aria-checked={settings.lang === l} data-active={settings.lang === l} onClick={() => update({ lang: l })}>{l.toUpperCase()}</button>
            ))}
          </div>
        </Row>
        <Row title={t('clock24')}>
          <Toggle label={t('clock24')} checked={!settings.hour12} onChange={(v) => update({ hour12: !v })} />
        </Row>
      </div>

      <h3 className="group-title">{t('test')}</h3>
      <div className="group group-pad">
        <div className="row-hint">{t('testHint')}</div>
        <div className="sim">
          <input type="range" min={0} max={1439} step={1} value={simValue} aria-label={t('simTime')} onChange={(e) => setSim(Number(e.target.value))} />
          <output>{sim === null ? t('now') : simLabel}</output>
        </div>
        <button type="button" className="btn-ghost" disabled={sim === null} onClick={() => setSim(null)}><RotateCcw size={14} />{t('reset')}</button>
      </div>

      {desktop && (
        <button type="button" className="btn-ghost btn-danger" onClick={() => api()?.quit()}><Power size={14} />{t('quit')}</button>
      )}

      <div className="about">
        <div className="about-row">
          <img src="./icon.svg" alt="" width={44} height={44} />
          <div>
            <div className="about-name">{APP_NAME} <span>v{APP_VERSION}</span></div>
            <div className="about-by">{t('madeBy')}</div>
          </div>
        </div>
      </div>
    </Sheet>
  );
}
