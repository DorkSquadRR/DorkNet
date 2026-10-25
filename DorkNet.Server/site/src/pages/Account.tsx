import { useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { getMe, signInUrl, signOut, unlink, type AccountMe } from '../lib/account';

// The signed-in account: which DorkNet account and provider email are
// linked, plus sign out and unlink.
export function Account() {
  const [me, setMe] = useState<AccountMe | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    getMe(true).then(setMe).catch((e) => setErr((e as Error).message));
  }, []);

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    try {
      await action();
      window.location.assign('/');
    } catch (e) {
      setErr((e as Error).message);
      setBusy(false);
    }
  };

  if (err) return <div className="max-w-lg mx-auto rounded-lg border border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">{err}</div>;
  if (!me) return <p className="max-w-lg mx-auto text-sm text-ink-400">Loading…</p>;

  if (!me.signedIn || !me.player) {
    return (
      <div className="max-w-lg mx-auto card !p-6 space-y-3 text-sm text-ink-300">
        <h1 className="text-xl font-semibold text-ink-50">Account</h1>
        <p>You're not signed in.</p>
        {me.signInAvailable
          ? <a className="btn-primary inline-block" href={signInUrl('/account')}>Sign in with {me.provider}</a>
          : <p className="text-ink-400">Sign-in isn't set up on this server.</p>}
      </div>
    );
  }

  return (
    <div className="max-w-lg mx-auto space-y-4">
      <h1 className="text-2xl font-semibold text-ink-50">Account</h1>
      <div className="card !p-6 space-y-3 text-sm">
        <div className="flex justify-between gap-3">
          <span className="text-ink-400">DorkNet account</span>
          <RouterLink className="text-brand-300 hover:text-brand-200" to={`/players/${me.player.id}`}>{me.player.username}</RouterLink>
        </div>
        <div className="flex justify-between gap-3">
          <span className="text-ink-400">Signed in with {me.provider}</span>
          <span className="text-ink-200">{me.player.email || '—'}</span>
        </div>
        <div className="flex flex-wrap gap-2 pt-2">
          <button type="button" className="btn-secondary" disabled={busy} onClick={() => run(signOut)}>Sign out</button>
          <button type="button" className="btn-ghost text-danger" disabled={busy}
                  onClick={() => { if (window.confirm(`Unlink this sign-in from ${me.player!.username}? You'll need the account's password to link it again.`)) void run(unlink); }}>
            Unlink
          </button>
        </div>
      </div>
    </div>
  );
}
