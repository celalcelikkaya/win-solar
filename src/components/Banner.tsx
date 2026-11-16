import { BellRing, Clock } from 'lucide-react';

export interface BannerAction { label: string; onClick: () => void; primary?: boolean }

export function Banner({ kind, text, actions }: { kind: 'headsup' | 'held'; text: string; actions: BannerAction[] }) {
  return (
    <div className="banner" data-kind={kind} role="status">
      <div className="banner-text">
        {kind === 'headsup' ? <BellRing size={16} /> : <Clock size={16} />}
        <span>{text}</span>
      </div>
      <div className="banner-actions">
        {actions.map((a) => (
          <button key={a.label} type="button" data-primary={a.primary ? 'true' : undefined} onClick={a.onClick}>{a.label}</button>
        ))}
      </div>
    </div>
  );
}
