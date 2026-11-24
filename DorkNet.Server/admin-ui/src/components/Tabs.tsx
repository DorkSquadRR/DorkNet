import { useRef } from 'react';
import { useSearchParams } from 'react-router';

// Shared horizontal tab strip used by the hub pages (Players / Activity /
// Content / Settings) and the player panel. Follows the ARIA tabs
// pattern: only the active tab is a Tab stop, Left/Right/Home/End move
// between tabs (and select them), and every tab points at the one
// <TabPanel> that shows the active tab's content. `idBase` must be
// unique on the page (use React's useId) and shared with the TabPanel.
export function Tabs<T extends string>({
  tabs,
  active,
  onChange,
  idBase,
  className = '',
}: {
  tabs: ReadonlyArray<{ key: T; label: string }>;
  active: T;
  onChange: (key: T) => void;
  idBase: string;
  className?: string;
}) {
  const refs = useRef<Array<HTMLButtonElement | null>>([]);

  const onKeyDown = (e: React.KeyboardEvent, index: number) => {
    let next: number;
    if (e.key === 'ArrowRight') next = (index + 1) % tabs.length;
    else if (e.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
    else if (e.key === 'Home') next = 0;
    else if (e.key === 'End') next = tabs.length - 1;
    else return;
    e.preventDefault();
    refs.current[next]?.focus();
    onChange(tabs[next].key);
  };

  return (
    <div className={`mb-4 flex overflow-x-auto border-b border-ink-800 text-[13.5px] ${className}`} role="tablist">
      {tabs.map((t, i) => (
        <button
          key={t.key}
          ref={el => { refs.current[i] = el; }}
          type="button"
          id={`${idBase}-tab-${t.key}`}
          role="tab"
          aria-selected={active === t.key}
          aria-controls={`${idBase}-panel`}
          tabIndex={active === t.key ? 0 : -1}
          onClick={() => onChange(t.key)}
          onKeyDown={e => onKeyDown(e, i)}
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

// The content area the Tabs above control. Render the active tab's
// content inside it.
export function TabPanel({ idBase, active, children }: {
  idBase: string;
  active: string;
  children: React.ReactNode;
}) {
  return (
    <div role="tabpanel" id={`${idBase}-panel`} aria-labelledby={`${idBase}-tab-${active}`}>
      {children}
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
