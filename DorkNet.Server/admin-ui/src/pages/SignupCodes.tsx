import { useEffect, useState } from 'react';
import { api, get } from '../lib/api';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';
import { Plus, RefreshCw } from '../components/Icons';

interface SignupCode {
  id: number;
  code: string;
  descriptor: string;
  createdAt: string;
  expiresAt: string | null;
  revoked: boolean;
  redeemedAt: string | null;
  redeemedByPlayerId: number | null;
  redeemedByUsername: string | null;
  status: 'active' | 'redeemed' | 'revoked' | 'expired';
}

const STATUS_BADGE: Record<SignupCode['status'], string> = {
  active: 'badge-online',
  redeemed: 'badge-admin',
  revoked: 'badge-banned',
  expired: 'badge-neutral',
};

export function SignupCodes({ embedded }: { embedded?: boolean } = {}) {
  const [codes, setCodes] = useState<SignupCode[] | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [descriptor, setDescriptor] = useState('');
  const [expiresAt, setExpiresAt] = useState('');
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const load = async () => {
    try {
      setCodes(await get<SignupCode[]>('/signup-codes'));
      setErr(null);
    } catch (e) {
      setErr((e as Error).message);
    }
  };

  useEffect(() => { void load(); }, []);

  const generate = async () => {
    setBusy(true);
    try {
      const created = await api<{ code: string }>('/signup-codes', {
        method: 'POST',
        body: {
          Descriptor: descriptor.trim(),
          ExpiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
        },
      });
      setDescriptor('');
      setExpiresAt('');
      toast.push(`Code ${created.code} created.`, 'success');
      await load();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const revoke = async (id: number, code: string) => {
    if (!confirm(`Revoke code ${code}? It can no longer be redeemed.`)) return;
    try {
      await api(`/signup-codes/${id}/revoke`, { method: 'POST' });
      toast.push('Code revoked.', 'success');
      await load();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const copy = async (code: string) => {
    try {
      await navigator.clipboard.writeText(code);
      toast.push('Code copied.', 'success');
    } catch {
      toast.push('Copy failed — select it manually.', 'error');
    }
  };

  const refreshBtn = (
    <button onClick={load} className="btn-secondary text-xs">
      <RefreshCw />
      Refresh
    </button>
  );

  return (
    <div>
      {embedded ? (
        <div className="flex justify-end mb-3">{refreshBtn}</div>
      ) : (
        <PageHeader
          title="Signup codes"
          blurb="Single-use invite codes. Hand one to a player; they redeem it on the site's /join page to create an account while signups are disabled."
          actions={refreshBtn}
        />
      )}

      {err && (
        <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger mb-4">{err}</div>
      )}

      <div className="card !p-5 max-w-2xl">
        <h2 className="text-sm font-semibold text-ink-50">Generate a code</h2>
        <div className="mt-3 grid gap-3 md:grid-cols-[1fr_auto]">
          <label className="block">
            <span className="label">Descriptor (who is this for?)</span>
            <input
              className="input mt-1"
              value={descriptor}
              onChange={(e) => setDescriptor(e.target.value)}
              placeholder="e.g. Phil's friend Dana"
            />
          </label>
          <label className="block">
            <span className="label">Expires (optional)</span>
            <input
              type="datetime-local"
              className="input mt-1"
              value={expiresAt}
              onChange={(e) => setExpiresAt(e.target.value)}
            />
          </label>
        </div>
        <div className="mt-3">
          <button onClick={generate} disabled={busy} className="btn-primary text-xs">
            <Plus />
            {busy ? 'Generating…' : 'Generate code'}
          </button>
        </div>
      </div>

      <div className="card !p-0 mt-5 overflow-hidden">
        {!codes && !err && <div className="p-6 text-center text-xs text-ink-400">Loading…</div>}
        {codes && codes.length === 0 && (
          <div className="p-6 text-center text-xs text-ink-400">No codes yet — generate one above.</div>
        )}
        {codes && codes.length > 0 && (
          <table className="w-full text-sm">
            <thead className="text-left text-[11px] uppercase tracking-wide text-ink-500 border-b border-ink-800">
              <tr>
                <th className="px-4 py-2">Code</th>
                <th className="px-4 py-2">Descriptor</th>
                <th className="px-4 py-2">Status</th>
                <th className="px-4 py-2">Redeemed by</th>
                <th className="px-4 py-2">Expires</th>
                <th className="px-4 py-2"></th>
              </tr>
            </thead>
            <tbody>
              {codes.map((c) => (
                <tr key={c.id} className="border-b border-ink-900/60">
                  <td className="px-4 py-2 font-mono text-ink-100">{c.code}</td>
                  <td className="px-4 py-2 text-ink-300">{c.descriptor || <span className="text-ink-600">—</span>}</td>
                  <td className="px-4 py-2"><span className={STATUS_BADGE[c.status]}>{c.status}</span></td>
                  <td className="px-4 py-2 text-ink-300">
                    {c.redeemedByUsername
                      ? <span>{c.redeemedByUsername} <span className="text-ink-600">({c.redeemedByPlayerId})</span></span>
                      : <span className="text-ink-600">—</span>}
                  </td>
                  <td className="px-4 py-2 text-ink-400 text-xs">
                    {c.expiresAt ? new Date(c.expiresAt).toLocaleString() : <span className="text-ink-600">never</span>}
                  </td>
                  <td className="px-4 py-2 text-right whitespace-nowrap">
                    <button onClick={() => copy(c.code)} className="btn-ghost !px-2 !py-1 text-xs">Copy</button>
                    {c.status === 'active' && (
                      <button onClick={() => revoke(c.id, c.code)} className="btn-ghost !px-2 !py-1 text-xs text-danger">Revoke</button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
