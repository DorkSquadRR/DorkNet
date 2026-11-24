import { useSearchParams } from 'react-router';

// Shared horizontal tab strip — the same underlined-button style the
// per-player modal and the old Bans page already used inline, pulled
// out so the consolidated hub pages (Players / Activity / Content /
// Settings) all render their sub-tabs identically.
export function Tabs<T extends string>({
  tabs,
  active,
  onChange,
  className = '',
}: {
  tabs: ReadonlyArray<{ key: T; label: string }>;
  active: T;
  onChange: (key: T) => void;
  className?: string;
}) {
  return (
    <div className={`mb-4 flex overflow-x-auto border-b border-ink-800 text-[13.5px] ${className}`} role="tablist">
      {tabs.map(t => (
        <button
          key={t.key}
          onClick={() => onChange(t.key)}
          role="tab"
          aria-selected={active === t.key}
          className={`-mb-px whitespace-nowrap border-b-2 px-3 py-2 ${
            active === t.key
              ? 'border-ink-50 font-medium text-ink-50'
              : 'border-transparent text-ink-400 hover:text-ink-100'
          }`}
        >
          {t.label}
        </button>
      ))}
    </div>
  );
}

// Binds the active hub tab to a `?tab=` query param so the back-compat
// redirects (e.g. /bans → /players?tab=bans) land on the right sub-tab
// and the choice survives a refresh / shared link. The default tab
// drops the param entirely to keep URLs clean.
export function useTabParam<T extends string>(def: T, valid: readonly T[]): [T, (t: T) => void] {
  const [params, setParams] = useSearchParams();
  const raw = params.get('tab');
  const active = (valid as readonly string[]).includes(raw ?? '') ? (raw as T) : def;
  const set = (t: T) => {
    const next = new URLSearchParams(params);
    if (t === def) next.delete('tab');
    else next.set('tab', t);
    setParams(next, { replace: true });
  };
  return [active, set];
}
