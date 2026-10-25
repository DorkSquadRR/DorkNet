import { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router';
import { getMe, link, LINK_ERRORS, signInUrl, type AccountMe } from '../lib/account';

// After a first sign-in with the provider: prove which DorkNet account this
// identity belongs to, once, with that account's password.
export function Link() {
  const [params] = useSearchParams();
  const returnUrl = params.get('returnUrl') || '/';
  const navigate = useNavigate();
  const [me, setMe] = useState<AccountMe | null>(null);
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    getMe(true).then((m) => {
      if (m.signedIn) navigate(returnUrl, { replace: true });
      else setMe(m);
    }).catch((e) => setErr((e as Error).message));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      await link(username.trim(), password);
      await getMe(true);
      window.location.assign(returnUrl); // the header re-reads who is signed in
    } catch (e) {
      const key = (e as Error).message;
      setErr(LINK_ERRORS[key] ?? key);
    } finally {
      setBusy(false);
    }
  };

  if (me && !me.pendingLink) {
    return (
      <div className="max-w-lg mx-auto card !p-6 space-y-3 text-sm text-ink-300">
        <h1 className="text-xl font-semibold text-ink-50">Link your account</h1>
        <p>Your sign-in expired before it was linked.</p>
        {me.signInAvailable && <a className="btn-primary inline-block" href={signInUrl(returnUrl)}>Sign in again</a>}
      </div>
    );
  }

  return (
    <div className="max-w-lg mx-auto space-y-4">
      <div>
        <h1 className="text-2xl font-semibold text-ink-50">Link your DorkNet account</h1>
        <p className="text-sm text-ink-400">
          You signed in{me?.pendingLink?.email ? <> as <span className="text-ink-200">{me.pendingLink.email}</span></> : null}.
          Enter your DorkNet username and password once to connect them; after that, signing in is enough.
        </p>
      </div>
      <form onSubmit={submit} className="card !p-6 space-y-4">
        <div className="space-y-1.5">
          <label className="label" htmlFor="username">DorkNet username</label>
          <input id="username" className="input" autoComplete="username" required
                 value={username} onChange={(e) => setUsername(e.target.value)} />
        </div>
        <div className="space-y-1.5">
          <label className="label" htmlFor="password">Password</label>
          <input id="password" type="password" className="input" autoComplete="current-password" required
                 value={password} onChange={(e) => setPassword(e.target.value)} />
        </div>
        {err && <div className="rounded-lg border border-danger/30 bg-danger/5 px-3 py-2 text-sm text-danger">{err}</div>}
        <button type="submit" disabled={busy} className="btn-primary w-full">{busy ? 'Linking…' : 'Link account'}</button>
        <p className="text-xs text-ink-500">
          No account yet? <RouterLink className="text-brand-300 hover:text-brand-200" to="/join">Join with a signup code</RouterLink>, then come back here.
        </p>
      </form>
    </div>
  );
}
