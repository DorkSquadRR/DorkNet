import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useEffect, useState, useSyncExternalStore } from 'react';
import { Sidebar } from './Sidebar';
import { LogOut, Menu } from './Icons';
import { clearSession, getMe, notifyAuthChange, subscribeAuth } from '../lib/auth';

export function Layout() {
  const navigate = useNavigate();
  const location = useLocation();
  const me = useSyncExternalStore(subscribeAuth, getMe, () => null);
  const [drawerOpen, setDrawerOpen] = useState(false);

  // Defensive: close the drawer on route change in case the user opens
  // it then navigates via something other than the sidebar links
  // (e.g. the legacy importer link in PageHeader actions). The drawer
  // also closes via NavLink onClick → onNavigate, so this is a belt.
  useEffect(() => { setDrawerOpen(false); }, [location.pathname]);

  const logout = () => {
    clearSession();
    notifyAuthChange();
    navigate('/login', { replace: true });
  };

  return (
    <div className="flex h-full">
      <Sidebar
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        onNavigate={() => setDrawerOpen(false)}
      />
      <div className="flex flex-1 flex-col min-w-0">
        <header className="flex h-14 items-center gap-2 border-b border-ink-800 bg-ink-900/60 px-3 sm:px-5">
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            className="md:hidden rounded-md p-1.5 text-ink-200 hover:bg-ink-800"
            aria-label="Open menu"
            aria-expanded={drawerOpen}
          >
            <Menu />
          </button>
          <div className="flex-1 text-sm text-ink-300 truncate">
            {/* Tighter copy on small screens — the "Signed in as" prefix
                eats too much width on a phone. Tablet+ shows the full
                phrase. */}
            <span className="hidden sm:inline">Signed in as </span>
            <span className="font-medium text-ink-50">
              {me?.displayName ?? me?.username ?? `#${me?.id ?? '?'}`}
            </span>
          </div>
          <button onClick={logout} className="btn-ghost text-xs shrink-0" aria-label="Sign out">
            <LogOut />
            <span className="hidden sm:inline">Sign out</span>
          </button>
        </header>
        <main className="flex-1 overflow-auto px-3 sm:px-5 py-4 sm:py-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
