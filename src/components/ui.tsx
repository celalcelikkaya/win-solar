import { Minus, Plus } from 'lucide-react';
import { ReactNode } from 'react';

export function Toggle({ checked, onChange, disabled, label }: { checked: boolean; onChange: (v: boolean) => void; disabled?: boolean; label: string }) {
  return (
    <button
      type="button" role="switch" aria-checked={checked} aria-label={label} disabled={disabled}
      className="toggle" data-on={checked} onClick={() => onChange(!checked)}
    >
      <span className="toggle-knob" />
    </button>
  );
}

export function Stepper({ value, onChange, min = -120, max = 120, step = 5, unit }: { value: number; onChange: (v: number) => void; min?: number; max?: number; step?: number; unit: string }) {
  const set = (v: number) => onChange(Math.max(min, Math.min(max, v)));
  const text = `${value > 0 ? '+' : value < 0 ? '−' : ''}${Math.abs(value)} ${unit}`;
  return (
    <div className="stepper">
      <button type="button" aria-label="−" onClick={() => set(value - step)} disabled={value <= min}><Minus size={16} /></button>
      <output>{text}</output>
      <button type="button" aria-label="+" onClick={() => set(value + step)} disabled={value >= max}><Plus size={16} /></button>
    </div>
  );
}

export function Row({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  return (
    <div className="row">
      <div className="row-text">
        <div className="row-title">{title}</div>
        {hint && <div className="row-hint">{hint}</div>}
      </div>
      {children}
    </div>
  );
}
