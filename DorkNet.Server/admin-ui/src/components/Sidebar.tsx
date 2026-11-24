import { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import {
  Building2, Clock, LogOut, Megaphone, MessageSquare,
  Moon, Radio, Search, Settings, Sun, Users, X,
} from './Icons';
import { BrandMark } from './BrandMark';
import { CLIENT_BUILD } from '../lib/build';
import { useTheme } from '../lib/theme';
import { useOpenReports } from '../lib/useOpenReports';

// Hub pages keep their sub-views behind `?tab=` (see useTabParam) and the
// settings page behind `#anchors`; the sidebar lists them under the active
// section so the page itself doesn't need a second menu on desktop.
interface Sub { label: string; tab?: string; hash?: string; to: string }
interface Item {
  label: string;
  to: string;
  icon: React.ReactNode;
  match: (path: string) => boolean;
  subs?: Sub[];
  reports?: boolean;
}

const primary: Item[] = [
  { label: 'Live', to: '/', icon: <Radio />, match: p => p === '/' },
  {
    label: 'Players', to: '/players', icon: <Users />, match: p => p.startsWith('/players'), reports: true,
    subs: [
      { label: 'Directory', to: '/players' },
      { label: 'Bans', tab: 'bans', to: '/players?tab=bans' },
      { label: 'Reports', tab: 'reports', to: '/players?tab=reports' },
    ],
  },
  {
    label: 'Rooms', to: '/rooms', icon: <Building2 />, match: p => p.startsWith('/rooms') || p.startsWith('/import-room'),
    subs: [
      { label: 'All rooms', to: '/rooms' },
      { label: 'Import a room', to: '/import-room' },
    ],
  },
  {
    label: 'Content', to: '/content', icon: <MessageSquare />, match: p => p.startsWith('/content'),
    subs: [
      { label: 'Community board', to: '/content' },
      { label: 'Loading tips', tab: 'tips', to: '/content?tab=tips' },
      { label: 'Charades words', tab: 'charades', to: '/content?tab=charades' },
    ],
  },
  { label: 'Broadcast', to: '/broadcast', icon: <Megaphone />, match: p => p.startsWith('/broadcast') },
];

const secondary: Item[] = [
  {
    label: 'Activity', to: '/activity', icon: <Clock />, match: p => p.startsWith('/activity'),
    subs: [
      { label: 'Audit log', to: '/activity' },
      { label: 'Player requests', tab: 'logs', to: '/activity?tab=logs' },
      { label: 'Test cases', tab: 'testcases', to: '/activity?tab=testcases' },
    ],
  },
  {
    label: 'Settings', to: '/settings', icon: <Settings />, match: p => p.startsWith('/settings'),
    subs: [
      { label: 'Server', to: '/settings' },
      { label: 'Game config', hash: '#game-config', to: '/settings#game-config' },
      { label: 'Circuits limits', hash: '#circuits', to: '/settings#circuits' },
      { label: 'Weekly challenges', hash: '#weekly', to: '/settings#weekly' },
      { label: 'Signup codes', tab: 'signup', to: '/settings?tab=signup' },
    ],
  },
];

function subActive(item: Item, sub: Sub, path: string, tab: string | null, hash: string): boolean {
  if (!item.subs) return false;
  // Rooms sub-items are separate routes rather than tabs.
  if (item.to === '/rooms') return sub.to === '/import-room' ? path.startsWith('/import-room') : !path.startsWith('/import-room');
  if ((sub.tab ?? null) !== tab) return false;
  const anchored = item.subs.some(s => s.hash && s.hash === hash);
  return sub.hash ? sub.hash === hash : !anchored;
}

// Two-mode rendering:
//   * Desktop (md+): always-visible rail to the left of <main>.
//   * Mobile: a drawer opened from the top bar's menu button. Picking a
//     destination closes it (onNavigate); Escape and the scrim close it too.
export function Sidebar({ open, onClose, onNavigate, onLogout, name }: {
  open: boolean;
  onClose: () => void;
  onNavigate: () => void;
  onLogout: () => void;
  name: string;
}) {
  const searchRef = useRef<HTMLInputElement>(null);
  const reports = useOpenReports();

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = prev;
    };
  }, [open, onClose]);

  // "/" jumps to the player search from anywhere that isn't a text field.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) return;
      const el = e.target as HTMLElement | null;
      if (el && (el.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName))) return;
      if (!searchRef.current || searchRef.current.offsetParent === null) return;
      e.preventDefault();
      searchRef.current.focus();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);

  const props = { onNavigate, onLogout, name, reports };
  return (
    <>
      {/* Mobile drawer + scrim */}
      <div
        className={`md:hidden fixed inset-0 z-40 bg-black/50 transition-opacity ${open ? 'opacity-100' : 'pointer-events-none opacity-0'}`}
        onClick={onClose}
        aria-hidden="true"
      />
      <aside
        className={`md:hidden fixed inset-y-0 left-0 z-50 flex w-72 max-w-[85vw] flex-col border-r border-ink-800 bg-bar shadow-float transition-transform duration-200 ${open ? 'translate-x-0' : '-translate-x-full'}`}
        aria-hidden={!open}
        inert={!open}
        aria-label="Navigation"
      >
        <SidebarBody {...props} onClose={onClose} mobile />
      </aside>
      {/* Desktop rail */}
      <aside className="hidden md:flex w-56 shrink-0 flex-col border-r border-ink-800 bg-bar" aria-label="Navigation">
        <SidebarBody {...props} searchRef={searchRef} />
      </aside>
    </>
  );
}

function SidebarBody({ onNavigate, onLogout, onClose, name, reports, mobile, searchRef }: {
  onNavigate: () => void;
  onLogout: () => void;
  onClose?: () => void;
  name: string;
  reports: number | null;
  mobile?: boolean;
  searchRef?: React.RefObject<HTMLInputElement | null>;
}) {
  const location = useLocation();
  const navigate = useNavigate();
  const [theme, toggleTheme] = useTheme();
  const [query, setQuery] = useState('');
  const tab = new URLSearchParams(location.search).get('tab');

  const search = (e: React.FormEvent) => {
    e.preventDefault();
    const q = query.trim();
    navigate(q ? `/players?query=${encodeURIComponent(q)}` : '/players');
    setQuery('');
    onNavigate();
  };

  const renderItem = (item: Item) => {
    const active = item.match(location.pathname);
    return (
      <div key={item.to}>
        <Link
          to={item.to}
          onClick={onNavigate}
          aria-current={active ? 'page' : undefined}
          className={`flex items-center gap-2.5 rounded-[5px] px-2 text-[13.5px] ${mobile ? 'h-10 text-[15px]' : 'h-8'} ${
            active ? 'bg-ink-800 font-medium text-ink-50' : 'text-ink-100 hover:bg-ink-850'
          }`}
        >
          <span className={active ? 'text-ink-50' : 'text-ink-400'}>{item.icon}</span>
          <span className="flex-1">{item.label}</span>
          {item.reports && !!reports && (
            <span className="text-xs font-medium text-warn tabular-nums" title={`${reports} open report${reports === 1 ? '' : 's'}`}>{reports}</span>
          )}
        </Link>
        {active && item.subs && (
          <div className="mb-1 mt-0.5 flex flex-col">
            {item.subs.map(sub => {
              const on = subActive(item, sub, location.pathname, tab, location.hash);
              return (
                <Link
                  key={sub.to}
                  to={sub.to}
                  onClick={onNavigate}
                  aria-current={on ? 'page' : undefined}
                  className={`flex items-center rounded-[5px] pl-[34px] pr-2 text-[13px] ${mobile ? 'h-9 text-[14px]' : 'h-7'} ${
                    on ? 'text-ink-50' : 'text-ink-400 hover:bg-ink-850 hover:text-ink-100'
                  }`}
                >
                  <span className="flex-1">{sub.label}</span>
                  {sub.tab === 'reports' && !!reports && <span className="text-xs font-medium text-warn tabular-nums">{reports}</span>}
                </Link>
              );
            })}
          </div>
        )}
      </div>
    );
  };

  return (
    <div className="flex h-full min-h-0 flex-col px-2.5 pb-2.5 pt-3">
      <div className="flex items-center gap-2 px-2 pb-3">
        <BrandMark className="size-6" />
        <span className="font-semibold text-ink-50">DorkNet</span>
        <span className="ml-auto text-xs text-ink-400 tabular-nums" title="Rec Room client build">{CLIENT_BUILD}</span>
        {mobile && (
          <button onClick={onClose} className="-mr-1 ml-1 rounded-[5px] p-1 text-ink-400 hover:bg-ink-850 hover:text-ink-100" aria-label="Close menu">
            <X />
          </button>
        )}
      </div>

      <form onSubmit={search} className="relative mb-3" role="search">
        <Search className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-ink-400" />
        <input
          ref={searchRef}
          value={query}
          onChange={e => setQuery(e.target.value)}
          placeholder="Find a player"
          aria-label="Find a player"
          className={`input pl-8 pr-7 ${mobile ? 'py-2 text-[15px]' : ''}`}
        />
        {!mobile && <kbd className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 font-sans text-xs text-ink-500">/</kbd>}
      </form>

      <nav className="flex min-h-0 flex-1 flex-col gap-px overflow-y-auto">
        {primary.map(renderItem)}
        <div className="mx-2 my-2 h-px bg-ink-800" />
        {secondary.map(renderItem)}
      </nav>

      <div className="mt-2 flex items-center gap-0.5 border-t border-ink-800 pl-2 pt-2 text-[13px]">
        <span className="min-w-0 flex-1 truncate text-ink-200">{name}</span>
        <button
          onClick={toggleTheme}
          className="rounded-[5px] p-1.5 text-ink-400 hover:bg-ink-850 hover:text-ink-100"
          aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
          title={theme === 'dark' ? 'Light theme' : 'Dark theme'}
        >
          {theme === 'dark' ? <Sun /> : <Moon />}
        </button>
        <button
          onClick={onLogout}
          className="rounded-[5px] p-1.5 text-ink-400 hover:bg-ink-850 hover:text-ink-100"
          aria-label="Sign out"
          title="Sign out"
        >
          <LogOut />
        </button>
      </div>
    </div>
  );
}
