import { NavLink, Link, useLocation, useNavigate, useSearchParams } from 'react-router';
import { useEffect, useRef, useState } from 'react';
import { BrandMark } from './BrandMark';
import { getDownloads, SignInRequiredError } from '../lib/downloads';
import { errorCopy, getMe, signInUrl, signOut, SIGNIN_ERRORS, type AccountMe } from '../lib/account';

const NAV = [
  { to: '/', label: 'Home', exact: true },
  { to: '/feed', label: 'Feed' },
  { to: '/players', label: 'Players' },
  { to: '/rooms', label: 'Rooms' },
  { to: '/join', label: 'Join' },
  { to: '/quest', label: 'Quest' },
];
// Shown only when the server offers downloads (DorkNet:Downloads:BaseUrl).
const DOWNLOADS = { to: '/downloads', label: 'Downloads', exact: false };

export function Header() {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState('');
  const nav = useNavigate();
  const loc = useLocation();
  const inputRef = useRef<HTMLInputElement>(null);
  const [hasDownloads, setHasDownloads] = useState(false);
  const navItems = hasDownloads ? [...NAV, DOWNLOADS] : NAV;

  const [me, setMe] = useState<AccountMe | null>(null);
  const [params, setParams] = useSearchParams();
  const signinError = params.get('signin_error');

  useEffect(() => {
    getDownloads().then((m) => setHasDownloads(m !== null))
      .catch((e) => setHasDownloads(e instanceof SignInRequiredError));
    getMe().then(setMe).catch(() => { /* site works without sign-in */ });
  }, []);

  const [signOutError, setSignOutError] = useState<string | null>(null);
  const doSignOut = async () => {
    setSignOutError(null);
    try {
      await signOut();
      window.location.assign('/');
    } catch (e) {
      setSignOutError((e as Error).message);
    }
  };

  // Sign in / account, shown only when the server has a sign-in provider.
  const account = !me?.signInAvailable ? null : me.signedIn && me.player ? (
    <span className="flex items-center gap-2 text-sm">
      <Link to="/account" className="text-ink-200 hover:text-ink-50">{me.player.username}</Link>
      <button type="button" onClick={doSignOut} className="btn-ghost !px-2 !py-1 text-xs">Sign out</button>
    </span>
  ) : (
    <a href={signInUrl()} className="btn-secondary !px-3 !py-1.5 text-sm">Sign in</a>
  );

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
          {navItems.map(n => (
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

        <div className="hidden md:flex shrink-0">{account}</div>

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
              {navItems.map(n => (
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
            {account && <div className="pt-1">{account}</div>}
          </div>
        </div>
      )}

      {signOutError && (
        <div className="border-t border-danger/30 bg-danger/10">
          <div className="mx-auto max-w-6xl px-4 sm:px-6 py-2 flex items-center justify-between gap-3 text-sm text-danger">
            <span>Couldn't sign out ({signOutError}). You're still signed in; try again.</span>
            <button type="button" className="btn-ghost !px-2 !py-1 text-xs" onClick={() => setSignOutError(null)}>Dismiss</button>
          </div>
        </div>
      )}

      {signinError && (
        <div className="border-t border-danger/30 bg-danger/10">
          <div className="mx-auto max-w-6xl px-4 sm:px-6 py-2 flex items-center justify-between gap-3 text-sm text-danger">
            <span>Sign-in didn't work: {errorCopy(SIGNIN_ERRORS, signinError, 'something went wrong. Try again.')}</span>
            <button type="button" className="btn-ghost !px-2 !py-1 text-xs" onClick={() => { params.delete('signin_error'); setParams(params, { replace: true }); }}>Dismiss</button>
          </div>
        </div>
      )}
    </header>
  );
}
