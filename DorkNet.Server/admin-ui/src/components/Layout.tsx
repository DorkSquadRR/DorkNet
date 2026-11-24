import { Link, Outlet, useLocation, useNavigate } from 'react-router';
import { useCallback, useEffect, useState, useSyncExternalStore } from 'react';
import { Sidebar } from './Sidebar';
import { BrandMark } from './BrandMark';
import { Menu, Search } from './Icons';
import { clearSession, getMe, notifyAuthChange, subscribeAuth } from '../lib/auth';
import { CLIENT_BUILD } from '../lib/build';

export function Layout() {
  const navigate = useNavigate();
  const location = useLocation();
  const me = useSyncExternalStore(subscribeAuth, getMe, () => null);
  const [drawerOpen, setDrawerOpen] = useState(false);

  // Belt and braces: close the drawer on any route change, not just the
  // ones that go through a sidebar link.
  useEffect(() => { setDrawerOpen(false); }, [location.pathname, location.search]);

  const closeDrawer = useCallback(() => setDrawerOpen(false), []);

  const logout = () => {
    clearSession();
    notifyAuthChange();
    navigate('/login', { replace: true });
  };

  // The Live page is a full-height split view; every other page gets the
  // standard content padding.
  const fullBleed = location.pathname === '/';

  return (
    <div className="flex h-full">
      <Sidebar
        open={drawerOpen}
        onClose={closeDrawer}
        onNavigate={closeDrawer}
        onLogout={logout}
        name={me?.displayName || me?.username || `#${me?.id ?? '?'}`}
      />
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-12 shrink-0 items-center gap-1 border-b border-ink-800 bg-bar px-2 md:hidden">
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            className="rounded-[5px] p-2 text-ink-200 hover:bg-ink-850"
            aria-label="Open menu"
            aria-expanded={drawerOpen}
          >
            <Menu className="size-5" />
          </button>
          <Link to="/" className="flex items-center gap-2 px-1">
            <BrandMark className="size-6" />
            <span className="font-semibold text-ink-50">DorkNet</span>
          </Link>
          <span className="ml-1 text-xs text-ink-400 tabular-nums">{CLIENT_BUILD}</span>
          <Link to="/players" className="ml-auto rounded-[5px] p-2 text-ink-200 hover:bg-ink-850" aria-label="Find a player">
            <Search className="size-5" />
          </Link>
        </header>
        <main className={`min-h-0 flex-1 ${fullBleed ? 'overflow-hidden' : 'overflow-auto px-4 py-4 sm:px-7 sm:py-6'}`}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
