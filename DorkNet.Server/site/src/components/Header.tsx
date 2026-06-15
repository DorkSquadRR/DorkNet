import { NavLink, Link, useLocation, useNavigate } from 'react-router-dom';
import { useEffect, useRef, useState } from 'react';
import { BrandMark } from './BrandMark';

const NAV = [
  { to: '/', label: 'Home', exact: true },
  { to: '/feed', label: 'Feed' },
  { to: '/players', label: 'Players' },
  { to: '/rooms', label: 'Rooms' },
  { to: '/join', label: 'Join' },
];

export function Header() {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState('');
  const nav = useNavigate();
  const loc = useLocation();
  const inputRef = useRef<HTMLInputElement>(null);

  // Close the mobile drawer on route change so tapping a nav item
  // doesn't leave a half-open overlay covering the page.
  useEffect(() => { setOpen(false); }, [loc.pathname]);

  const submitSearch = (e: React.FormEvent) => {
    e.preventDefault();
    const v = q.trim();
    if (v) nav(`/players?q=${encodeURIComponent(v)}`);
  };

  return (
    <header className="sticky top-0 z-40 border-b border-ink-800 bg-ink-950/85 backdrop-blur">
      <div className="mx-auto max-w-6xl flex items-center gap-4 px-4 sm:px-6 py-3">
        <Link to="/" className="flex items-center gap-2 shrink-0">
          <BrandMark className="size-7" />
          <span className="font-semibold text-ink-50">DorkNet</span>
        </Link>

        <nav className="hidden md:flex items-center gap-1 ml-2">
          {NAV.map(n => (
            <NavLink
              key={n.to}
              to={n.to}
              end={n.exact}
              className={({ isActive }) =>
                `px-3 py-1.5 rounded-md text-sm transition-colors ${
                  isActive ? 'bg-ink-800 text-ink-50' : 'text-ink-300 hover:bg-ink-800/60 hover:text-ink-100'
                }`
              }
            >
              {n.label}
            </NavLink>
          ))}
        </nav>

        <form onSubmit={submitSearch} className="ml-auto hidden sm:flex flex-1 max-w-xs">
          <input
            ref={inputRef}
            value={q}
            onChange={e => setQ(e.target.value)}
            placeholder="Search players…"
            className="input"
          />
        </form>

        <button
          type="button"
          onClick={() => setOpen(o => !o)}
          aria-label="Toggle menu"
          className="md:hidden btn-ghost text-sm ml-auto"
        >
          {open ? 'Close' : 'Menu'}
        </button>
      </div>

      {open && (
        <div className="md:hidden border-t border-ink-800 bg-ink-900">
          <div className="mx-auto max-w-6xl px-4 sm:px-6 py-3 flex flex-col gap-2">
            <form onSubmit={submitSearch} className="flex">
              <input
                value={q}
                onChange={e => setQ(e.target.value)}
                placeholder="Search players…"
                className="input"
              />
            </form>
            <nav className="flex flex-col gap-1">
              {NAV.map(n => (
                <NavLink
                  key={n.to}
                  to={n.to}
                  end={n.exact}
                  className={({ isActive }) =>
                    `px-3 py-2 rounded-md text-sm ${isActive ? 'bg-ink-800 text-ink-50' : 'text-ink-200 hover:bg-ink-800/60'}`
                  }
                >
                  {n.label}
                </NavLink>
              ))}
            </nav>
          </div>
        </div>
      )}
    </header>
  );
}
