import { useState } from 'react';
import { api } from '../lib/api';
import type { IpBan, Player } from '../lib/types';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';
import { Empty } from '../components/Empty';
import { absoluteTime, relativeTime } from '../lib/format';
import { RefreshCw, Trash } from '../components/Icons';

export function Bans({ embedded }: { embedded?: boolean } = {}) {
  const [tab, setTab] = useState<'players' | 'ip'>('players');
  return (
    <div>
      {!embedded && (
        <PageHeader
          title="Bans"
          blurb="Player bans and IP-level bans, all in one place."
        />
      )}
      <div className="flex border-b border-ink-800 text-sm mb-4">
        {(['players', 'ip'] as const).map(t => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`px-4 py-2 -mb-px border-b-2 ${tab === t ? 'border-brand-500 text-ink-50' : 'border-transparent text-ink-300 hover:text-ink-100'}`}
          >
            {t === 'players' ? 'Player bans' : 'IP bans'}
          </button>
        ))}
      </div>
      {tab === 'players' && <PlayerBans />}
      {tab === 'ip' && <IpBans />}
    </div>
  );
}

function PlayerBans() {
  // Pull a generous page of players and filter client-side for currently-banned.
  // For a small private server this is fine; if we ever scale we can add a
  // dedicated /players/banned endpoint.
  const { data, loading, error, refresh } = useApi<Player[]>('/players?take=200');
  const now = Date.now();
  const banned = (data ?? []).filter(p => p.bannedUntil && new Date(p.bannedUntil).getTime() > now);
  const toast = useToast();

  const unban = async (id: number) => {
    try {
      await api(`/players/${id}/unban`, { method: 'POST', body: { Reason: 'admin-unban' } });
      toast.push('Unbanned', 'success');
      refresh();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  return (
    <div className="card overflow-hidden">
      <div className="flex items-center justify-between border-b border-ink-800 px-4 py-2.5">
        <div className="text-xs text-ink-400">{banned.length} active player ban{banned.length === 1 ? '' : 's'}</div>
        <button onClick={refresh} className="btn-ghost text-xs" disabled={loading}>
          <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
        </button>
      </div>
      {error && <div className="px-4 py-3 text-sm text-danger">{error}</div>}
      {!error && banned.length === 0 && <Empty title="No active player bans" blurb="Issued bans appear here until they expire or are lifted." />}
      {banned.length > 0 && (
        <div className="table-scroll"><table className="w-full text-sm min-w-[640px]">
          <thead className="text-[11px] uppercase tracking-wider text-ink-400 bg-ink-900/50 border-b border-ink-800">
            <tr>
              <th className="text-left font-medium px-4 py-2.5">Player</th>
              <th className="text-left font-medium px-4 py-2.5">Expires</th>
              <th className="text-left font-medium px-4 py-2.5">Last IP</th>
              <th />
            </tr>
          </thead>
          <tbody className="divide-y divide-ink-800">
            {banned.map(p => (
              <tr key={p.id} className="table-row-hover">
                <td className="px-4 py-2.5">
                  <div className="font-medium text-ink-50">{p.displayName || p.username}</div>
                  <div className="text-xs text-ink-400">@{p.username} · #{p.id}</div>
                </td>
                <td className="px-4 py-2.5 text-ink-200 text-xs" title={absoluteTime(p.bannedUntil)}>
                  {relativeTime(p.bannedUntil)} <span className="text-ink-500">({absoluteTime(p.bannedUntil)})</span>
                </td>
                <td className="px-4 py-2.5 text-ink-300 font-mono text-xs">{p.lastIp ?? '—'}</td>
                <td className="px-4 py-2.5 text-right">
                  <button onClick={() => unban(p.id)} className="btn-secondary text-xs">Unban</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table></div>
      )}
    </div>
  );
}

function IpBans() {
  const { data, loading, error, refresh } = useApi<IpBan[]>('/ipbans');
  const toast = useToast();
  const [cidr, setCidr] = useState('');
  const [reason, setReason] = useState('');
  const [days, setDays] = useState<number | ''>('');

  const add = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!cidr.trim()) return;
    try {
      await api('/ipbans', { method: 'POST', body: { Cidr: cidr.trim(), Reason: reason, DurationDays: days || null } });
      setCidr(''); setReason(''); setDays('');
      toast.push('IP ban added', 'success');
      refresh();
    } catch (err) {
      toast.push((err as Error).message, 'error');
    }
  };

  const remove = async (id: number) => {
    if (!confirm('Remove this IP ban?')) return;
    try {
      await api(`/ipbans/${id}`, { method: 'DELETE' });
      toast.push('IP ban removed', 'success');
      refresh();
    } catch (err) {
      toast.push((err as Error).message, 'error');
    }
  };

  const active = (b: IpBan) => !b.until || new Date(b.until).getTime() > Date.now();

  return (
    <div className="space-y-4">
      <form onSubmit={add} className="card !p-4 grid grid-cols-1 md:grid-cols-[1fr,1fr,140px,auto] gap-2">
        <input value={cidr} onChange={e => setCidr(e.target.value)} placeholder="1.2.3.4 or 10.0.0.0/24" required className="input" />
        <input value={reason} onChange={e => setReason(e.target.value)} placeholder="Reason (logged)" className="input" />
        <input
          type="number"
          min={1}
          max={3650}
          value={days}
          onChange={e => setDays(e.target.value === '' ? '' : parseInt(e.target.value))}
          placeholder="days (∞)"
          className="input"
        />
        <button className="btn-primary text-xs">Add ban</button>
      </form>

      <div className="card overflow-hidden">
        <div className="flex items-center justify-between border-b border-ink-800 px-4 py-2.5">
          <div className="text-xs text-ink-400">{data ? `${data.length} total · ${data.filter(active).length} active` : ''}</div>
          <button onClick={refresh} className="btn-ghost text-xs" disabled={loading}>
            <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
          </button>
        </div>
        {error && <div className="px-4 py-3 text-sm text-danger">{error}</div>}
        {data && data.length === 0 && <Empty title="No IP bans" blurb="Add a CIDR above to start blocking ranges." />}
        {data && data.length > 0 && (
          <div className="table-scroll"><table className="w-full text-sm min-w-[640px]">
            <thead className="text-[11px] uppercase tracking-wider text-ink-400 bg-ink-900/50 border-b border-ink-800">
              <tr>
                <th className="text-left font-medium px-4 py-2.5">CIDR</th>
                <th className="text-left font-medium px-4 py-2.5">Reason</th>
                <th className="text-left font-medium px-4 py-2.5">Issued</th>
                <th className="text-left font-medium px-4 py-2.5">Expires</th>
                <th />
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-800">
              {data.map(b => (
                <tr key={b.id} className="table-row-hover">
                  <td className="px-4 py-2.5 font-mono text-xs text-ink-100">{b.cidr}</td>
                  <td className="px-4 py-2.5 text-ink-300">{b.reason || '—'}</td>
                  <td className="px-4 py-2.5 text-ink-300 text-xs" title={absoluteTime(b.bannedAt)}>{relativeTime(b.bannedAt)}</td>
                  <td className="px-4 py-2.5 text-ink-300 text-xs">
                    {b.until ? <span title={absoluteTime(b.until)}>{relativeTime(b.until)}</span> : <span className="text-ink-500">∞</span>}
                  </td>
                  <td className="px-4 py-2.5 text-right">
                    <button onClick={() => remove(b.id)} className="btn-ghost text-xs text-danger">
                      <Trash /> Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table></div>
        )}
      </div>
    </div>
  );
}
