import { useEffect, useState } from 'react';
import { get } from '../lib/api';
import type { Stats } from '../lib/types';
import { RefreshCw } from '../components/Icons';

export function Dashboard() {
  const [stats, setStats] = useState<Stats | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const load = async () => {
    setLoading(true);
    setErr(null);
    try {
      const data = await get<Stats>('/stats');
      setStats(data);
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    const id = setInterval(load, 10_000);
    return () => clearInterval(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="space-y-6">
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight text-ink-50">Dashboard</h1>
          <p className="text-sm text-ink-400">Server snapshot, refreshed every 10s.</p>
        </div>
        <button onClick={load} className="btn-secondary text-xs" disabled={loading}>
          <RefreshCw className={loading ? 'animate-spin' : ''} />
          Refresh
        </button>
      </div>

      {err && <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">{err}</div>}

      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <Stat label="Players online" value={stats?.players.onlineNow} accent="success" sub={stats && `${stats.players.total} total`} />
        <Stat label="Banned now"    value={stats?.players.bannedNow} accent="danger"  sub={stats && `${stats.moderation.activeIpBans} IP bans`} />
        <Stat label="Open reports"  value={stats?.moderation.openReports} accent="warn"    />
        <Stat label="Total rooms"   value={stats?.rooms.total}    accent="neutral" sub={stats && `${stats.inventions} inventions`} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="card p-4">
          <h2 className="text-sm font-semibold text-ink-100">Top rooms by visits</h2>
          <p className="text-xs text-ink-400 mb-3">All-time visit counts.</p>
          <div className="table-scroll"><table className="w-full text-sm min-w-[480px]">
            <thead className="text-[11px] uppercase tracking-wider text-ink-400 border-b border-ink-800">
              <tr>
                <th className="text-left font-medium pb-2">Room</th>
                <th className="text-right font-medium pb-2">Visits</th>
                <th className="text-right font-medium pb-2">Visitors</th>
                <th className="text-right font-medium pb-2">Cheers</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-800">
              {stats?.rooms.topByVisits.map(r => (
                <tr key={r.id} className="table-row-hover">
                  <td className="py-2 text-ink-100">{r.name}</td>
                  <td className="py-2 text-right text-ink-200 tabular-nums">{r.visitCount.toLocaleString()}</td>
                  <td className="py-2 text-right text-ink-200 tabular-nums">{r.visitorCount.toLocaleString()}</td>
                  <td className="py-2 text-right text-ink-200 tabular-nums">{r.cheerCount.toLocaleString()}</td>
                </tr>
              ))}
              {!stats && <tr><td colSpan={4} className="py-6 text-center text-ink-400 text-xs">loading…</td></tr>}
            </tbody>
          </table></div>
        </div>

        <div className="card p-4">
          <h2 className="text-sm font-semibold text-ink-100">Recent signups</h2>
          <p className="text-xs text-ink-400 mb-3">Latest 10 accounts.</p>
          <div className="table-scroll"><table className="w-full text-sm min-w-[480px]">
            <thead className="text-[11px] uppercase tracking-wider text-ink-400 border-b border-ink-800">
              <tr>
                <th className="text-left font-medium pb-2">Username</th>
                <th className="text-left font-medium pb-2">Joined</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-800">
              {stats?.recentJoins.map(j => (
                <tr key={j.id} className="table-row-hover">
                  <td className="py-2 text-ink-100">{j.username} <span className="text-ink-500 text-xs">#{j.id}</span></td>
                  <td className="py-2 text-ink-300 text-xs">{new Date(j.createdAt).toLocaleString()}</td>
                </tr>
              ))}
              {!stats && <tr><td colSpan={2} className="py-6 text-center text-ink-400 text-xs">loading…</td></tr>}
            </tbody>
          </table></div>
        </div>
      </div>

      <p className="text-[11px] text-ink-500">Server time: {stats ? new Date(stats.serverTime).toLocaleString() : '—'}</p>
    </div>
  );
}

function Stat({ label, value, sub, accent }: { label: string; value: number | undefined; sub?: string | null; accent: 'success' | 'danger' | 'warn' | 'neutral' }) {
  const accentRing = {
    success: 'before:bg-success',
    danger:  'before:bg-danger',
    warn:    'before:bg-warn',
    neutral: 'before:bg-brand-500',
  }[accent];
  return (
    <div className={`card p-4 relative overflow-hidden before:absolute before:left-0 before:top-0 before:h-full before:w-0.5 ${accentRing}`}>
      <div className="text-[11px] uppercase tracking-widest text-ink-400">{label}</div>
      <div className="mt-1 text-2xl font-semibold tabular-nums text-ink-50">{value?.toLocaleString() ?? '—'}</div>
      {sub && <div className="mt-0.5 text-xs text-ink-400">{sub}</div>}
    </div>
  );
}
