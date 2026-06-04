import { useEffect } from 'react';
import { NavLink } from 'react-router-dom';
import {
  LayoutDashboard, Users, Building2, Activity,
  Megaphone, MessageSquare, Settings, X,
} from './Icons';

// Consolidated navigation. Per-player moderation (bans, grants, gifts,
// password resets) lives in the player detail modal; the old Bans /
// Reports / Audit / Player-logs / Community / Loading-tips / Gift /
// Passwords / Signup-codes pages are now tabs under Players, Activity,
// Content, and Settings. Import room is a header action on the Rooms
// page. Old routes still resolve via redirects in App.tsx.
const groups: Array<{ title: string; items: Array<{ to: string; label: string; icon: React.ReactNode; new?: boolean }>}> = [
  {
    title: 'Overview',
    items: [
      { to: '/',          label: 'Dashboard', icon: <LayoutDashboard /> },
    ],
  },
  {
    title: 'Moderation',
    items: [
      { to: '/players',   label: 'Players',  icon: <Users /> },
      { to: '/activity',  label: 'Activity', icon: <Activity /> },
    ],
  },
  {
    title: 'Content',
    items: [
      { to: '/rooms',  label: 'Rooms',   icon: <Building2 /> },
      { to: '/content', label: 'Content', icon: <MessageSquare /> },
    ],
  },
  {
    title: 'Operations',
    items: [
      { to: '/broadcast', label: 'Broadcast', icon: <Megaphone /> },
      { to: '/settings',  label: 'Settings',  icon: <Settings /> },
    ],
  },
];

// Two-mode rendering:
//   * Desktop (md+): always-visible static rail to the left of <main>.
//   * Mobile: hidden by default; opens as a slide-in drawer with a
//     backdrop scrim, controlled by `open` from <Layout>. Tapping any
//     nav link calls `onNavigate()` so the drawer closes automatically
//     after the user picks a destination. Escape also closes.
export function Sidebar({ open, onClose, onNavigate }: { open: boolean; onClose: () => void; onNavigate: () => void }) {
  // Lock body scroll while the mobile drawer is up so the page behind
  // doesn't scroll through the backdrop. Skip on desktop where `open`
  // is irrelevant (the sidebar is always there).
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

  const inner = (
    <>
      <div className="px-2 pb-4 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <div className="size-8 rounded-md bg-gradient-to-br from-brand-400 to-brand-600 flex items-center justify-center font-bold text-white">D</div>
          <div>
            <div className="text-sm font-semibold tracking-tight text-ink-50">DorkNet</div>
            <div className="text-[10px] uppercase tracking-widest text-ink-400">admin</div>
          </div>
        </div>
        <button
          onClick={onClose}
          className="md:hidden rounded-md p-1 text-ink-400 hover:bg-ink-800 hover:text-ink-200"
          aria-label="Close menu"
        >
          <X />
        </button>
      </div>
      <nav className="flex flex-col gap-3 overflow-y-auto">
        {groups.map(group => (
          <div key={group.title}>
            <div className="px-2 pb-1 text-[10px] font-semibold uppercase tracking-widest text-ink-400">{group.title}</div>
            <div className="flex flex-col gap-0.5">
              {group.items.map(item => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.to === '/'}
                  onClick={onNavigate}
                  className={({ isActive }) =>
                    `group flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm transition-colors ${
                      isActive
                        ? 'bg-brand-500/15 text-brand-100 ring-1 ring-inset ring-brand-500/30'
                        : 'text-ink-200 hover:bg-ink-800 hover:text-ink-50'
                    }`
                  }
                >
                  <span className="text-ink-300 group-[.active]:text-brand-200">{item.icon}</span>
                  <span className="flex-1">{item.label}</span>
                  {item.new && <span className="badge badge-neutral !bg-brand-500/20 !text-brand-200">new</span>}
                </NavLink>
              ))}
            </div>
          </div>
        ))}
      </nav>
    </>
  );

  return (
    <>
      {/* Mobile drawer + backdrop */}
      <div
        className={`md:hidden fixed inset-0 z-40 transition-opacity ${open ? 'opacity-100 pointer-events-auto' : 'opacity-0 pointer-events-none'}`}
        onClick={onClose}
        aria-hidden={!open}
      >
        <div className="absolute inset-0 bg-ink-950/70 backdrop-blur-sm" />
      </div>
      <aside
        className={`md:hidden fixed inset-y-0 left-0 z-50 w-72 max-w-[85vw] flex flex-col gap-1 border-r border-ink-800 bg-ink-900 px-3 py-4 transition-transform duration-200 ${open ? 'translate-x-0' : '-translate-x-full'}`}
        aria-hidden={!open}
        role="dialog"
        aria-label="Navigation"
      >
        {inner}
      </aside>
      {/* Desktop static rail */}
      <aside className="hidden md:flex md:w-60 lg:w-64 flex-col gap-1 border-r border-ink-800 bg-ink-900/60 px-3 py-4">
        {inner}
      </aside>
    </>
  );
}
