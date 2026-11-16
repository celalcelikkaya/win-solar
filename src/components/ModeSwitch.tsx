import { Moon, Sparkles, Sun } from 'lucide-react';
import { T } from '../lib/i18n';
import { ThemeMode } from '../types';

const MODES: ThemeMode[] = ['auto', 'light', 'dark'];

export function ModeSwitch({ mode, onChange, t, lockedUntil }: { mode: ThemeMode; onChange: (m: ThemeMode) => void; t: T; lockedUntil?: string }) {
  const idx = MODES.indexOf(mode);
  const icon = { auto: <Sparkles size={16} />, light: <Sun size={16} />, dark: <Moon size={16} /> };
  const hint = lockedUntil ?? { auto: t('modeAuto'), light: t('modeLight'), dark: t('modeDark') }[mode];
  return (
    <div>
      <div className="seg" role="radiogroup" aria-label="Mode">
        <span className="seg-thumb" style={{ transform: `translateX(${idx * 100}%)` }} />
        {MODES.map((m) => (
          <button key={m} type="button" role="radio" aria-checked={mode === m} data-active={mode === m} onClick={() => onChange(m)}>
            {icon[m]}<span>{t(m)}</span>
          </button>
        ))}
      </div>
      <p className="hint">{hint}</p>
    </div>
  );
}
